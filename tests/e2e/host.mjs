// Test harness of the end-to-end runs. Two ways to run Kazo OS:
// - On the published trimmed OS (Kazo OS ADR 0281 §5): when the CI sets KAZOOS_E2E_WWWROOT,
//   KAZOOS_E2E_PACKAGES and KAZOOS_E2E_REPOSITORY, the OS is the release users load and IRC is
//   installed in each fresh profile with `sudo kazopkg install`, through the shared harness of the
//   Kazo OS submodule (external/KazoOs/wasm2/KazoOs/build/app-e2e/kazoos-app-e2e.mjs). The package
//   is the Store's: it has no demonstration network, so the chat runs talk to the test network
//   (connectTestNetwork), a fake IRC server inside the test.
// - Otherwise: the IRC host (src/Kazo.Irc.Os, built beforehand with `dotnet build Kazo.Irc.slnx -c
//   Release -p:IrcDemoNetwork=true`, or the configuration named by IRC_CONFIGURATION) on a free
//   port, or IRC_BASE_URL when it points to one already running. This is the fast local loop.
// Either way, boot starts a fresh Kazo OS profile with IRC in it.
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import { fakeIrcServer, testBotOptions } from './fake-irc-server.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const hostProject = path.join(root, 'src', 'Kazo.Irc.Os');
const sharedHarness = path.join(root, 'external', 'KazoOs', 'wasm2', 'KazoOs', 'build', 'app-e2e', 'kazoos-app-e2e.mjs');

/** The network the chat runs add and join: its WebSocket is answered by the fake server and its bot. */
export const TEST_NETWORK = Object.freeze({ name: 'Kazo Test', url: 'wss://irc.test.example/' });

// The first WebAssembly boot downloads and compiles the whole OS: give it time.
export const BOOT_TIMEOUT_MS = 120_000;

const freePort = () => new Promise((resolve, reject) => {
    const server = createServer();
    server.listen(0, '127.0.0.1', () => {
        const { port } = server.address();
        server.close(() => resolve(port));
    });
    server.on('error', reject);
});

const waitForHttp = async (url, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        try {
            const response = await fetch(url);
            if (response.ok) {
                return;
            }
        } catch {
            // Not listening yet.
        }
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`${url} did not answer within ${timeoutMs} ms.`);
};

/**
 * The shared harness and its setup when the CI asks for the trimmed OS, null otherwise. The module
 * is imported only then: a Kazo OS submodule older than the harness (before 3.5.1) does not have it.
 * @returns {Promise<{ harness: object, setup: { wwwroot: string, packages: string, repository: string } } | null>}
 */
async function trimmedSetup() {
    const { KAZOOS_E2E_WWWROOT, KAZOOS_E2E_PACKAGES, KAZOOS_E2E_REPOSITORY } = process.env;
    if (!KAZOOS_E2E_WWWROOT || !KAZOOS_E2E_PACKAGES || !KAZOOS_E2E_REPOSITORY) {
        return null;
    }
    if (!existsSync(sharedHarness)) {
        throw new Error(`KAZOOS_E2E_* are set but the Kazo OS submodule has no shared harness (${sharedHarness}).`);
    }
    const harness = await import(pathToFileURL(sharedHarness).href);
    return { harness, setup: harness.fromEnvironment() };
}

// The trimmed setup of each running host, by address: boot finds it from the address alone.
const trimmedHosts = new Map();

/**
 * Starts the host: the published trimmed OS when the CI asks for it, else the IRC host, or
 * IRC_BASE_URL.
 * @returns {Promise<{ baseUrl: string, trimmed: boolean, stop: () => Promise<void> | void, assertNoFailures: () => void }>}
 *   assertNoFailures throws when a page showed an error that means the trimmed OS lacks something
 *   IRC needs (a missing member, type or file): call it once the tests are done.
 */
export async function startHost() {
    const trimmed = await trimmedSetup();
    if (trimmed) {
        const os = await trimmed.harness.startTrimmedOs(trimmed.setup);
        const failures = []; // one list per page, filled while the page runs
        trimmedHosts.set(os.baseUrl, { ...trimmed, failures });
        return {
            baseUrl: os.baseUrl,
            trimmed: true,
            stop: async () => {
                trimmedHosts.delete(os.baseUrl);
                await os.stop();
            },
            assertNoFailures: () => {
                const seen = failures.flat();
                if (seen.length > 0) {
                    throw new Error(`IRC failed on the trimmed Kazo OS:\n${seen.join('\n')}`);
                }
            },
        };
    }
    return startDotnetHost();
}

/**
 * The host for a test on a phone. The shared harness installs the app from the desktop Terminal;
 * until the Kazo OS version of the submodule also installs it on a phone (its SHELLS export names
 * "phone"), a phone test runs on the IRC host, already built by the CI.
 * @param {{ baseUrl: string }} host The host of the other tests.
 * @returns {Promise<{ baseUrl: string, stop: () => Promise<void> | void }>} Stop it after the test.
 */
export async function phoneHost(host) {
    const trimmed = trimmedHosts.get(host.baseUrl);
    if (!trimmed || trimmed.harness.SHELLS?.includes('phone')) {
        return { baseUrl: host.baseUrl, stop: () => {} };
    }
    return startDotnetHost();
}

/**
 * Starts the IRC host (dotnet run), or reuses IRC_BASE_URL.
 * @returns {Promise<{ baseUrl: string, trimmed: boolean, stop: () => void, assertNoFailures: () => void }>}
 */
async function startDotnetHost() {
    const none = () => {};
    if (process.env.IRC_BASE_URL) {
        return { baseUrl: process.env.IRC_BASE_URL, trimmed: false, stop: none, assertNoFailures: none };
    }
    const port = await freePort();
    const baseUrl = `http://127.0.0.1:${port}/`;
    const configuration = process.env.IRC_CONFIGURATION ?? 'Release';
    const server = spawn('dotnet', ['run', '--no-build', '-c', configuration, '--urls', baseUrl], { cwd: hostProject, stdio: 'ignore' });
    await waitForHttp(baseUrl, 60_000);
    return { baseUrl, trimmed: false, stop: () => server.kill(), assertNoFailures: none };
}

/**
 * Boots Kazo OS in a fresh browser context (first boot: the administrator "neo" is created), the
 * test network's WebSocket answered by the fake server (routeTestNetwork). On
 * the trimmed OS, IRC is then installed with kazopkg, as the Store installs it; the page's errors
 * that mean a missing member or type go to the host's failures.
 * @param {import('playwright').Page} page A page of a new context.
 * @param {string} baseUrl The host's address.
 */
export async function boot(page, baseUrl) {
    await routeTestNetwork(page.context());
    const trimmed = trimmedHosts.get(baseUrl);
    if (trimmed) {
        const { harness, setup, failures } = trimmed;
        failures.push(harness.watchFailures(page));
        await harness.routePackages(page.context(), setup);
        await harness.bootFirstAccount(page, baseUrl);
        await harness.installApp(page, setup);
        return;
    }
    await page.goto(baseUrl);
    const inputs = page.locator('input');
    await inputs.first().waitFor({ timeout: BOOT_TIMEOUT_MS });
    await inputs.nth(0).fill('neo');
    await inputs.nth(1).fill('Neo');
    await inputs.nth(2).fill('matrix123');
    await inputs.nth(3).fill('matrix123');
    await page.getByRole('button', { name: 'Create local profile' }).click();
    // The welcome wizard (Kazo OS ADR 0276, from 3.4.0) shows once per account: skip it when
    // this Kazo OS version has it.
    const skip = page.locator('.welcome-skip');
    // The desktop shows "Applications", the phone shell "All applications".
    const desktop = page.getByText('Applications', { exact: true }).or(page.getByRole('button', { name: 'All applications' }));
    await skip.or(desktop).first().waitFor({ timeout: 60_000 });
    if (await skip.isVisible()) {
        await skip.click();
    }
}

/**
 * Opens the IRC app: from the Applications menu on the desktop, from "All applications" on a phone.
 * @param {import('playwright').Page} page The page, booted.
 * @param {{ mobile?: boolean }} [options] Whether the page runs the mobile shell.
 */
export async function openIrc(page, { mobile = false } = {}) {
    const launcher = mobile
        ? page.getByRole('button', { name: 'All applications' })
        : page.getByText('Applications', { exact: true });
    await launcher.click({ timeout: 60_000 });
    await page.getByText('IRC', { exact: true }).first().click();
    await approveIfAsked(page, page.locator('.irc-window'));
    await page.locator('.irc-window').waitFor({ timeout: 30_000 });
}

/**
 * Approves, as the administrator "neo", the dangerous capability an app asks for at its first
 * launch in a fresh profile (IRC's own network.real.access since work item 3, or the Browser's),
 * then waits for the app.
 * @param {import('playwright').Page} page The page.
 * @param {import('playwright').Locator} app Something the app shows once it runs.
 */
export async function approveIfAsked(page, app) {
    const approval = page.getByText('Administrator authorization required', { exact: false });
    await approval.or(app).first().waitFor({ timeout: 30_000 });
    if (await approval.isVisible()) {
        await page.locator('input[type="password"]').last().fill('matrix123');
        await page.getByRole('button', { name: 'Allow', exact: true }).click();
    }
}

/**
 * The OS window that holds the IRC app (its title bar and buttons).
 * @param {import('playwright').Page} page The page.
 */
export const ircOsWindow = page => page.locator('[data-window-id]', { has: page.locator('.irc-window') });

/**
 * Opens an app from the Applications menu of the desktop.
 * @param {import('playwright').Page} page The page.
 * @param {string} name The app's name in the menu.
 */
export async function openApp(page, name) {
    await page.getByText('Applications', { exact: true }).click();
    await page.getByText(name, { exact: true }).first().click();
}

/**
 * Types one command in a new Terminal window (which takes the focus from the other windows).
 * @param {import('playwright').Page} page The page.
 * @param {string} command The command line.
 */
export async function runInTerminal(page, command) {
    await openApp(page, 'Terminal');
    const input = page.locator('.xterm-helper-textarea').first();
    await input.waitFor({ state: 'attached', timeout: 30_000 });
    // The shell prints its banner and prompt first.
    await page.locator('.xterm-rows').getByText('neo@', { exact: false }).first().waitFor({ timeout: 30_000 });
    await input.focus();
    await page.keyboard.type(`${command}\n`, { delay: 5 });
}

// The contexts whose test network WebSocket is already answered by the fake server.
const routedContexts = new WeakSet();

/**
 * Answers the test network's WebSocket (TEST_NETWORK) in the context with the fake IRC server and
 * its bot (fake-irc-server.mjs, testBotOptions), so the chat runs need neither the demonstration
 * network (not in the Store's package) nor a real network. Playwright routes the WebSockets of a
 * page only from its next navigation: boot calls it before opening the OS.
 * @param {import('playwright').BrowserContext} context The context.
 */
export async function routeTestNetwork(context) {
    if (!routedContexts.has(context)) {
        routedContexts.add(context);
        await context.routeWebSocket(TEST_NETWORK.url, ws => fakeIrcServer(ws, { lines: [] }, testBotOptions()));
    }
}

/**
 * Adds the test network (TEST_NETWORK) with the network editor; boot already answers its
 * WebSocket (routeTestNetwork).
 * @param {import('playwright').Page} page The page, with the IRC window open and its channel list
 *   on screen (on a phone, the Channels panel open).
 */
export async function addTestNetwork(page) {
    await page.getByRole('button', { name: 'Manage networks' }).click();
    const editor = page.locator('.network-editor');
    await editor.getByRole('button', { name: /Add a network/ }).click();
    await editor.getByLabel('Name', { exact: true }).fill(TEST_NETWORK.name);
    await editor.getByLabel('WebSocket address').fill(TEST_NETWORK.url);
    await editor.getByRole('button', { name: 'Save' }).click();
    await editor.locator('.network-editor-row', { hasText: TEST_NETWORK.name }).waitFor({ timeout: 10_000 });
    await editor.getByRole('button', { name: 'Close' }).click();
    await editor.waitFor({ state: 'detached' });
}

/**
 * Adds the test network, connects the IRC window to it and waits for the registration.
 * @param {import('playwright').Page} page The page, with the IRC window open.
 */
export async function connectTestNetwork(page) {
    await addTestNetwork(page);
    await page.locator('.channel-connect', { hasText: TEST_NETWORK.name }).click();
    await page.locator('.message-line', { hasText: 'Connected as' }).waitFor({ timeout: 30_000 });
}

/**
 * Types a line in the IRC input and sends it with Enter.
 * @param {import('playwright').Page} page The page.
 * @param {string} text The line.
 */
export async function say(page, text) {
    const input = page.getByRole('textbox', { name: 'Message' });
    await input.fill(text);
    await input.press('Enter');
}
