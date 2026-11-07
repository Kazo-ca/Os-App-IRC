using Kazo.Os.App.Abstractions;

namespace Kazo.Irc;

/// <summary>
/// The app's manifest (decisions 7, 8 and 20 of doc/en/plan.md). The build generates
/// <c>manifest.json</c> from this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 1 declares the base: one window per user, on the desktop and on phones, and the app's
/// own folder for its settings (<c>~/.config/ca.kazo.irc/settings.json</c>). Each later work item
/// adds the capabilities its code uses, as the plan lists them: <c>network.real.access</c> and the
/// real ports with the connection (work item 3, after Kazo OS ADR 0235 §3), the notifications and
/// the logs (work item 4, done: <c>notifications.post</c>, <c>filesystem.user-home.read</c> and
/// <c>.write</c>), opening a web address in the Browser (work item 5).
/// </remarks>
[AppManifest]
public sealed class IrcApp : AppDefinition
{
    /// <summary>Width under which the window shows one column and its side panels (phones, a narrow window).</summary>
    public const int CompactWidth = 720;

    /// <inheritdoc />
    public override string Id => "ca.kazo.irc";

    /// <inheritdoc />
    public override string Name => "IRC";

    /// <inheritdoc />
    public override AppKind Kind => AppKind.Window;

    /// <inheritdoc />
    public override Type EntryPoint => typeof(IrcWindow);

    /// <inheritdoc />
    /// <remarks>Decision 7: the same window on phones, in one column with side panels.</remarks>
    public override Type? MobileEntryPoint => typeof(IrcWindow);

    /// <inheritdoc />
    public override string PublisherId => "pub.kazo";

    /// <inheritdoc />
    public override string Description => "Chat on real IRC networks";

    /// <inheritdoc />
    public override string Category => "communication";

    /// <inheritdoc />
    /// <remarks>The tag that publishes to the Kazo OS Store must be v + this version.</remarks>
    public override string Version => "1.0.0";

    /// <inheritdoc />
    /// <remarks>
    /// Decision 8: one window per user holds every network and channel; launching the app again
    /// brings that window back.
    /// </remarks>
    public override bool SingleInstancePerUser => true;

    /// <inheritdoc />
    public override IReadOnlyList<string> Capabilities =>
    [
        // ~/.config/ca.kazo.irc/: settings.json (the console font of work item 1) and, from work
        // item 3, networks.json.
        AppCapabilities.FileSystemPrivateRead,
        AppCapabilities.FileSystemPrivateWrite,

        // Work item 4, decision 14: a mention or a private message while the window does not have
        // the focus posts an OS notification (which plays the notification sound).
        AppCapabilities.NotificationsPost,

        // Work item 4, decision 15: the logs in ~/.local/share/ca.kazo.irc/logs/, in the user's
        // home, where the user can read them with the other apps.
        AppCapabilities.FileSystemUserHomeRead,
        AppCapabilities.FileSystemUserHomeWrite,
    ];

    /// <inheritdoc />
    public override IReadOnlyList<string> Icons => ["icons/app.svg"];

    /// <inheritdoc />
    public override IReadOnlyList<AssetManifest> Assets =>
        [new("icons/app.svg", AssetKind.Image, new string('0', 64), "app-icon")];

    /// <inheritdoc />
    /// <remarks>
    /// Opens wide enough for the three columns (channels, messages, users); a window narrower than
    /// <see cref="CompactWidth"/> switches to one column, so it can be made as small as a phone.
    /// </remarks>
    public override WindowOptions? Window => new()
    {
        DefaultWidth = 960,
        DefaultHeight = 620,
        MinWidth = 340,
        MinHeight = 400,
    };
}
