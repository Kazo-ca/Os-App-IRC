// Screenshots of the Kazo OS Store listing (store/screenshots/N.png), taken in the IRC host:
// `npm run store:screenshots` after `dotnet build src/Kazo.Irc.Os -c Release` (the Store build,
// without the demonstration network, so the network list is the one users get).
//
// Each scene boots a fresh Kazo OS profile and opens IRC. The proposed WebSocket network,
// wss://irc.unrealircd.org/, is answered by the fake server of tests/e2e (fake-irc-server.mjs):
// no real network is reached, and the people and lines are made up. Not part of CI: the
// screenshots are checked in, and taken again when the app's look changes.
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium, devices } from 'playwright';
import { fakeIrcServer } from '../e2e/fake-irc-server.mjs';
import { boot, openIrc, say, startHost } from '../e2e/host.mjs';

const output = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'store', 'screenshots');
const UNREALIRCD = 'wss://irc.unrealircd.org/';
const DESKTOP = { viewport: { width: 1280, height: 800 } };

// IRC formatting codes: \x02 bold, \x03NN color, \x0F reset.
const CHANNELS = {
    '#kazo-os': {
        topic: '\x02Kazo OS\x0F, an OS in your browser | https://os.kazo.ca | be nice',
        users: ['@morpheus', '@trinity', '+tank', 'switch', 'apoc', 'mouse'],
        history: [
            'morpheus: welcome to #kazo-os, the channel of the OS that runs in a browser tab',
            'trinity: the IRC app joined the Store: WebSocket networks work with no server at all',
            'tank: and Libera.Chat or OFTC go through the Kazo server relay, TLS on 6697',
            'switch: \x0303logs\x0F are plain text in ~/.local/share/ca.kazo.irc/logs/, one file a day',
            'apoc: release notes: https://github.com/Kazo-ca/Os-App-IRC',
            'trinity: neo, try /join #help, someone asked about SASL',
        ],
    },
    '#help': {
        topic: 'Questions about Kazo OS apps',
        users: ['@trinity', 'mouse'],
        history: ['mouse: how do I keep my SASL password? -- in the OS keyring, the app never writes it to a file'],
    },
};

let host;

/** A booted Kazo OS with IRC open and connected to UnrealIRCd, in #kazo-os. */
async function connected(browser, contextOptions, { mobile = false } = {}) {
    const context = await browser.newContext(contextOptions);
    await context.routeWebSocket(UNREALIRCD, ws => fakeIrcServer(ws, { lines: [] }, {
        server: 'irc.unrealircd.org', network: 'UnrealIRCd', channels: CHANNELS, echo: '',
    }));
    const page = await context.newPage();
    await boot(page, host.baseUrl);
    await openIrc(page, { mobile });
    if (mobile) {
        await page.locator('[aria-controls="irc-channels-panel"]').tap();
    }
    await page.locator('.channel-connect', { hasText: 'UnrealIRCd' }).click();
    await page.locator('.message-line', { hasText: 'Connected as neo.' }).waitFor({ timeout: 30_000 });
    await say(page, '/join #help,#kazo-os');
    // The last channel joined is selected; on a phone the channels panel has closed.
    await page.locator('.channel-row.selected', { hasText: '#kazo-os' }).waitFor({ state: 'attached', timeout: 30_000 });
    await page.locator('.message-line', { hasText: 'try /join #help' }).waitFor({ timeout: 30_000 });
    return { context, page };
}

/** Maximizes the OS window of IRC (a double click on its title bar). */
async function enlarge(page) {
    const window = page.locator('[data-window-id]', { has: page.locator('.irc-window') });
    await window.locator('h2').first().dblclick();
    await page.waitForTimeout(800);
}

const scenes = [
    // 1. A channel on a WebSocket network: topic, messages with a mention and links, users.
    async browser => {
        const { context, page } = await connected(browser, DESKTOP);
        await enlarge(page);
        await say(page, 'thanks trinity, the window stays connected when I minimize it');
        await page.waitForTimeout(1_000);
        return { context, page };
    },
    // 2. "Manage networks": Libera.Chat and OFTC through the Kazo server, UnrealIRCd over WebSocket.
    async browser => {
        const { context, page } = await connected(browser, DESKTOP);
        await enlarge(page);
        await page.getByRole('button', { name: 'Manage networks' }).click();
        await page.locator('.network-editor').waitFor();
        await page.waitForTimeout(800);
        return { context, page };
    },
    // 3. On a phone: one column, the console font.
    async browser => {
        const { context, page } = await connected(browser, { ...devices['Pixel 7'] }, { mobile: true });
        await page.locator('.irc-font-toggle').tap();
        await page.waitForTimeout(1_000);
        return { context, page };
    },
];

host = await startHost();
const browser = await chromium.launch();
try {
    await mkdir(output, { recursive: true });
    for (const [index, scene] of scenes.entries()) {
        const { context, page } = await scene(browser);
        const file = path.join(output, `${index + 1}.png`);
        await page.screenshot({ path: file });
        console.log(`[screenshots] ${file}`);
        await context.close();
    }
} finally {
    await browser.close();
    host.stop();
}
