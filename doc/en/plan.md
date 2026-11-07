> [Version française](../fr/plan.md)

# Plan: the IRC app for Kazo OS

Plan **validated on 2026-11-07** by Benoit (thread "Planifier l'app IRC", plan
merged by PR #3). This document sets the goal, the decisions and the order of
the work; progress is at the end of the page.

## Goal

IRC is a **native Kazo OS chat client** for real IRC networks (Libera.Chat,
OFTC, etc.), written in C# and Blazor, on desktop and mobile. It connects like
a real client:

- **without the optional server**, over WebSocket, to networks that accept IRC
  over WebSocket;
- **with the server**, through its TCP relay (TLS on port 6697), to networks
  that do not.

It is a third-party app in its own repository, following the
`kazoos-solution` template like Chess and Webamp, to be published in the Kazo
OS Store.

## Decisions

Benoit's answers on 2026-11-07 ("ok" or "agreed" = the recommendation).

| # | Topic | Decision |
| --- | --- | --- |
| 1 | Goal | Chat on **real IRC networks**. |
| 2 | Client base | **Native C# / Blazor client**, layout inspired by KiwiIRC (KiwiIRC itself is not used: Vue 2 is no longer maintained). WebSocket is kept so the app does not depend on the server; an option, available only when the server is connected, reaches networks that do not accept WebSocket. |
| 3 | Transport | **Both**: direct WebSocket (`KazoClientWebSocket`, new in the SDK) when the network accepts it; the server's TCP relay (`KazoTcpClient`) otherwise. Kazo OS [ADR 0235](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0235-real-network-for-chat-apps.md). |
| 4 | TLS | The **relay opens TLS** to the IRC server; the app uses 6697 by default. OS change (ADR 0235 §2). |
| 5, 9 | Ports | The app **declares its ports** (6697, 6667) in its manifest; the `network.real.access` approval shows them; the relay accepts them unless its policy refuses them (`BlockedPorts`). ADR 0235 §3. |
| 6 | Simulated IRC server | It must go through TCP: not in the first version. Later: [#1](https://github.com/Kazo-ca/Os-App-IRC/issues/1), blocked by [Os#393](https://github.com/Kazo-ca/Os/issues/393) (TCP to a simulated host). |
| 7 | Mobile | Desktop **and** mobile from the first version (one column on mobile). |
| 8 | Window | One window (`SingleInstancePerUser`) with every network and channel. It stays connected when minimized; closing it disconnects cleanly (`QUIT`). |
| 10 | OS changes | One ADR (0235) in three sections and a `real-network/base` series in Kazo OS: [Os#395](https://github.com/Kazo-ca/Os/issues/395). |
| 11 | WebSocket capability | The same `network.real.access` as the relay: one grant "this app talks to the real network". |
| 12 | Nickname and password | Default nickname = the OS user name. SASL PLAIN; the password is kept in the OS's encrypted keyring (`KazoKeyring`), never in a clear-text file. |
| 13 | Offered networks | A short editable list: Libera.Chat, OFTC, and one network that accepts WebSocket. A network without WebSocket is marked "Kazo server required". Work item 3 checks which networks accept WebSocket. **Work item 3:** Libera.Chat and OFTC have none (relay); UnrealIRCd (`wss://irc.unrealircd.org/`) has one, chosen by Benoit on 2026-11-07. |
| 14 | Mentions and private messages | When the window is not focused: OS notification, notification sound, unread count in the window title. Unread channels highlighted in the list. |
| 15 | Logs | On by default, as text in `~/.local/share/ca.kazo.irc/logs/<network>/<channel>/YYYY-MM-DD.log`; an option turns them off. |
| 16 | `irc://` links | Kazo OS cannot associate a URL scheme with an app yet: [Os#394](https://github.com/Kazo-ca/Os/issues/394), linked to [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2). |
| 17 | Files and links | No DCC (it would need incoming connections). Web addresses in messages open in the OS Browser. |
| 18 | IRC features | Base protocol + common IRCv3: CAP, SASL, server-time, message-tags, away-notify, multi-prefix, account-notify. Commands `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`. |
| 19 | Look | Follows the OS theme (modern look), optional monospace font for a "console" feel. |
| 20 | Identity | Id `ca.kazo.irc`, name "IRC", publisher `pub.kazo`, category `communication`, version `1.0.0`, MIT license. |
| 21 | Documents | This plan in this repository (fr + en); ADR 0235 in Kazo OS; the IRC row of `features.md` says "native client". |

## The app

| Item | Value |
| --- | --- |
| Manifest | `[AppManifest]` `IrcApp`: window, desktop and mobile, `SingleInstancePerUser`, declared real ports `tcp/6697` and `tcp/6667` (exact shape set by ADR 0235 in code). |
| Capabilities | `network.real.access` (dangerous: approved at install, with the ports), `notifications.post`, `filesystem.private.read` and `.write` (`~/.config/ca.kazo.irc/`), `filesystem.user-home.write` (logs in `~/.local/share/ca.kazo.irc/`), `apps.launch` (open an address in the Browser). The SASL password goes in the app's keyring, no capability needed. |
| Transports | An `IIrcTransport` interface (incoming and outgoing text lines) with three implementations: WebSocket (`KazoClientWebSocket`, subprotocol `text.ircv3.net`), TCP relay (`KazoTcpClient`, TLS by the relay), and a fake for tests. Chosen per network; the "through the Kazo server" option is offered only when the server is connected. **Changed in work item 3 (Benoit's choice, 2026-11-07):** an app cannot see the server without `server.connection.manage` (dangerous); the option is therefore always offered and **tried on demand**: "Kazo server required", then "No Kazo server connected" after a failure. A detection API is asked of Kazo OS ([Os#418](https://github.com/Kazo-ca/Os/issues/418)). |
| Protocol | A C# library in the solution: parsing and writing messages (IRCv3 tags included), CAP negotiation, SASL PLAIN, `PING`/`PONG`, reconnection with growing delay, rejoining open channels after a reconnection. |
| Interface | Desktop: three columns (networks and channels, messages, channel users), input with history and nickname completion (Tab). Mobile: one column, channel list and user list as panels. |
| Settings | `~/.config/ca.kazo.irc/networks.json`: networks (address, port, transport, TLS, nickname, SASL account, auto-joined channels); `~/.config/ca.kazo.irc/settings.json`: logs, font, notifications. |
| Logs | One timestamped line per message (`[HH:MM:SS] <nick> text`), one file per day. |

## Work, in order

Series `irc/base` (one `irc/<n>-<topic>` branch and one PR per work item).

0. **Kazo OS prerequisite**: ADR 0235 coded ([Os#395](https://github.com/Kazo-ca/Os/issues/395), `real-network/base` series) and shipped in an OS release. Work items 1 and 2 do not depend on it.
1. `irc/1-solution`: `kazoos-solution` on Kazo OS 3.0.0, manifest, single window, desktop and mobile layout, theme and font, `THIRD-PARTY-NOTICES.md`.
2. `irc/2-protocol`: protocol library (messages, CAP, SASL, IRCv3, commands), `IIrcTransport` and fake transport, channels and users, wired to the interface with a fake server.
3. `irc/3-connect` (after the OS release with ADR 0235): WebSocket and TCP + TLS relay transports, network list and editor, nickname, SASL password in the keyring, reconnection, transport choice depending on the server.
4. `irc/4-notifications-and-logs`: notifications for mentions and private messages, title counter, unread channels, file logs and their option.
5. `irc/5-links`: web addresses open in the Browser; `irc://` links wait for [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2).
6. `irc/6-store`: `store/` folder (icon, fr and en descriptions explaining WebSocket and the Kazo server, screenshots, `communication` category), end-to-end tests, tag `v1.0.0`, catalog PR (signing only in the Os-AppMarketPlace repository).

## Tests

Like Webamp: C# tests (xUnit) for the protocol (message and tag parsing, CAP and SASL, reconnection, commands), settings and logs; Playwright journeys in the host with a fake WebSocket IRC server (connect, join a channel, receive a mention and its notification, private message, reconnection); one run through the relay when a Kazo server is present.

## Licenses

- This repository: MIT.
- No KiwiIRC code is reused; any third-party icon or file would be listed in
  `THIRD-PARTY-NOTICES.md`.

## Later

- Simulated IRC server: [#1](https://github.com/Kazo-ca/Os-App-IRC/issues/1) (after [Os#393](https://github.com/Kazo-ca/Os/issues/393)).
- `irc://` and `ircs://` links: [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2) (after [Os#394](https://github.com/Kazo-ca/Os/issues/394)).
- DCC, image previews, other SASL mechanisms.

## Progress

On 2026-11-07, Benoit started the work items. Series `irc/base` (created from
`main`), one PR per work item to `irc/base`, then one PR `irc/base` → `main`
that Benoit merges. On the Kazo OS side, series `real-network/base`
(ADR 0235).

| Work item | Branch | Depends on | State |
| --- | --- | --- | --- |
| Kazo OS 1: `KazoClientWebSocket` | `real-network/1-websocket` (Kazo-ca/Os) | nothing | Done (Kazo OS 3.2.0) |
| Kazo OS 2: TLS by the relay | `real-network/2-relay-tls` (Kazo-ca/Os) | nothing | Done (Kazo OS 3.2.0) |
| Kazo OS 3: declared ports, `BlockedPorts` | `real-network/3-declared-ports` (Kazo-ca/Os) | nothing | Done (Kazo OS 3.2.0) |
| 1. Solution, manifest, window, desktop and mobile layout | `irc/1-solution` | nothing | Done ([#5](https://github.com/Kazo-ca/Os-App-IRC/pull/5)) |
| 2. IRC protocol, IRCv3, fake transport | `irc/2-protocol` | nothing | Done ([#4](https://github.com/Kazo-ca/Os-App-IRC/pull/4)) |
| 3. WebSocket and relay connection, networks, SASL | `irc/3-connect` | 1, 2, OS release with ADR 0235 | In review ([#8](https://github.com/Kazo-ca/Os-App-IRC/pull/8)) |
| 4. Chat wired to the interface (fake transport), notifications and logs | `irc/4-notifications-and-logs` | 1, 2 | Done ([#6](https://github.com/Kazo-ca/Os-App-IRC/pull/6)) |
| 5. Web links | `irc/5-links` | 4 (message display) | Done ([#7](https://github.com/Kazo-ca/Os-App-IRC/pull/7)) |
| 6. Store listing, `v1.0.0` | `irc/6-store` | 3, 4, 5 | To do |
