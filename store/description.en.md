# IRC

A native Kazo OS chat client, written in C# and Blazor, for **real IRC networks**, on a computer and
on a phone. It follows the OS theme; a fixed-width font is offered for a "console" look.

## Connecting

- **Without any server, over WebSocket.** The proposed network is UnrealIRCd
  (`wss://irc.unrealircd.org/`). Any network that accepts IRC over WebSocket (IRCv3,
  `text.ircv3.net`) can be added with "Manage networks".
- **Through the Kazo server, for the other networks.** Libera.Chat and OFTC do not accept WebSocket:
  IRC goes through the relay of the optional Kazo OS server, over TCP, with TLS on port 6697 (6667
  in clear). These networks are marked "Kazo server required". The option is always offered and
  tried when you connect; with no Kazo server connected, IRC says so clearly ("No Kazo server
  connected").
- Your nickname is your Kazo OS user name; it can be changed per network. The SASL password of an
  account is kept in the OS keyring, never in a file.
- The networks and the channels they join automatically are in
  `~/.config/ca.kazo.irc/networks.json`.

## Administrator approval

IRC asks for access to the **real network** (`network.real.access`), a capability Kazo OS reserves to
the administrator. At the first launch, Kazo OS asks an administrator to approve it; the approval
window also lists the ports the app declares for the Kazo server's relay (`tcp/6697` and
`tcp/6667`).

## Chatting

- Three columns on a computer (networks and channels, messages, users); one column with side panels
  on a phone.
- Commands `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`, input history (↑ and
  ↓) and nickname completion (Tab).
- Common IRCv3: CAP, SASL, server-time, message-tags, away-notify, multi-prefix, account-notify;
  automatic reconnection that rejoins the channels.
- A mention or a private message while the window does not have the focus: a Kazo OS notification
  with its sound, a count in the title ("(2) IRC") and badges in the channel list.
- IRC colors and bold are drawn; a web address in a message opens in the Kazo OS Browser.
- One window; it stays connected when minimized, and closing it quits the networks.

## Logs

Conversations are written per day, as text, in
`~/.local/share/ca.kazo.irc/logs/<network>/<channel>/YYYY-MM-DD.log`, where the other Kazo OS apps
can read them. The "Logs" button of the toolbar turns them off.

## Limits of the first version

- No file transfer (DCC): it would need to accept incoming connections.
- `irc://` links do not open in IRC yet (Kazo OS cannot associate an address scheme with an app yet).
- Needs Kazo OS 3.2.0 or later.

## License

IRC is free software under the MIT license. It bundles no third-party work: the code, the interface
and the icon are this repository's own.
