namespace Kazo.Irc.Protocol.Client;

/// <summary>A channel to join, with its key when it has one.</summary>
/// <param name="Name">The channel name (<c>#kazo</c>).</param>
/// <param name="Key">The channel key, or null.</param>
public sealed record IrcChannelJoin(string Name, string? Key = null);

/// <summary>The account and password of a SASL PLAIN login.</summary>
/// <param name="Account">The services account.</param>
/// <param name="Password">The password; the app keeps it in the OS keyring, never in a file.</param>
public sealed record IrcSaslCredentials(string Account, string Password)
{
    /// <summary>Hides the password.</summary>
    public override string ToString() => $"IrcSaslCredentials {{ Account = {Account} }}";
}

/// <summary>How the client registers and behaves on one network.</summary>
public sealed record IrcClientOptions
{
    /// <summary>The IRCv3 capabilities the client asks for when the server offers them (plan, decision 18).</summary>
    public static IReadOnlyList<string> DefaultCapabilities { get; } =
        ["message-tags", "server-time", "away-notify", "multi-prefix", "account-notify", "sasl"];

    /// <summary>The nickname wanted (by default the OS user name, chosen by the app).</summary>
    public required string Nick { get; init; }

    /// <summary>Nicknames tried, in order, when <see cref="Nick"/> is taken; then the client adds <c>_</c> and digits.</summary>
    public IReadOnlyList<string> AlternateNicks { get; init; } = [];

    /// <summary>The user name (ident) of <c>USER</c>; null uses the nickname.</summary>
    public string? UserName { get; init; }

    /// <summary>The real name of <c>USER</c>.</summary>
    public string RealName { get; init; } = "Kazo OS IRC";

    /// <summary>The server password (<c>PASS</c>), or null.</summary>
    public string? ServerPassword { get; init; }

    /// <summary>The SASL PLAIN login, or null to connect without one.</summary>
    public IrcSaslCredentials? Sasl { get; init; }

    /// <summary>The capabilities to ask for.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = DefaultCapabilities;

    /// <summary>The channels joined after the first registration.</summary>
    public IReadOnlyList<IrcChannelJoin> AutoJoin { get; init; } = [];

    /// <summary>When the client tries again after a lost connection.</summary>
    public IrcReconnectPolicy Reconnect { get; init; } = new();

    /// <summary>Silence from the server after which the client sends a <c>PING</c>.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Further silence after that <c>PING</c> after which the connection is held lost.</summary>
    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long <c>QUIT</c> waits for the server to close the connection before closing it itself.</summary>
    public TimeSpan QuitTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The answer to a CTCP <c>VERSION</c> query.</summary>
    public string VersionReply { get; init; } = "Kazo OS IRC";

    /// <summary>The quit message of a <c>QUIT</c> without one.</summary>
    public string DefaultQuitMessage { get; init; } = "Kazo OS IRC";
}
