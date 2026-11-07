// IRC as the Kazo OS Store installs it: `npm run test:installed -- <Kazo OS wwwroot> <packages>`.
//
// <Kazo OS wwwroot>: a published Kazo OS 3.2.0 (for example the output of
//   external/KazoOs/wasm2/KazoOs/build/publish-trimmed.sh, artifacts/publish-trimmed/publish/wwwroot,
//   or `dotnet publish OS/Kazo.Os.Ecosystem -c Release`), the trimmed OS users run.
// <packages>: the folder `dotnet build src/Kazo.Irc -c Release -t:KazoOsPack` writes
//   (default src/Kazo.Irc/bin/kazoos-packages), the content of the kazoos-packages branch. Built
//   without -p:IrcDemoNetwork=true, as the Store builds it: the package has no demonstration network.
//
// The run serves the OS, answers the GitHub addresses of this repository's packages from <packages>,
// installs IRC with `sudo kazopkg install` (its API check against the trimmed runtime included),
// opens it from the Applications menu, approves network.real.access as the administrator, checks the
// proposed networks (no "Kazo Demo"; Libera.Chat and OFTC need the Kazo server), connects to
// UnrealIRCd over WebSocket (answered by the fake server of tests/e2e, no real network is reached),
// joins a channel and talks, with the app's scoped CSS linked and no console error.
// Not part of CI: it needs a Kazo OS publish (about ten minutes to build).
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';
import { fakeIrcServer } from '../e2e/fake-irc-server.mjs';

const [wwwroot, packages] = process.argv.slice(2).map(argument => argument && path.resolve(argument));
if (!wwwroot || !packages) {
    console.error('usage: npm run test:installed -- <Kazo OS wwwroot> <packages folder>');
    process.exit(2);
}

const REPOSITORY = 'Kazo-ca/Os-App-IRC';
const UNREALIRCD = 'wss://irc.unrealircd.org/';
const LOGIN = 'neo';
const PASSWORD = 'matrix123';
const STEP_MS = 120_000;
const TYPES = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream',
};

const log = message => console.log(`[installed] ${message}`);

// A plain static server, as a release host is: no COOP/COEP, no cache.
const server = createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://host').pathname);
    let file = path.join(wwwroot, relative);
    if (!file.startsWith(wwwroot)) {
        response.writeHead(403).end();
        return;
    }
    try {
        if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
        const body = await readFile(file);
        response.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(body);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}/`;

const waitForText = async (locator, predicate, what) => {
    const deadline = Date.now() + STEP_MS;
    while (Date.now() < deadline) {
        const text = (await locator.innerText()).replace(/ /g, ' ');
        if (predicate(text)) return text;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out waiting for ${what}`);
};

const browser = await chromium.launch();
const errors = [];
let page;
try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
        const relative = decodeURIComponent(new URL(route.request().url()).pathname).split('/kazoos-packages/')[1];
        const file = path.join(packages, relative);
        const headers = { 'Access-Control-Allow-Origin': '*' };
        try {
            if (!file.startsWith(packages)) throw new Error('outside the packages folder');
            await route.fulfill({ status: 200, body: await readFile(file), headers: { ...headers, 'Content-Type': 'application/octet-stream' } });
            log(`served ${relative}`);
        } catch {
            await route.fulfill({ status: 404, headers });
        }
    });
    // The proposed WebSocket network, answered by the fake server: nothing leaves this machine.
    const record = { lines: [] };
    await context.routeWebSocket(UNREALIRCD, ws => fakeIrcServer(ws, record, { server: 'irc.unrealircd.org', network: 'UnrealIRCd' }));
    page = await context.newPage();
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('pageerror', error => errors.push(String(error)));
    const missing = [];
    page.on('response', response => { if (response.status() >= 400) { missing.push(`${response.status()} ${response.url()}`); log(`request failed: ${response.status()} ${response.url()}`); } });

    // First boot: the administrator is created.
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Create administrator' }).waitFor({ timeout: 180_000 });
    await page.getByLabel('Login name').fill(LOGIN);
    await page.getByLabel('Display name').fill('Neo');
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create local profile' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });
    const openApp = async name => {
        await page.getByRole('button', { name: 'App launcher' }).click({ timeout: STEP_MS });
        await page.getByRole('combobox', { name: 'Search applications' }).fill(name);
        await page.getByRole('button', { name: `Launch ${name}`, exact: true }).click();
    };

    // Install, as an administrator.
    await openApp('Terminal');
    const rows = page.locator('[data-terminal-panel] .xterm-rows').first();
    await waitForText(rows, text => text.includes('$'), 'the shell prompt');
    await page.locator('[data-terminal-panel] .xterm-screen').first().click();
    await page.keyboard.type(`sudo kazopkg install ${REPOSITORY}`);
    await page.keyboard.press('Enter');
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    await prompt.waitFor({ state: 'visible', timeout: STEP_MS });
    const login = prompt.locator('input[type=text]');
    if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
    await prompt.locator('input[type=password]').fill(PASSWORD);
    await prompt.locator('input[type=password]').press('Enter');
    const output = await waitForText(rows, text => text.includes('available now') || /does not have|refused|error/i.test(text), 'the end of the installation');
    if (!output.includes('available now')) throw new Error(`kazopkg did not install IRC:\n${output}`);
    log('kazopkg installed IRC.');

    // Launch: the first launch asks the administrator for network.real.access and its ports.
    await openApp('IRC');
    const approval = page.getByText('Administrator authorization required', { exact: false });
    const ircWindow = page.locator('.irc-window');
    await approval.or(ircWindow).first().waitFor({ timeout: STEP_MS });
    if (await approval.isVisible()) {
        const asked = (await page.locator('body').innerText()).replace(/ /g, ' ');
        for (const port of ['6697', '6667']) {
            if (!asked.includes(port)) throw new Error(`the approval does not list the port ${port}`);
        }
        await page.locator('input[type="password"]').last().fill(PASSWORD);
        await page.getByRole('button', { name: 'Allow', exact: true }).click();
        log('the administrator approved network.real.access (ports 6697 and 6667 listed).');
    } else {
        log('no approval was asked at the first launch.');
    }
    await ircWindow.waitFor({ timeout: STEP_MS });

    const linked = await page.evaluate(() => [...document.querySelectorAll('link[data-kazoos-installed-app]')].map(link => link.getAttribute('href')));
    if (!linked.some(href => href.endsWith('_content/Kazo.Irc/Kazo.Irc.bundle.scp.css'))) {
        throw new Error(`the scoped CSS of IRC is not linked: ${JSON.stringify(linked)}`);
    }
    const overflow = await page.evaluate(() => getComputedStyle(document.querySelector('.irc-window')).overflow);
    if (overflow !== 'hidden') throw new Error(`the IRC window is not styled (overflow: ${overflow})`);
    log('IRC opened, styled.');

    // Kazo OS 3.2.0 publishes an installed app's wwwroot/ when its assembly loads: the launcher asks
    // for the icon before that and gets a 404 (Kazo-ca/Os#420). Once IRC runs, the icon is served.
    const icon = await page.evaluate(async () => (await fetch('_content/Kazo.Irc/icons/app.svg', { cache: 'no-store' })).status);
    if (icon !== 200) throw new Error(`the icon is not served once IRC runs (status ${icon})`);
    const known = /_content\/Kazo\.Irc\/icons\/app\.svg$/;
    const beforeLaunch = missing.filter(entry => known.test(entry));
    if (beforeLaunch.length > 0) log(`known: the launcher asked for the icon before IRC ran (Os#420): ${beforeLaunch.length} 404`);
    missing.splice(0, missing.length, ...missing.filter(entry => !known.test(entry)));
    // The browser prints each of those 404 as a console error without its address: drop that many.
    let known404 = beforeLaunch.length;
    errors.splice(0, errors.length, ...errors.filter(error => !(/status of 404/.test(error) && known404-- > 0)));

    // The Store build has no demonstration network; the relay networks are marked.
    await page.locator('.channel-connect', { hasText: 'UnrealIRCd' }).waitFor({ timeout: STEP_MS });
    if (await page.locator('.channel-connect', { hasText: 'Kazo Demo' }).count() > 0) throw new Error('the Store package shows the demonstration network');
    await page.locator('.channel-connect', { hasText: 'Libera.Chat' }).locator('.channel-connect-note', { hasText: 'Kazo server required' }).waitFor({ timeout: STEP_MS });

    // UnrealIRCd over WebSocket, with no Kazo server.
    await page.locator('.channel-connect', { hasText: 'UnrealIRCd' }).click();
    await page.locator('.message-line', { hasText: 'Connected as neo.' }).waitFor({ timeout: STEP_MS });
    const input = page.getByRole('textbox', { name: 'Message' });
    await input.fill('/join #kazo');
    await input.press('Enter');
    await page.locator('.channel-row', { hasText: '#kazo' }).click({ timeout: STEP_MS });
    await page.locator('.irc-topic', { hasText: 'Topic over WebSocket' }).waitFor({ timeout: STEP_MS });
    await input.fill('hello from the installed app');
    await input.press('Enter');
    await page.locator('.message-line', { hasText: 'neo said: hello from the installed app' }).waitFor({ timeout: STEP_MS });
    if (!record.lines.includes('PRIVMSG #kazo :hello from the installed app')) throw new Error(`the line was not sent: ${record.lines.join(' | ')}`);
    log('IRC connected to UnrealIRCd over WebSocket, joined #kazo and talked.');

    // Libera.Chat: no Kazo server here, said clearly.
    await page.locator('.channel-connect', { hasText: 'Libera.Chat' }).click();
    await page.locator('.message-line', { hasText: 'Libera.Chat needs the Kazo server' }).waitFor({ timeout: STEP_MS });
    log('Libera.Chat says it needs the Kazo server.');

    if (errors.length > 0) throw new Error(`console errors: ${errors.join(' | ')} (failed requests: ${missing.join(' | ') || 'none'})`);
    log('PASS');
} catch (error) {
    console.error(`[installed] FAIL: ${error.message}`);
    await page?.screenshot({ path: 'installed-failure.png' }).catch(() => {});
    process.exitCode = 1;
} finally {
    await browser.close();
    server.close();
}
