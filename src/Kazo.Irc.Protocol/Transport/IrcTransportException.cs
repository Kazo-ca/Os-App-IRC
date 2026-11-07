namespace Kazo.Irc.Protocol.Transport;

/// <summary>
/// A connection that could not be opened or was lost, with a stable <see cref="Code"/> the app
/// translates into a clear message, and whether trying again can help.
/// </summary>
/// <remarks>
/// A transport throws it from <see cref="IIrcTransport.ConnectAsync"/> (or a read or write). The
/// client (<see cref="Client.IrcClient"/>) reports it as an <see cref="Client.IrcClientError"/>
/// with this <see cref="Code"/>, and stops reconnecting when <see cref="IsPermanent"/> is true: a
/// missing permission or a refused certificate does not fix itself in 2, 4 or 8 seconds.
/// </remarks>
public sealed class IrcTransportException : IOException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">The stable code (see <see cref="IrcTransportErrors"/>).</param>
    /// <param name="message">A technical message (the app shows its own text for the code).</param>
    /// <param name="isPermanent">Whether reconnecting is pointless until the user changes something.</param>
    /// <param name="innerException">The error of the network API, if any.</param>
    public IrcTransportException(string code, string message, bool isPermanent, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        Code = code;
        IsPermanent = isPermanent;
    }

    /// <summary>The stable code of the failure.</summary>
    public string Code { get; }

    /// <summary>Whether reconnecting is pointless until the user changes something.</summary>
    public bool IsPermanent { get; }
}

/// <summary>The codes of <see cref="IrcTransportException"/>.</summary>
public static class IrcTransportErrors
{
    /// <summary>The app may not reach the real network (its capability is missing or refused).</summary>
    public const string NoPermission = "NO_PERMISSION";

    /// <summary>No Kazo server relays the connection (none connected, relay off, or no grant yet).</summary>
    public const string NoServer = "NO_SERVER";

    /// <summary>The connection was refused: by the IRC server, or by the relay for a blocked port.</summary>
    public const string Refused = "REFUSED";

    /// <summary>The TLS certificate of the IRC server was refused (invalid, expired, wrong name).</summary>
    public const string CertificateRefused = "CERTIFICATE_REFUSED";

    /// <summary>The address is a host of the simulated network, which carries no IRC yet.</summary>
    public const string SimulatedHost = "SIMULATED_HOST";

    /// <summary>The server could not be reached (name unknown, no answer, upgrade refused).</summary>
    public const string Unreachable = "UNREACHABLE";

    /// <summary>The connection was lost after it opened.</summary>
    public const string Lost = "LOST";

    /// <summary>This place cannot open the connection at all (for example a Web Worker).</summary>
    public const string NotSupported = "NOT_SUPPORTED";
}
