> [Version française](readme-fr.md)

# Os-App-IRC

Native IRC chat client for [Kazo OS](https://github.com/Kazo-ca/Os), written in C# and Blazor, for
real IRC networks on desktop and mobile. It connects over WebSocket without any server, and through
the optional Kazo OS server's relay (TCP with TLS) to networks that do not accept WebSocket.

**State:** in development (series `irc/base`). The app opens in Kazo OS with its layout (three
columns on a computer, one column with side panels on a phone) and the console font option; the
IRC protocol and the connection come with the next work items (see the plan's progress table).

- Plan: [English](doc/en/plan.md) · [Français](doc/fr/plan.md)
- Architecture of the code: [English](doc/en/architecture.md) · [Français](doc/fr/architecture.md)
- License: MIT ([LICENSE](LICENSE)); third-party works: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build and run

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-IRC.git
cd Os-App-IRC
dotnet run --project src/Kazo.Irc.Os      # Kazo OS with the IRC app, on http://localhost:5310
```

The `external/KazoOs` submodule is Kazo OS on its release tag (3.0.0): it gives the version of the
Kazo OS packages. `Debug` builds the OS from the submodule's sources, `Release` takes the packages
from nuget.org (what CI and the Kazo OS Store build).

## Tests

```bash
dotnet build Kazo.Irc.slnx -c Release
dotnet test Kazo.Irc.slnx -c Release --no-build
npm ci && npx playwright install chromium
npm run test:e2e                            # the app in the Kazo OS host, desktop and phone
```
