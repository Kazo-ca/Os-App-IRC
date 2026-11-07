> [Version française](readme-fr.md)

# Os-App-IRC

Native IRC chat client for [Kazo OS](https://github.com/Kazo-ca/Os), written in C# and Blazor, for
real IRC networks on desktop and mobile. It connects over WebSocket without any server, and through
the optional Kazo OS server's relay (TCP with TLS) to networks that do not accept WebSocket.

**Version 1.0.0**, for Kazo OS 3.2.0 or later, on a computer and on a phone. It connects **without
any server** over WebSocket (UnrealIRCd is proposed, and any network that accepts IRCv3 over
WebSocket can be added) and **through the Kazo server** to the other networks (Libera.Chat and OFTC
are proposed), with TLS on 6697. An administrator approves the real network the first time IRC
opens. Mentions and private messages give an OS notification and a "(N) IRC" title, web addresses
open in the Kazo OS Browser, the conversations are logged per day in
`~/.local/share/ca.kazo.irc/logs/`, the networks are edited in the app ("Manage networks",
`~/.config/ca.kazo.irc/networks.json`) and a SASL password is kept in the OS keyring.

**Install:** from the Kazo OS Store (category Communication), or in the Terminal of Kazo OS as an
administrator: `sudo kazopkg install Kazo-ca/Os-App-IRC`. The Store listing is in [`store/`](store).

- Plan: [English](doc/en/plan.md) · [Français](doc/fr/plan.md)
- Architecture of the code: [English](doc/en/architecture.md) · [Français](doc/fr/architecture.md)
- License: MIT ([LICENSE](LICENSE)); third-party works: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build and run

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-IRC.git
cd Os-App-IRC
dotnet run --project src/Kazo.Irc.Os      # Kazo OS with the IRC app, on http://localhost:5310
```

The `external/KazoOs` submodule is Kazo OS on its release tag (3.2.0): it gives the version of the
Kazo OS packages. `Debug` builds the OS from the submodule's sources, `Release` takes the packages
from nuget.org (what CI and the Kazo OS Store build).

## Tests

```bash
dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true   # the E2E tests use the demonstration network
dotnet test Kazo.Irc.slnx -c Release --no-build
npm ci && npx playwright install chromium
npm run test:e2e                            # the app in the Kazo OS host, desktop and phone
```

The Store build has no demonstration network, and puts the protocol library inside `Kazo.Irc.dll`
(Kazo OS 3.2.0 packs one assembly per app, [Os#419](https://github.com/Kazo-ca/Os/issues/419)):

```bash
dotnet build src/Kazo.Irc -c Release -t:KazoOsPack       # the package, in src/Kazo.Irc/bin/kazoos-packages
npm run test:installed -- <Kazo OS wwwroot> src/Kazo.Irc/bin/kazoos-packages   # installed by kazopkg, in a published Kazo OS
dotnet build src/Kazo.Irc.Os -c Release && npm run store:screenshots          # store/screenshots/
```

See "Kazo OS Store listing and installed app" in the [architecture](doc/en/architecture.md).
