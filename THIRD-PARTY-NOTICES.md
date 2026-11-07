# Third-party notices

This repository is under the MIT license ([LICENSE](LICENSE)).

It bundles **no third-party work**: the code, the interface and the icon
(`src/Kazo.Irc/wwwroot/icons/app.svg`) are this repository's own. The layout is inspired by
[KiwiIRC](https://github.com/kiwiirc/kiwiirc) (three columns: channels, messages, users), but no
code, style sheet, image or text of KiwiIRC is copied (decision 2 of the plan).

The app is built on Kazo OS (`external/KazoOs` as a submodule for development, the `Kazo.Os.*`
packages of nuget.org for release builds), under that repository's MIT license; it is a reference,
not a bundled copy.

If a later work item adds a third-party file (a library, an icon, a sound), it is listed here with
its version, license and SHA-256, and its license file is kept next to it.
