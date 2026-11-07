namespace Kazo.Irc.Layout;

/// <summary>
/// A side panel of the compact layout (one column, on phones and in a narrow window): the panel
/// slides over the messages until it is closed.
/// </summary>
public enum IrcPanel
{
    /// <summary>No panel: the messages fill the window.</summary>
    None,

    /// <summary>The networks and their channels, on the left.</summary>
    Channels,

    /// <summary>The users of the current channel, on the right.</summary>
    Users,
}
