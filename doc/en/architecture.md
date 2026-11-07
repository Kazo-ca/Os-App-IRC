> [Version française](../fr/architecture.md)

# Architecture of the IRC app

## Purpose

IRC is a Kazo OS window app: a native chat client, in C# and Blazor, for real
IRC networks. The purpose and the decisions are in the [plan](plan.md). This
document describes the code; each work item of the `irc/base` series adds to it
(the protocol in work item 2, the connection in work item 3, and so on).

## Solution

```text
Os-App-IRC/
├─ external/KazoOs/          # submodule: Kazo OS on the v3.0.0 tag (the repository's version)
├─ Directory.Build.props     # submodule root, profile, import of Versions.props
├─ Directory.Build.targets   # source mode in Debug, KOSAPP0070 without the submodule
├─ global.json
├─ Kazo.Irc.slnx
├─ src/
│  ├─ Kazo.Irc/              # the app
│  ├─ Kazo.Irc.Os/           # host: Kazo OS, its base apps and IRC
│  ├─ Kazo.Irc.Demo/         # the demonstration network, an IRC server in memory (work item 4, never in the Store)
│  └─ Kazo.Irc.Protocol/     # the IRC protocol, no dependency on the Kazo SDK (work item 2)
├─ tests/
│  ├─ Kazo.Irc.Tests/        # C# unit tests of the app (xUnit)
│  ├─ Kazo.Irc.Protocol.Tests/ # xUnit tests of the protocol
│  └─ e2e/                   # runs in the host (node --test + Playwright)
├─ .github/workflows/        # ci.yml, publish-*.yml, tag-release.yml
└─ package.json              # end-to-end test tools
```

The repository follows the output of the Kazo OS 3.0.0 **`kazoos-solution`**
template (ADR 0176 and 0207), like [Chess](https://github.com/Kazo-ca/Os-App-Chess)
and [Webamp](https://github.com/Kazo-ca/Os-App-Webamp): `external/KazoOs` is
Kazo OS as a submodule, exactly on a release tag, and its `build/Versions.props`
gives **the** version of the repository. The submodule is never modified: CI
refuses a modified submodule or one off its tag
(`.github/scripts/check-kazoos-submodule.sh`). The workflows come from Webamp
(CI with the Playwright tests, "Tag a release", package publishing **without a
signature** — signing happens only in Os-AppMarketPlace, Kazo OS ADR 0219 — and
the submission to the Kazo OS Store catalog).

| Profile | Kazo OS comes from | Use |
| --- | --- | --- |
| `Debug` (`KazoOsUseSource=true`) | the submodule's projects (step into the SDK and the OS) | development, debugging |
| `Release` (`KazoOsUseSource=false`) | the `Kazo.Os.*` packages of nuget.org, at the submodule's version | CI, E2E tests, Kazo OS Store |

Clone: `git clone --recurse-submodules`, or `git submodule update --init`
afterwards (without the submodule, the build stops on `KOSAPP0070`).

**Host.** `Kazo.Irc.Os` is a full Kazo OS with the base apps and IRC as a
`ProjectReference`. `dotnet run --project src/Kazo.Irc.Os` starts it on
<http://localhost:5310>.

## The `Kazo.Irc` app

| Folder or file | Role |
| --- | --- |
| `IrcApp.cs` | Manifest (`[AppManifest]`): id `ca.kazo.irc`, name "IRC", version `1.0.0`, publisher `pub.kazo`, category `communication` (decision 20), **desktop and mobile** (`MobileEntryPoint` = the same window, decision 7), `SingleInstancePerUser` (decision 8). 960×620 window, resizable down to 340×400. Capabilities: `filesystem.private.read` and `.write` (the settings file, work item 1); `notifications.post`, `filesystem.user-home.read` and `.write` (notifications and logs, work item 4). Each work item adds those its code uses: `network.real.access` and the real ports in work item 3 (after Kazo OS ADR 0235 §3), `apps.launch` in work item 5. |
| `IrcWindow.razor`, `.razor.cs`, `.razor.css` | The single window: toolbar (title and topic, "Logs" and "Console font" buttons), three columns, compact-mode panels, status line. It holds an `IrcChat`, follows the window's focus, keeps the title's counter and sends `QUIT` when it closes (`IWindowCloseGuard`, decision 8). |
| `Components/ChannelList` | Left column: each network (state dot), its server tab, channels and private conversations, with unread and mention badges and a × to close; the networks that can be opened. |
| `Components/MessagePane` (`.razor`, `.razor.cs`, `.razor.css`, `.razor.js`) | Middle column: the lines (`role="log"`, newest at the bottom) and the input line with history and nickname completion. |
| `Components/UserList` | Right column: the channel's users with their prefix; a click opens a private conversation. |
| `Chat/` | The chat without Blazor (see "Chat" below): `IrcChat`, `IrcSession`, `Conversation`, `ChatLine`, `IrcNetwork` and `IIrcTransportFactory`, `IrcMentions`, `IrcNick`, `InputHistory`, `NickCompleter`, `IIrcNotifier` / `KazoIrcNotifier`, `DemoNetwork` (under `IRC_DEMO`). |
| `Logs/` | `IrcLog` (paths, safe names, line format) and `IrcLogWriter` (writes in order). |
| `Layout/IrcLayout.cs`, `IrcPanel.cs` | Which side panel is open in compact mode (none, channels or users; one at a time). |
| `Settings/IrcSettings.cs` | `~/.config/ca.kazo.irc/settings.json`: the console font (decision 19) and the logs (`logsEnabled`, on by default, decision 15). A missing, unreadable or broken file gives the defaults; an unknown field (added by a later work item) is ignored. |
| `Settings/IIrcFileSystem.cs`, `KazoIrcFileSystem.cs`, `AppScopedFileSystem.cs` | Reading, writing and appending the app's files; an in-memory version serves the tests. `AppScopedFileSystem` enters the app context for the writes made from the client's read loop (see "Logs"). |
| `Resources/Strings*.resx` | The interface texts, in English and French (ADR 0104). |
| `Resources/AppManifest.fr.resx` | French name and description, written to `localization/fr.json` by the build. |
| `wwwroot/icons/app.svg` | Illustrated icon in the app icon style (Kazo OS ADR 0232): the common dark tile, two chat bubbles, a `#` in the front one. |

### Layout

The window chooses its layout from **its own width** (CSS container query on
`.irc-window`), not from the screen: a narrow window on a computer behaves like
a phone.

| Window width | Layout |
| --- | --- |
| 720 px and more (`IrcApp.CompactWidth`) | Three columns: networks and channels (220 px), messages, users (220 px). |
| Under 720 px | One column: the messages. The toolbar's "Channels" and "Users" buttons open a panel that slides over it, from the left or the right; a dark backdrop, the panel's × button or Escape close it. Opening one closes the other. |

In the mobile shell, a transparent 1.75 rem strip at the top of every app
belongs to the notification shade (`.mobile-shade-handle`): the toolbar starts
under it (`.mobile-shell .irc-toolbar`), otherwise its buttons do not receive
taps.

### Theme and font

Every colour comes from the OS theme variables (`--kos-*`), with a fallback.
The `--irc-text-font` variable gives the font of the messages, the lists and the
input: the theme's interface font (`--kos-font-ui`), or its monospace font
(`--kos-font-mono`) when the "Console font" button is pressed (`aria-pressed`).
The choice is saved at once; if writing fails, the font still changes and the
status line says why the choice will not be kept.

### The app's folder

`KazoDirectory.CreateDirectoryAsync` looks at every parent folder before
creating (like `mkdir -p`); looking at `~/.config` needs
`filesystem.user-home.read`, which the app does not ask for. `KazoIrcFileSystem`
therefore creates only `~/.config/ca.kazo.irc/` (like `mkdir` without `-p`),
through the app's file gateway, when it does not exist.

## Chat (work item 4)

Work item 4 wires the protocol client to the window, with the demonstration
network as its only transport; work item 3 adds the real ones.

```text
IrcWindow ──> IrcChat ──> IrcSession (one per network) ──> IrcClient ──> IIrcTransport
                 │              └─ Conversation (server tab, channels, private) ──> ChatLine
                 ├─ IIrcNotifier (KazoIrcNotifier: OS notification + sound)
                 └─ IrcLogWriter ──> IIrcFileSystem (AppScopedFileSystem ──> KazoIrcFileSystem)
```

| Type | Role |
| --- | --- |
| `IrcNetwork`, `IrcNetworkCatalog`, `IIrcTransportFactory` | A network has a `TransportKind`; the catalog gives the client a function that creates a new transport of that kind at each connection attempt. Work item 4: kind `demo` only. Work item 3 adds `websocket` and `relay`. |
| `IrcSession` | One network: its `IrcClient` and its conversations, filled from the client's events. A message goes to its channel or private conversation; a private notice goes where that person already talks, or to the server tab; numerics, MOTD, WHOIS, errors and the connection's state go to the server tab. A private conversation follows a nickname change. |
| `Conversation` | The server tab, a channel or a private conversation: its last 1000 lines (`MaxLines`), `UnreadCount`, `HighlightCount`, its users and topic from the client's model. |
| `ChatLine` | Time, kind (message, action, notice, activity, server, error), nickname, text, `IsOwn`, `IsMention`. |
| `IrcChat` | The networks, the open conversation, the typed lines (`IrcInputParser` then `IrcClient.ExecuteAsync`; a refused line gives a `ChatInputProblem` the window translates), the counters, the notifications and the logs. `/join` opens the channel when the server confirms it (not after a reconnection); `/query` opens the private conversation. |
| `IrcNick` | The default nickname (decision 12): the OS user name, keeping the characters a nickname allows, at most 16. |

### What the user is told (decision 14)

| Event | Effect |
| --- | --- |
| Someone else's message in a conversation that is not open, or while the window does not have the focus | `UnreadCount` + 1: the conversation is bold in the list with a grey badge. |
| A **mention** (`IrcMentions`: the nickname as a whole word, any case) or a **private message**, not seen | `HighlightCount` + 1: the conversation is in the warning colour with an amber badge, and the window title becomes "(N) IRC", N = all the highlights (`IrcChat.AttentionCount`). A mention line is highlighted in the conversation. |
| The same while the window does **not** have the focus (another window active, or minimized) | An OS notification ("kazo-bot in #kazo", "kazo-bot (private)"), which the OS shows as a bubble and with the notification sound. Notices never notify. |
| The conversation is open and the window gets the focus back | Both counters go back to 0; the title becomes "IRC". |

The focus comes from the window events of the SDK (`OnWindowActivated`,
`OnWindowDeactivated`, `OnWindowStateChanged`). A private conversation started
by someone else does not take the user away from the open conversation.

### Input line

Enter sends; ↑ and ↓ walk the last 100 lines (`InputHistory`, the line being
typed is kept); Tab completes the last word with the users of the channel (or
the other person) and cycles through the matches, adding `": "` at the start of
the line (`NickCompleter`). Blazor cannot cancel one key only, so
`MessagePane.razor.js` cancels Tab's default action, and only while the input
holds text: an empty input lets Tab leave it. The lines follow the newest one
without script (`flex-direction: column-reverse`), and only the last 400 are
rendered.

### Logs (decision 15)

| Rule | Value |
| --- | --- |
| File | `~/.local/share/ca.kazo.irc/logs/<network>/<conversation>/YYYY-MM-DD.log`, the day and times in the OS time zone (`AppContext.TimeZone`). |
| Lines | `[HH:MM:SS] <nick> text`, `[HH:MM:SS] * nick action`, `[HH:MM:SS] -nick- notice`, `[HH:MM:SS] *** nick joined` (and left, quit, is now known as, changed the topic, sets mode, is away, is back). The server tab and server lines are not logged. |
| Names | `IrcLog.SafeName`: `/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|` and control characters become `_`; a leading dot becomes `_` (so never `.`, `..` or a hidden folder); never empty; at most 64 characters. The conversation's name is folded to the network's case rules (`#Kazo` and `#kazo` share a folder). |
| Order and errors | `IrcLogWriter` writes one line after the other. The first failure is shown once on the status line and never stops the chat or the next lines. |
| Option | The "Logs" button (`aria-pressed`) saves `logsEnabled` in `settings.json`. |
| Context | The lines arrive from the client's read loop, which the OS did not start: `KazoFile` would find no app context there. `AppScopedFileSystem` enters it (`KazoExecutionContext.Enter`) for each operation. |

Another app reading a log right after a line is written may see it a few
seconds later (the OS file system); the end-to-end test reads again.

### Demonstration network

`Kazo.Irc.Demo` is `DemoIrcServer`, an `IIrcTransport` that plays a small IRC
server in memory (no SDK reference). It registers the client (CAP, welcome
numerics, `005`, MOTD that says what to try), answers `PING`, `JOIN` (topic and
`NAMES` with `@kazo-bot`, `+morpheus`, `trinity`), `PART`, `TOPIC`, `WHOIS`,
`AWAY`, `NICK`, `MODE`, `QUIT`, refuses the bot's nickname (`433`) and unknown
commands (`421`). Its bot, `kazo-bot`:

| You write | The bot |
| --- | --- |
| a channel message that names it | answers at once and names you (a mention) |
| `!mention N` in a channel | names you in that channel N seconds later (3 by default, 60 at most) |
| `!private N` in a channel | sends you a private message N seconds later |
| `!links` in a channel | sends two web addresses, bold, colored and italic text, and text that looks like HTML (work item 5) |
| a private message to it | answers privately |

It is offered as "Kazo Demo" only when the app is built with
`IrcDemoNetwork=true`: by default in Debug, and in the CI with
`dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true` for the
end-to-end tests. Then `Kazo.Irc` references `Kazo.Irc.Demo` and defines
`IRC_DEMO` (`Chat/DemoNetwork.cs`, `Chat/IrcNetworks.cs`). The Store and the
packages build Release without it: the package has neither `Kazo.Irc.Demo.dll`
nor the demo code.

## Links and formatting (work item 5)

The text of a line (message, action, notice, activity, the channel topic in the
toolbar) goes through the `Components/MessageText.razor` component, which draws
the IRC formatting codes and makes web addresses clickable (decision 17).

| Part | Role |
| --- | --- |
| `IrcFormatting` (`Kazo.Irc.Protocol/Text`) | Reads the codes `\x02` bold, `\x1D` italics, `\x1F` underline, `\x1E` strikethrough, `\x11` monospace, `\x16` reverse, `\x0F` reset, `\x03` mIRC color (`fg[,bg]`, one or two digits, 99 = default) and `\x04` hexadecimal color, into runs of text sharing a style (`IrcTextRun`, `IrcTextStyle`). `Strip` removes the codes. |
| `MessageLinks` (`Kazo.Irc/Text`) | Finds `http://` and `https://` addresses: not inside a word, without a user name (`https://kazo.ca@evil.example`), ending at a formatting code or the first blank or `< > " \` { } \| ^` character, without the sentence's trailing punctuation (`. , ; : ! ? ' " * …` and quotes) nor a closing parenthesis or bracket with no opening one in the address (`(https://kazo.ca)`, while `…/IRC_(protocol)` keeps its own). Only `http` and `https` become links: never `javascript:`, `data:`, `file:`; `irc://` waits for [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2). |
| `MessageSegments` (`Kazo.Irc/Text`) | Puts both together: the style runs cut at the links' edges; a formatting code ends an address (`\x02https://kazo.ca\x02hello` links only `https://kazo.ca`). |
| `MessageText` | Each run is a text node (Razor encodes it): nothing from the network becomes HTML, `<b>` is shown as typed. A link is an `<a role="link" tabindex="0">` **without `href`**, its escaped absolute address (`Uri.AbsoluteUri`) in its tooltip: a middle click or "open in a new tab" cannot hand the address to the host browser; a click or Enter hands it to the window. Styles are classes of `MessageText.razor.css` (`irc-b`, `irc-fg-4`, `irc-bg-1`…), never inline styles (ADR 0007): the 16 standard mIRC colors are drawn; extended (16 to 98) and hexadecimal colors keep the default colors. |
| `IrcWindow.OpenLinkAsync` | Opens the address in a new window of the OS Browser (`KazoBrowser.OpenAsync`, capability `apps.launch`); a failure is shown in the status bar. |

The codes are also removed where the text must be plain: log lines,
notification text, the nickname search for mentions, and the topic's tooltip.

On the Browser's first launch in a fresh profile, Kazo OS 3.0.0 asks the
administrator to approve **the Browser's** `network.real.access` capability (not
IRC's); the end-to-end test approves it.

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

### Protocol tests

`tests/Kazo.Irc.Protocol.Tests` (xUnit, `Microsoft.Extensions.TimeProvider.Testing`
for the fake clock): messages and tags, encoding, cutting lines and texts, CTCP, formatting codes,
`CASEMAPPING`, ISUPPORT, input, and the client on `FakeIrcTransport`: CAP, SASL,
nick taken, channels, modes, topics, away, messages, CTCP, WHOIS, commands,
`QUIT`, reconnection, `PING`. Protocol coverage: 97 % of lines, 91 % of
branches.

```bash
dotnet test Kazo.Irc.slnx
```

## Tests

| Suite | Command | Content |
| --- | --- | --- |
| Protocol (xUnit) | `dotnet test Kazo.Irc.slnx` | see "Protocol tests" above. |
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifest (class and generated `manifest.json`, French texts, icon, capabilities), complete English and French texts, compact-mode panels, settings (path, defaults, broken or denied file, unknown field, failed write, logs option); chat on the demonstration server with a fake clock (connection, join, messages, mentions, private messages, unread, title counter, notifications with and without the focus, input problems, close, `QUIT`); mentions, default nickname, history, completion; logs (paths, hostile names, line format, time zone, option, failed write, codes removed); the demonstration server; links (punctuation, parentheses, refused schemes, tags and quotes from the network), segments and the styles' CSS classes, notification without codes. |
| End to end | `npm run test:e2e` after `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true` | in the host, fresh profile: three columns side by side on a computer in the OS theme; console font kept after closing and reopening; on a Pixel 7, one column and both panels; `chat.e2e.mjs`: connect to the demonstration network, `/join #kazo`, send and receive, history and Tab, a mention while the Terminal has the focus gives an OS notification and "(1) IRC", the log file read with `cat` in the Terminal, a private message counted, closing the window, the chat on a Pixel 7; `!links`: two links without the punctuation around them, bold, color and italics drawn, tags shown as text, a click opens the OS Browser on the address without leaving the page. |

The end-to-end tests start the host (`dotnet run --no-build -c Release`) on a
free port, or use `IRC_BASE_URL`.

## Progress

| Work item | What it added here |
| --- | --- |
| 1. `irc/1-solution` | Solution, host, CI, manifest, icon, empty window and layout, console font, tests. |
| 2. `irc/2-protocol` | The `Kazo.Irc.Protocol` library and its tests (the "Protocol" section above). |
| 4. `irc/4-notifications-and-logs` | The chat wired to the window on the demonstration network, notifications and title counter, logs (the "Chat" section above). |
| 5. `irc/5-links` | Clickable web addresses opened in the Browser, IRC formatting codes drawn or removed (the "Links and formatting" section above). |

## Work item 2 tasks

- [x] Project `src/Kazo.Irc.Protocol` with no Kazo SDK dependency.
- [x] Reading and writing messages: tags, source, parameters, limits, UTF-8 and Latin-1 fallback.
- [x] `IIrcTransport` and `FakeIrcTransport`.
- [x] Client: registration, CAP 302, SASL PLAIN, `PING`/`PONG`, nick taken, reconnection with a growing delay and back into the channels.
- [x] Model of channels and users: prefixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Input commands `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] xUnit tests.
- [x] Wiring to the UI: done by work item 4 (with the demonstration network); the real transports come with work item 3.

## Work item 4 tasks

- [x] One session per network holding an `IrcClient`; transport factory (`IIrcTransportFactory`) for work item 3 to complete.
- [x] Demonstration network in memory (`Kazo.Irc.Demo`), only with `IrcDemoNetwork` (Debug, CI E2E), never in the Store package.
- [x] Networks and conversations list, message view (time, nickname, `/me`, notices, server lines), users with prefixes.
- [x] Input line: commands of decision 18 through `ExecuteAsync`, history (↑/↓), nickname completion (Tab); desktop and mobile.
- [x] Closing the window sends `QUIT` (decision 8).
- [x] Mentions and private messages: OS notification and sound without the focus, "(N) IRC" title, unread and mention badges (decision 14).
- [x] Logs per day in `~/.local/share/ca.kazo.irc/logs/`, safe names, `logsEnabled` option (decision 15).
- [x] Capabilities `notifications.post`, `filesystem.user-home.read` and `.write`.
- [x] xUnit and Playwright tests; docs (fr, en), plan progress.

## Work item 5 tasks

- [x] IRC formatting codes read into styles (`IrcFormatting`, protocol library) and removed in logs, notifications and the mention search.
- [x] `http://` and `https://` addresses found without the trailing punctuation (`MessageLinks`).
- [x] Drawn by `MessageText`: text only (no HTML from the network), intercepted `<a>` links, styles as CSS classes (16 mIRC colors).
- [x] Click: a new window of the OS Browser (`KazoBrowser.OpenAsync`), capability `apps.launch`.
- [x] `!links` command of the demonstration bot.
- [x] xUnit tests (formatting, links, segments, classes, logs, notifications) and Playwright (a click opens the Browser).
- [x] Docs (fr, en), plan progress.
