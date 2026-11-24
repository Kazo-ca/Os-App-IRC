// A small IRCv3 WebSocket server inside the tests: no real IRC network is reached. Playwright's
// routeWebSocket answers the IRC client's WebSocket with it (the OS refuses a WebSocket to this
// machine, a simulated host, so a server on localhost cannot be used). One WebSocket message is one
// IRC line, without CR LF (text.ircv3.net). Used by websocket.e2e.mjs, chat.e2e.mjs (with the bot,
// see testBotOptions), tests/installed and tests/store.

/**
 * @typedef {object} FakeChannel
 * @property {string} [topic] The topic sent after the JOIN (332).
 * @property {string[]} [users] The other users of the channel, with their mode prefix (`@op`, `+voice`).
 * @property {string[]} [history] Lines `nick: text` the other users say right after the JOIN.
 */

/**
 * @typedef {object} FakeIrcOptions
 * @property {string} [server] The server name in the replies.
 * @property {string} [network] The network name of the welcome line (and of 005 NETWORK when given).
 * @property {Record<string, FakeChannel>} [channels] What a JOIN of a channel answers; a channel not
 *   listed gets the topic "Topic over WebSocket" and the user `@echo`.
 * @property {string} [echo] The nick that answers each PRIVMSG with `<nick> said: <text>`; empty for none.
 * @property {string} [bot] The nick of a bot that talks back like kazo-bot of the demonstration
 *   network (src/Kazo.Irc.Demo/DemoIrcServer.cs): a channel line naming it gets `<nick>: hello!…`,
 *   `!mention N` names the sender N seconds later, `!private N` sends a private message N seconds
 *   later, `!links` sends LINKS_MESSAGE, and a private message to it gets `You said: <text>`.
 */

/** The delay of `!mention` and `!private` without a number, and the longest one (seconds). */
const BOT_DEFAULT_DELAY = 3;
const BOT_MAX_DELAY = 60;

/** What the bot answers to `!links`: addresses, IRC formatting codes and text that looks like HTML. */
export const LINKS_MESSAGE =
    '\u0002Links\u0002: the docs (http://docs.hack/guide?topic=irc), in \u000304,01red\u0003 and \u001Ditalics\u001D. '
    + '<b>Not bold</b> <script>alert(1)</script> https://kazo.ca/.';

/**
 * The options of a network with the bot, as the demonstration network is: #kazo with its topic,
 * kazo-bot (operator), morpheus (voice) and trinity, and no echo.
 * @returns {FakeIrcOptions}
 */
export function testBotOptions() {
    return {
        server: 'irc.test.example',
        // No NETWORK in 005: the name the user gave in the editor ("Kazo Test") is the one shown.
        echo: '',
        bot: 'kazo-bot',
        channels: {
            '#kazo': { topic: 'Welcome to the Kazo OS IRC demonstration network', users: ['@kazo-bot', '+morpheus', 'trinity'] },
        },
    };
}

/**
 * Reads `!command` or `!command N`: the delay in seconds (clamped to 0..60), or null when the text
 * is not that command.
 */
function botCommand(text, command) {
    const trimmed = text.trim();
    if (trimmed.toLowerCase() === command) {
        return BOT_DEFAULT_DELAY;
    }
    const match = trimmed.match(new RegExp(`^${command}\\s+(\\d+)$`, 'i'));
    return match ? Math.min(Number(match[1]), BOT_MAX_DELAY) : null;
}

/**
 * Answers the IRC client like a small IRCv3 WebSocket server.
 * @param {import('playwright').WebSocketRoute} ws The routed WebSocket.
 * @param {{ lines: string[] }} record Receives the lines the client sent.
 * @param {FakeIrcOptions} [options] What the server says.
 */
export function fakeIrcServer(ws, record, options = {}) {
    const server = options.server ?? 'irc.e2e.example';
    const network = options.network ?? 'E2E';
    const channels = options.channels ?? {};
    const echo = options.echo ?? 'echo';
    const bot = options.bot ?? '';
    let nick = '*';
    let closed = false;
    const send = line => {
        if (!closed) {
            ws.send(line);
        }
    };
    const later = (seconds, line) => setTimeout(() => send(line), seconds * 1000);
    const source = who => `${who}!~${who}@e2e`;
    // The bot's answers (see FakeIrcOptions.bot); true when it answered.
    const botAnswer = (target, text) => {
        if (!bot) {
            return false;
        }
        if (!target.startsWith('#')) {
            if (target.toLowerCase() !== bot.toLowerCase()) {
                return false;
            }
            send(`:${source(bot)} PRIVMSG ${nick} :You said: ${text}`);
            return true;
        }
        const mention = botCommand(text, '!mention');
        const privately = botCommand(text, '!private');
        if (text.trim().toLowerCase() === '!links') {
            send(`:${source(bot)} PRIVMSG ${target} :${LINKS_MESSAGE}`);
        } else if (mention !== null) {
            later(mention, `:${source(bot)} PRIVMSG ${target} :${nick}: you asked me to name you, here you are.`);
        } else if (privately !== null) {
            later(privately, `:${source(bot)} PRIVMSG ${nick} :Hello ${nick}, this is a private message.`);
        } else if (text.toLowerCase().includes(bot.toLowerCase())) {
            send(`:${source(bot)} PRIVMSG ${target} :${nick}: hello! Try !mention 5 or !private 5.`);
        } else {
            return false;
        }
        return true;
    };
    ws.onClose?.(() => {
        closed = true;
    });
    ws.onMessage(message => {
        const line = typeof message === 'string' ? message : Buffer.from(message).toString('utf8');
        record.lines.push(line);
        const [command, ...rest] = line.split(' ');
        const trailing = line.includes(' :') ? line.slice(line.indexOf(' :') + 2) : rest[rest.length - 1];
        switch (command.toUpperCase()) {
            case 'CAP':
                if (rest[0] === 'LS') {
                    send(`:${server} CAP * LS :`);
                }
                break;
            case 'NICK':
                nick = rest[0];
                break;
            case 'USER':
                send(`:${server} 001 ${nick} :Welcome to the ${network} network ${nick}`);
                send(`:${server} 005 ${nick} ${options.network ? `NETWORK=${network} ` : ''}PREFIX=(ov)@+ CHANTYPES=# :are supported by this server`);
                send(`:${server} 376 ${nick} :End of /MOTD command.`);
                break;
            case 'PING':
                send(`:${server} PONG ${server} :${trailing}`);
                break;
            case 'JOIN':
                for (const channel of rest[0].split(',')) {
                    const known = channels[channel] ?? { topic: 'Topic over WebSocket', users: ['@echo'] };
                    send(`:${nick}!~${nick}@e2e JOIN ${channel}`);
                    if (known.topic) {
                        send(`:${server} 332 ${nick} ${channel} :${known.topic}`);
                    }
                    send(`:${server} 353 ${nick} = ${channel} :${[nick, ...(known.users ?? [])].join(' ')}`);
                    send(`:${server} 366 ${nick} ${channel} :End of /NAMES list.`);
                    for (const said of known.history ?? []) {
                        const colon = said.indexOf(': ');
                        const who = said.slice(0, colon);
                        send(`:${who}!~${who}@e2e PRIVMSG ${channel} :${said.slice(colon + 2)}`);
                    }
                }
                break;
            case 'PRIVMSG':
                if (botAnswer(rest[0], trailing)) {
                    break;
                }
                if (echo) {
                    send(`:${echo}!~${echo}@e2e PRIVMSG ${rest[0]} :${nick} said: ${trailing}`);
                }
                break;
            case 'QUIT':
                closed = true;
                ws.close();
                break;
            default:
                break;
        }
    });
}
