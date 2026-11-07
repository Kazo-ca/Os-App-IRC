// End-to-end runs of the real networks (work item 3): `npm run test:e2e` after
// `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true`. No real IRC network is reached:
// the WebSocket of the test network is answered by a small IRC server inside the test
// (fake-irc-server.mjs), through Playwright's routeWebSocket. The relay needs a Kazo server, which
// the IRC host is not: the test checks the clear message instead, and the relay itself is covered
// by the xUnit tests.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { fakeIrcServer } from './fake-irc-server.mjs';
import { boot, openIrc, say, startHost } from './host.mjs';

const TEST_URL = 'wss://irc.e2e.example/';

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

test('a network added in the editor connects over WebSocket without a Kazo server', { timeout: 300_000 }, async () => {
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const record = { lines: [] };
    await context.routeWebSocket(TEST_URL, ws => fakeIrcServer(ws, record));
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page);

    // The proposed networks (decision 13): Libera.Chat and OFTC need the Kazo server, UnrealIRCd does not.
    const libera = page.locator('.channel-connect', { hasText: 'Libera.Chat' });
    await libera.locator('.channel-connect-note', { hasText: 'Kazo server required' }).waitFor({ timeout: 15_000 });
    await page.locator('.channel-connect', { hasText: 'OFTC' }).locator('.channel-connect-note').waitFor();
    assert.equal(await page.locator('.channel-connect', { hasText: 'UnrealIRCd' }).locator('.channel-connect-note').count(), 0);

    // The editor adds a WebSocket network, written to networks.json.
    await page.getByRole('button', { name: 'Manage networks' }).click();
    const editor = page.locator('.network-editor');
    await editor.getByRole('button', { name: /Add a network/ }).click();
    await editor.getByLabel('Name', { exact: true }).fill('E2E');
    await editor.getByLabel('WebSocket address').fill(TEST_URL);
    await editor.getByLabel('Channels joined on connection').fill('#e2e');
    await editor.getByRole('button', { name: 'Save' }).click();
    await editor.locator('.network-editor-row', { hasText: 'E2E' }).waitFor({ timeout: 10_000 });
    await editor.getByRole('button', { name: 'Close' }).click();
    await editor.waitFor({ state: 'detached' });

    // Connecting: the IRCv3 WebSocket, the OS user name as the nickname, the automatic channel.
    await page.locator('.channel-connect', { hasText: 'E2E' }).click();
    await page.locator('.message-line', { hasText: 'Connected as neo.' }).waitFor({ timeout: 30_000 });
    assert.ok(record.lines.includes('NICK neo'), `the client registered as neo: ${record.lines.join(' | ')}`);
    assert.ok(record.lines.every(line => !line.endsWith('\r\n')), 'a WebSocket message carries no CR LF');
    await page.locator('.channel-row', { hasText: '#e2e' }).click();
    await page.locator('.irc-topic', { hasText: 'Topic over WebSocket' }).waitFor({ timeout: 15_000 });
    await page.locator('.user-row', { hasText: 'echo' }).waitFor();

    await say(page, 'hello over WebSocket');
    await page.locator('.message-line.own', { hasText: 'hello over WebSocket' }).waitFor();
    await page.locator('.message-line', { hasText: 'neo said: hello over WebSocket' }).waitFor({ timeout: 15_000 });
    assert.ok(record.lines.includes('PRIVMSG #e2e :hello over WebSocket'));
    assert.deepEqual(errors, []);
    await context.close();
});

test('a network that needs the Kazo server says clearly that none is connected', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openIrc(page);

    await page.locator('.channel-connect', { hasText: 'Libera.Chat' }).click();
    await page.locator('.message-line', { hasText: 'Libera.Chat needs the Kazo server' }).waitFor({ timeout: 30_000 });
    // The networks that need it are now marked « no Kazo server connected ».
    await page.locator('.channel-connect', { hasText: 'OFTC' })
        .locator('.channel-connect-note.unavailable', { hasText: 'No Kazo server connected' })
        .waitFor({ timeout: 10_000 });
    assert.deepEqual(errors, []);
    await page.close();
});
