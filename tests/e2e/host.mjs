// Test harness of the end-to-end runs: starts the IRC host (src/Kazo.Irc.Os, built beforehand with
// `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true`, or the configuration named by IRC_CONFIGURATION) on a free port, or
// uses IRC_BASE_URL when it points to one already running, and boots a fresh Kazo OS profile.
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const hostProject = path.join(root, 'src', 'Kazo.Irc.Os');

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
 * Starts the host, or reuses IRC_BASE_URL.
 * @returns {Promise<{ baseUrl: string, stop: () => void }>}
 */
export async function startHost() {
    if (process.env.IRC_BASE_URL) {
        return { baseUrl: process.env.IRC_BASE_URL, stop: () => {} };
    }
    const port = await freePort();
    const baseUrl = `http://127.0.0.1:${port}/`;
    const configuration = process.env.IRC_CONFIGURATION ?? 'Release';
    const server = spawn('dotnet', ['run', '--no-build', '-c', configuration, '--urls', baseUrl], { cwd: hostProject, stdio: 'ignore' });
    await waitForHttp(baseUrl, 60_000);
    return { baseUrl, stop: () => server.kill() };
}

/**
 * Boots Kazo OS in a fresh browser context (first boot: the administrator "neo" is created).
 * @param {import('playwright').Page} page A page of a new context.
 * @param {string} baseUrl The host's address.
 */
export async function boot(page, baseUrl) {
    await page.goto(baseUrl);
    const inputs = page.locator('input');
    await inputs.first().waitFor({ timeout: BOOT_TIMEOUT_MS });
    await inputs.nth(0).fill('neo');
    await inputs.nth(1).fill('Neo');
    await inputs.nth(2).fill('matrix123');
    await inputs.nth(3).fill('matrix123');
    await page.getByRole('button', { name: 'Create local profile' }).click();
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

/**
 * Connects the IRC window to the demonstration network (built with -p:IrcDemoNetwork=true) and
 * waits for the registration.
 * @param {import('playwright').Page} page The page, with the IRC window open.
 */
export async function connectDemo(page) {
    await page.locator('.channel-connect', { hasText: 'Kazo Demo' }).click();
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
