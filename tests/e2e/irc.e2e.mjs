// End-to-end runs of the IRC app inside the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true`. Each test boots a fresh browser profile.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium, devices } from 'playwright';
import { boot, ircOsWindow, openIrc, startHost } from './host.mjs';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await chromium.launch();
});

after(async () => {
    await browser?.close();
    host?.stop();
});

const box = async locator => {
    const value = await locator.boundingBox();
    assert.ok(value, 'the element is on screen');
    return value;
};

test('on the desktop the window shows three columns side by side, in the OS theme', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page);

    // The window opens with an animation: measure once it has its final size.
    await page.getByText('No network yet.').waitFor();
    await page.waitForFunction(() => {
        const width = document.querySelector('.irc-users')?.getBoundingClientRect().width;
        const settled = width === window.__ircLastWidth;
        window.__ircLastWidth = width;
        return settled && width > 0;
    }, null, { polling: 250 });
    const channels = await box(page.locator('.irc-channels'));
    const conversation = await box(page.locator('.irc-conversation'));
    const users = await box(page.locator('.irc-users'));
    assert.ok(channels.x + channels.width <= conversation.x + 1, `the channels are left of the messages: ${JSON.stringify({ channels, conversation, users })}`);
    assert.ok(conversation.x + conversation.width <= users.x + 1, 'the users are right of the messages');
    assert.ok(conversation.width > channels.width, 'the messages take the most room');

    await page.getByText('No network yet.').waitFor();
    await page.getByText('Join a network to start chatting.').waitFor();
    assert.equal(await page.getByRole('textbox', { name: 'Message' }).isDisabled(), true);
    // The side panels' buttons belong to the compact layout only.
    assert.equal(await page.locator('.irc-panel-toggle').first().isVisible(), false);
    assert.equal(await page.locator('.irc-scrim').count(), 0);
    // The OS theme's background, not the browser's white.
    const background = await page.locator('.irc-window').evaluate(element => getComputedStyle(element).backgroundColor);
    assert.notEqual(background, 'rgb(255, 255, 255)');
    assert.deepEqual(errors, []);
    await page.close();
});

test('the console font is an option kept for the next time the app opens', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await openIrc(page);

    // The theme's monospace font, as the browser resolves it (the default theme's interface font
    // may already be a monospace one, so the test compares with this font, not with the first one).
    const monospace = await page.locator('.irc-window').evaluate(element => {
        const probe = document.createElement('span');
        probe.style.fontFamily = 'var(--kos-font-mono)';
        element.appendChild(probe);
        const family = getComputedStyle(probe).fontFamily;
        probe.remove();
        return family;
    });
    const font = () => page.locator('.message-log').evaluate(element => getComputedStyle(element).fontFamily);
    const toggle = page.getByRole('button', { name: 'Console font' });
    assert.equal(await toggle.getAttribute('aria-pressed'), 'false');
    await toggle.click();
    await page.locator('.irc-window.monospace').waitFor({ timeout: 10_000 });
    assert.equal(await toggle.getAttribute('aria-pressed'), 'true');
    assert.equal(await font(), monospace);

    // Closed and opened again, the window reads ~/.config/ca.kazo.irc/settings.json.
    await ircOsWindow(page).locator('.chrome-button-close').click();
    await page.locator('.irc-window').waitFor({ state: 'detached', timeout: 15_000 });
    await openIrc(page);
    await page.locator('.irc-window.monospace').waitFor({ timeout: 15_000 });
    assert.equal(await font(), monospace);
    await page.close();
});

test('on a phone the window is one column and the channels and users slide in as panels', { timeout: 300_000 }, async () => {
    const context = await browser.newContext({ ...devices['Pixel 7'] });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page, { mobile: true });

    const viewport = page.viewportSize();
    const conversation = await box(page.locator('.irc-conversation'));
    assert.ok(conversation.width > viewport.width * 0.9, `the messages are ${conversation.width}px wide on a ${viewport.width}px screen`);
    assert.equal(await page.locator('.irc-channels').isVisible(), false);
    assert.equal(await page.locator('.irc-users').isVisible(), false);

    // The channels: opened from the toolbar, over the messages, closed by the backdrop.
    const channelsButton = page.getByRole('button', { name: 'Channels', exact: true });
    await channelsButton.tap();
    await page.locator('.irc-channels.open').waitFor({ timeout: 10_000 });
    assert.equal(await channelsButton.getAttribute('aria-expanded'), 'true');
    await page.getByText('No network yet.').waitFor({ state: 'visible', timeout: 10_000 });
    const channels = await box(page.locator('.irc-channels'));
    assert.ok(channels.x >= -1 && channels.x + channels.width <= viewport.width + 1, 'the panel is on screen');
    await page.locator('.irc-scrim').tap({ position: { x: viewport.width - 10, y: 200 } });
    await page.locator('.irc-channels.open').waitFor({ state: 'detached', timeout: 10_000 });

    // The users: the other side; opening them closes the channels, its × closes them.
    await channelsButton.tap();
    await page.locator('.irc-channels.open').waitFor({ timeout: 10_000 });
    await page.getByRole('button', { name: 'Users', exact: true }).tap();
    await page.locator('.irc-users.open').waitFor({ timeout: 10_000 });
    assert.equal(await page.locator('.irc-channels.open').count(), 0);
    await page.getByText('Nobody here yet.').waitFor({ state: 'visible', timeout: 10_000 });
    await page.locator('.irc-users .irc-panel-close').tap();
    await page.locator('.irc-users.open').waitFor({ state: 'detached', timeout: 10_000 });
    assert.deepEqual(errors, []);
    await context.close();
});
