namespace Kazo.Irc.Protocol.Transport;

/// <summary>
/// One connection to an IRC server, seen as lines of text in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The client (<see cref="Client.IrcClient"/>) asks its factory for a new transport at every
/// connection attempt and disposes it when the connection ends: a transport is used once.
/// </para>
/// <para>
/// Implementations (chantier 3): WebSocket with the <c>text.ircv3.net</c> sub-protocol (one frame
/// is one line, no CR LF); the relay's TCP stream (lines end with CR LF, cut by
/// <see cref="Text.IrcLineBuffer"/>); and <see cref="FakeIrcTransport"/> for tests.
/// </para>
/// </remarks>
public interface IIrcTransport : IAsyncDisposable
{
    /// <summary>Opens the connection.</summary>
    /// <exception cref="IOException">The server cannot be reached.</exception>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the next line from the server, without its CR LF; returns null when the
    /// connection is closed.
    /// </summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>Sends one line (without CR LF: the transport adds its own framing).</summary>
    /// <exception cref="IOException">The connection is closed.</exception>
    Task WriteLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>Closes the connection; a pending <see cref="ReadLineAsync"/> then returns null.</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}
