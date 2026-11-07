// A small IRCv3 WebSocket server inside the tests: no real IRC network is reached. Playwright's
// routeWebSocket answers the IRC client's WebSocket with it (the OS refuses a WebSocket to this
// machine, a simulated host, so a server on localhost cannot be used). One WebSocket message is one
// IRC line, without CR LF (text.ircv3.net). Used by websocket.e2e.mjs, tests/installed and
// tests/store.

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
 */

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
    let nick = '*';
    const send = line => ws.send(line);
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
                if (echo) {
                    send(`:${echo}!~${echo}@e2e PRIVMSG ${rest[0]} :${nick} said: ${trailing}`);
                }
                break;
            case 'QUIT':
                ws.close();
                break;
            default:
                break;
        }
    });
}
