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

    /// <summary>
    /// What a connection failure means, by <c>IrcTransportErrors</c> code (work item 3). {0}: the
    /// network, {1}: the technical message. A code with no text shows the technical message.
    /// </summary>
    public IReadOnlyDictionary<string, string> TransportErrors { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NO_PERMISSION"] = "IRC may not reach the real network: an administrator must allow its « real network » permission (Settings, app permissions).",
        ["NO_SERVER"] = "{0} needs the Kazo server: no Kazo server relays IRC (none connected, relay off, or the permission has not reached it yet).",
        ["REFUSED"] = "{0} refused the connection (the IRC server, or the Kazo server, which may block this port).",
        ["CERTIFICATE_REFUSED"] = "The TLS certificate of {0} was refused: {1}",
        ["SIMULATED_HOST"] = "This address is on the simulated network, which has no IRC server yet.",
        ["UNREACHABLE"] = "{0} could not be reached: {1}",
        ["LOST"] = "The connection to {0} was lost: {1}",
        ["NOT_SUPPORTED"] = "This connection cannot be opened here: {1}",
    };
}
