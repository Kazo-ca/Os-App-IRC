> [Version française](../fr/architecture.md)

# Architecture of the IRC app

## Goal

IRC is a native Kazo OS chat client for real IRC networks. The goal and the
decisions are in the [plan](plan.md). This document describes the code; each
work item of the `irc/base` series completes it.

## Solution

```text
Os-App-IRC/
├─ Kazo.Irc.slnx
├─ src/
│  └─ Kazo.Irc.Protocol/        # the IRC protocol, with no Kazo SDK dependency (work item 2)
└─ tests/
   └─ Kazo.Irc.Protocol.Tests/  # xUnit tests of the protocol
```

## Protocol (`Kazo.Irc.Protocol`)

A **pure** C# library: no reference to the Kazo OS SDK or to Blazor. It is
tested alone, and the app gives it a transport (WebSocket or the relay's TCP
stream, work item 3). It follows RFC 1459 / RFC 2812 and the IRCv3
specifications chosen by the plan (decision 18).

| Folder | Content |
| --- | --- |
| `Messages/` | `IrcMessage` (reads and writes a line: IRCv3 tags, source, command, parameters), `IrcTags` (value escaping), `IrcSource` (`nick!user@host` or a server), `IrcLimits` (512 bytes, tags 4096 / 8191), `IrcNumerics`, `IrcProtocolException`. |
| `Text/` | `IrcEncoding` (UTF-8 when writing; when reading UTF-8 with a per-line Latin-1 fallback), `IrcLineBuffer` (cuts a byte stream into lines), `IrcTextSplitter` (cuts a long text without cutting a character), `IrcCtcp` (`ACTION`, `VERSION`...), `IrcCaseMapping` (`rfc1459`, `strict-rfc1459`, `ascii`). |
| `Transport/` | `IIrcTransport` (connect, incoming and outgoing lines, close) and `FakeIrcTransport` (the tests' fake server). |
| `Model/` | `IrcServerSupport` (ISUPPORT: `PREFIX`, `CHANTYPES`, `CHANMODES`, `CASEMAPPING`, `NETWORK`), `IrcChannel`, `IrcChannelUser`, `IrcUser`. |
| `Client/` | `IrcClient` (partial files by topic), `IrcClientOptions`, `IrcReconnectPolicy`, `IrcClientState` and the event data. |
| `Input/` | `IrcInputParser` and `IrcInputCommand`: what the user types. |

### Messages

`IrcMessage.Parse` is lenient (extra spaces, long line, lower-case command,
badly escaped tag value); `IrcMessage.Format` is strict and refuses what a
server would refuse: CR, LF or NUL in a parameter, an empty middle parameter or
one with a space, a line longer than 512 bytes (CR LF included), client tags
longer than 4096 bytes. Lengths are counted in **UTF-8 bytes**, not characters.
The last parameter gets a `:` only when it needs one.

### Transport

```csharp
public interface IIrcTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken); // null = closed
    Task WriteLineAsync(string line, CancellationToken cancellationToken); // no CR LF
    Task CloseAsync(CancellationToken cancellationToken);
}
```

The client asks its factory for a **new** transport at every connection
attempt. Work item 3 writes two transports: WebSocket (`KazoClientWebSocket`,
sub-protocol `text.ircv3.net`, one frame = one line) and the TCP relay
(`KazoTcpClient`, lines ended by CR LF and cut by `IrcLineBuffer`).
`FakeIrcTransport` plays the server by hand: `ServerSendAsync` returns once the
client has handled the line, and `WaitForSentAsync` waits for a line the client
sends.

### Client

`IrcClient` handles one network. Its read loop handles lines one at a time and
raises the events from that loop (in Blazor WebAssembly, that is the UI
thread).

| Topic | Behavior |
| --- | --- |
| Registration | `CAP LS 302`, `PASS` (when a server password is given), `NICK`, `USER`. |
| CAP | Reads the list over several lines (`*`), asks only for what it wants and the server offers (`message-tags`, `server-time`, `away-notify`, `multi-prefix`, `account-notify`, `sasl`), handles `ACK`, `NAK`, `NEW`, `DEL`, then `CAP END`. An old server without CAP (`421`) is accepted. |
| SASL | `PLAIN` only, and only when the server offers it. Payload `account\0account\0password` in base64, in pieces of 400 (`+` after a full piece). `903` succeeded, `902`/`904`/`905`/`906` failed: registration goes on and `SaslCompleted` says so. The password never shows in `ToString`. |
| Nick taken | During registration, `433`, `432` and `436` try the alternate nicknames, then `nick_`, `nick__`, `nick1` to `nick9`; when all are taken, `NICK_EXHAUSTED`, `QUIT` and no reconnection. After registration, it is an error shown to the user. |
| `001` | State `Connected`; joins the auto-join channels (the first time) and every channel the client wants to be in, in as few `JOIN` lines as the limit allows (channels with a key first). |
| `PING`/`PONG` | Answers `PING`. After 90 s without receiving anything, sends a `PING`; 60 s later without an answer, the connection is held lost (`PING_TIMEOUT`) and the reconnection starts. |
| Reconnection | Growing delay: 2 s, 4 s, 8 s... up to 5 minutes (`IrcReconnectPolicy`), back to the start after a successful registration; an optional maximum number of attempts. After a loss, the channels stay known (`WantsJoin`) and are joined again. `/part` and a kick remove that wish. |
| `QUIT` | `QuitAsync` sends `QUIT`, waits for the server to close (3 s at most) then closes itself; no reconnection. `DisposeAsync` stops without `QUIT`. |
| Clock | A `TimeProvider` (delays, PING timer, time of messages without `server-time`): tests pass a fake clock. |

### Model

| Message | Effect |
| --- | --- |
| `005` | `IrcServerSupport`; a `CASEMAPPING` change recomputes every key. |
| `JOIN`, `PART` (several channels), `KICK`, `QUIT`, `NICK` | Channels and users up to date, one `IrcActivity` per conversation affected (channels and private conversation). `extended-join` gives the account. |
| `353` / `366` | The `NAMES` list is kept until its end then replaces the users; with `multi-prefix` a name carries several symbols (`~@nick`); `nick!user@host` (userhost-in-names) is read. |
| `MODE` / `324` | User modes (`PREFIX`) and channel modes by the A, B, C, D types of `CHANMODES`; the `+k` key is kept to join again. |
| `TOPIC`, `331`, `332`, `333` | Topic, author and date. |
| `AWAY` (away-notify), `301`, `305`, `306`, `ACCOUNT` (account-notify) | Away state and account of users, away state of the client. |
| `PRIVMSG`, `NOTICE` | `IrcChatMessage` with the `server-time` time, the conversation (channel, `@#channel` included, or the other person), message / action / notice. A private message opens a private conversation; a notice does not. Server notices and notices before `001` go to the server tab. |
| CTCP | `ACTION` is an action; `VERSION`, `PING`, `TIME`, `CLIENTINFO` get an answer by `NOTICE`; the rest (DCC included, refused by the plan) is only shown. |
| WHOIS | `311`, `312`, `313`, `317`, `319`, `330`, `301`, `401` gathered until `318` into an `IrcWhoisInfo`. |
| Other numerics | `4xx`/`5xx` → `Error`; others → `ServerText`. |

### Input

`IrcInputParser.Parse(line, conversation, chantypes)` reads plain text and the
commands `/join` (`/j`), `/part`, `/msg`, `/me`, `/nick`, `/topic`, `/whois`,
`/away`, `/quit`, `/query`, `/raw` (`/quote`). `//text` sends `/text`. A line
that cannot be done gives `Invalid` with the reason (`UnknownCommand`,
`MissingArgument`, `NoConversation`, `NotAChannel`): the UI translates it.
`IrcClient.ExecuteAsync` does the command. A long message is cut (at a space
when possible) so the line the server relays, with `:nick!user@host ` in front,
fits in 512 bytes; before it knows its host, the client assumes the longest one
(63 bytes).

### Tests

`tests/Kazo.Irc.Protocol.Tests` (xUnit, `Microsoft.Extensions.TimeProvider.Testing`
for the fake clock): messages and tags, encoding, cutting lines and texts, CTCP,
`CASEMAPPING`, ISUPPORT, input, and the client on `FakeIrcTransport`: CAP, SASL,
nick taken, channels, modes, topics, away, messages, CTCP, WHOIS, commands,
`QUIT`, reconnection, `PING`. Protocol coverage: 97 % of lines, 91 % of
branches.

```bash
dotnet test Kazo.Irc.slnx
```

## Work item 2 tasks

- [x] Project `src/Kazo.Irc.Protocol` with no Kazo SDK dependency.
- [x] Reading and writing messages: tags, source, parameters, limits, UTF-8 and Latin-1 fallback.
- [x] `IIrcTransport` and `FakeIrcTransport`.
- [x] Client: registration, CAP 302, SASL PLAIN, `PING`/`PONG`, nick taken, reconnection with a growing delay and back into the channels.
- [x] Model of channels and users: prefixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Input commands `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] xUnit tests.
- [ ] Wiring to the UI: work items 3 and 4 (work item 1 was not merged when work item 2 ended).
