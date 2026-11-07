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
│  └─ Kazo.Irc.Os/           # host: Kazo OS, its base apps and IRC
├─ tests/
│  ├─ Kazo.Irc.Tests/        # C# unit tests (xUnit)
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

## Tests

| Suite | Command | Content |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifest (class and generated `manifest.json`, French texts, icon), complete English and French texts, compact-mode panels, settings (path, defaults, broken or denied file, unknown field, failed write). |
| End to end | `npm run test:e2e` after `dotnet build Kazo.Irc.slnx -c Release` | in the host, fresh profile: three columns side by side on a computer in the OS theme; console font kept after closing and reopening; on a Pixel 7, one column and both panels. |

The end-to-end tests start the host (`dotnet run --no-build -c Release`) on a
free port, or use `IRC_BASE_URL`.

## Progress

| Work item | What it added here |
| --- | --- |
| 1. `irc/1-solution` | Solution, host, CI, manifest, icon, empty window and layout, console font, tests. |
