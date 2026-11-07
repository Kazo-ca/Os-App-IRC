// End-to-end runs of the chat on the demonstration network (work item 4): `npm run test:e2e` after
// `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true`. Each test boots a fresh profile.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium, devices } from 'playwright';
import { boot, connectDemo, ircOsWindow, openIrc, runInTerminal, say, startHost } from './host.mjs';

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

test('join a channel, talk, get a mention while away: notification, title counter and log file', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page);

    await connectDemo(page);
    const nick = (await page.locator('.message-line', { hasText: 'Connected as' }).innerText()).match(/Connected as (\S+)\./)[1];
    assert.equal(nick, 'neo', 'the nickname is the OS user name (decision 12)');

    // /join opens the channel with its topic and users (prefixes).
    await say(page, '/join #kazo');
    await page.locator('.irc-title', { hasText: '#kazo' }).waitFor({ timeout: 15_000 });
    await page.locator('.irc-topic', { hasText: 'demonstration network' }).waitFor();
    await page.locator('.user-row', { hasText: 'kazo-bot' }).waitFor();
    assert.equal((await page.locator('.user-row', { hasText: 'kazo-bot' }).locator('.user-prefix').innerText()).trim(), '@');

    // Own message, then the bot's answer that names the user (a mention, highlighted).
    await say(page, 'kazo-bot: hello');
    await page.locator('.message-line.own', { hasText: 'kazo-bot: hello' }).waitFor();
    await page.locator('.message-line.mention', { hasText: 'neo: hello!' }).waitFor({ timeout: 15_000 });
    // Seen in the open channel with the focus: no counter.
    assert.equal(await page.locator('.channel-badge').count(), 0);

    // Up arrow brings the last line back; Tab completes a nickname.
    const input = page.getByRole('textbox', { name: 'Message' });
    await input.press('ArrowUp');
    assert.equal(await input.inputValue(), 'kazo-bot: hello');
    await input.fill('morph');
    await input.press('Tab');
    assert.equal(await input.inputValue(), 'morpheus: ');
    assert.equal(await input.evaluate(element => document.activeElement === element), true, 'Tab keeps the focus in the input');

    // The bot names the user in 4 s; meanwhile the Terminal takes the focus and reads the log.
    await say(page, '!mention 4');
    const day = new Date().toISOString().slice(0, 10);
    const cat = `cat "/home/neo/.local/share/ca.kazo.irc/logs/Kazo Demo/#kazo/${day}.log"`;
    await runInTerminal(page, cat);
    // The OS file system may take a few seconds to show the last appends to another app: read again.
    const logged = page.locator('.xterm-rows').getByText('<neo> kazo-bot: hello', { exact: false }).first();
    for (let attempt = 0; ; attempt++) {
        try {
            await logged.waitFor({ timeout: 4_000 });
            break;
        } catch (error) {
            if (attempt === 5) {
                throw error;
            }
            await page.keyboard.type(`${cat}\n`, { delay: 5 });
        }
    }
    await page.locator('.xterm-rows').getByText('*** neo joined', { exact: false }).first().waitFor();

    // Decision 14: an OS notification, and the counter in the IRC window's title.
    await page.getByText('kazo-bot in #kazo').first().waitFor({ timeout: 20_000 });
    await ircOsWindow(page).getByText('(1) IRC').first().waitFor({ timeout: 10_000 });
    await page.locator('.channel-row.highlight', { hasText: '#kazo' }).waitFor();

    // Back to the IRC window (the Terminal closed, a click in it): the mention is seen, the counter goes.
    await page.locator('[data-window-id]', { has: page.locator('.xterm') }).locator('.chrome-button-close').click();
    await page.locator('.irc-conversation').click({ position: { x: 200, y: 100 } });
    await ircOsWindow(page).getByText('(1) IRC').first().waitFor({ state: 'detached', timeout: 10_000 });
    assert.equal(await page.locator('.channel-row.highlight').count(), 0);
    assert.deepEqual(errors, []);
    await page.close();
});

test('a private message from the bot opens a conversation marked for the user', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await openIrc(page);
    await connectDemo(page);
    await say(page, '/join #kazo');
    await page.locator('.irc-title', { hasText: '#kazo' }).waitFor({ timeout: 15_000 });

    await say(page, '!private 1');
    const row = page.locator('.channel-row.kind-private.highlight', { hasText: 'kazo-bot' });
    await row.waitFor({ timeout: 15_000 });
    assert.equal((await row.locator('.channel-badge').innerText()).trim(), '1');
    // The user stays where they are; the title counts it.
    await page.locator('.irc-title', { hasText: '#kazo' }).waitFor();
    await ircOsWindow(page).getByText('(1) IRC').first().waitFor({ timeout: 10_000 });

    await row.locator('.channel-select').click();
    await page.locator('.message-line', { hasText: 'this is a private message' }).waitFor();
    await ircOsWindow(page).getByText('(1) IRC').first().waitFor({ state: 'detached', timeout: 10_000 });
    await page.close();
});

test('closing the window disconnects; opened again it starts from the list of networks', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page);
    await connectDemo(page);

    await ircOsWindow(page).locator('.chrome-button-close').click();
    await page.locator('.irc-window').waitFor({ state: 'detached', timeout: 15_000 });
    // Opened again: a new window, not connected.
    await openIrc(page);
    await page.locator('.channel-connect', { hasText: 'Kazo Demo' }).waitFor();
    assert.deepEqual(errors, []);
    await page.close();
});

test('on a phone the conversation is joined from the channels panel', { timeout: 300_000 }, async () => {
    const context = await browser.newContext({ ...devices['Pixel 7'] });
    const page = await context.newPage();
    await boot(page, host.baseUrl);
    await openIrc(page, { mobile: true });

    await page.getByRole('button', { name: 'Channels', exact: true }).tap();
    await page.locator('.irc-channels.open').waitFor({ timeout: 10_000 });
    await page.locator('.channel-connect', { hasText: 'Kazo Demo' }).tap();
    // Choosing a network closes the panel and shows its server tab.
    await page.locator('.irc-channels.open').waitFor({ state: 'detached', timeout: 10_000 });
    await page.locator('.message-line', { hasText: 'Connected as' }).waitFor({ timeout: 30_000 });

    await say(page, '/join #kazo');
    await page.locator('.irc-title', { hasText: '#kazo' }).waitFor({ timeout: 15_000 });
    await say(page, 'hello from a phone');
    await page.locator('.message-line.own', { hasText: 'hello from a phone' }).waitFor();
    const line = await page.locator('.message-line.own').last().boundingBox();
    assert.ok(line && line.width > 0 && line.x >= 0, 'the line is on screen');
    await context.close();
});
