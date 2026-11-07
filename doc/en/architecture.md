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
| `IrcApp.cs` | Manifest (`[AppManifest]`): id `ca.kazo.irc`, name "IRC", version `1.0.0`, publisher `pub.kazo`, category `communication` (decision 20), **desktop and mobile** (`MobileEntryPoint` = the same window, decision 7), `SingleInstancePerUser` (decision 8). 960×620 window, resizable down to 340×400. Work item 1 capabilities: `filesystem.private.read` and `.write` only (the settings file). Each work item adds those its code uses: `network.real.access` and the real ports in work item 3 (after Kazo OS ADR 0235 §3), notifications and logs in work item 4, `apps.launch` in work item 5. |
| `IrcWindow.razor`, `.razor.cs`, `.razor.css` | The single window: toolbar, three columns, compact-mode panels, status line. |
| `Components/ChannelList` | Left column: the networks and their channels (empty in work item 1). |
| `Components/MessagePane` | Middle column: the messages (`role="log"`) and the input line (disabled while there is no connection). |
| `Components/UserList` | Right column: the channel's users (empty in work item 1). |
| `Layout/IrcLayout.cs`, `IrcPanel.cs` | Which side panel is open in compact mode (none, channels or users; one at a time). |
| `Settings/IrcSettings.cs` | `~/.config/ca.kazo.irc/settings.json`: the console font (decision 19). A missing, unreadable or broken file gives the defaults; an unknown field (added by a later work item) is ignored. |
| `Settings/IIrcFileSystem.cs`, `KazoIrcFileSystem.cs` | Reading and writing the app's files; an in-memory version serves the tests. |
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
for the fake clock): messages and tags, encoding, cutting lines and texts, CTCP,
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
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifest (class and generated `manifest.json`, French texts, icon), complete English and French texts, compact-mode panels, settings (path, defaults, broken or denied file, unknown field, failed write). |
| End to end | `npm run test:e2e` after `dotnet build Kazo.Irc.slnx -c Release` | in the host, fresh profile: three columns side by side on a computer in the OS theme; console font kept after closing and reopening; on a Pixel 7, one column and both panels. |

The end-to-end tests start the host (`dotnet run --no-build -c Release`) on a
free port, or use `IRC_BASE_URL`.

## Progress

| Work item | What it added here |
| --- | --- |
| 1. `irc/1-solution` | Solution, host, CI, manifest, icon, empty window and layout, console font, tests. |
| 2. `irc/2-protocol` | The `Kazo.Irc.Protocol` library and its tests (the "Protocol" section above). |

## Work item 2 tasks

- [x] Project `src/Kazo.Irc.Protocol` with no Kazo SDK dependency.
- [x] Reading and writing messages: tags, source, parameters, limits, UTF-8 and Latin-1 fallback.
- [x] `IIrcTransport` and `FakeIrcTransport`.
- [x] Client: registration, CAP 302, SASL PLAIN, `PING`/`PONG`, nick taken, reconnection with a growing delay and back into the channels.
- [x] Model of channels and users: prefixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Input commands `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] xUnit tests.
- [ ] Wiring to the UI: work items 3 and 4 (work item 1 was not merged when work item 2 ended).
