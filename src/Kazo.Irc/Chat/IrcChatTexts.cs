namespace Kazo.Irc.Chat;

/// <summary>
/// The texts the chat model writes itself (connection lines, notification titles). The window gives
/// the translated ones (<c>Resources/Strings*.resx</c>); the defaults are the English ones. Each is
/// a composite format string.
/// </summary>
public sealed record IrcChatTexts
{
    /// <summary>{0}: the network. A line of the server tab.</summary>
    public string Connecting { get; init; } = "Connecting to {0}...";

    /// <summary>{0}: the nickname. A line of the server tab.</summary>
    public string Connected { get; init; } = "Connected as {0}.";

    /// <summary>{0}: seconds, {1}: attempt.</summary>
    public string Reconnecting { get; init; } = "Connection lost. New attempt in {0} s (attempt {1}).";

    /// <summary>No argument.</summary>
    public string Disconnected { get; init; } = "Disconnected.";

    /// <summary>{0}: nickname, {1}: user@host, {2}: real name, {3}: server, {4}: channels.</summary>
    public string Whois { get; init; } = "{0} is {1} ({2}), on {3}. Channels: {4}";

    /// <summary>{0}: nickname.</summary>
    public string WhoisUnknown { get; init; } = "{0}: no such nickname.";

    /// <summary>{0}: who, {1}: the channel. Title of a mention's notification.</summary>
    public string MentionTitle { get; init; } = "{0} in {1}";

    /// <summary>{0}: who. Title of a private message's notification.</summary>
    public string PrivateTitle { get; init; } = "{0} (private)";
}
