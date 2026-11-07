using System.Net.WebSockets;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Transport;

/// <summary>
/// IRC over WebSocket (IRCv3 WebSocket, <c>text.ircv3.net</c> or <c>binary.ircv3.net</c>): one
/// message is one line, without its CR LF.
/// </summary>
/// <remarks>
/// <para>
/// The transport works on any <see cref="WebSocket"/>: the app gives a function that opens one
/// (Kazo OS's <c>KazoClientWebSocket</c>, which inherits <see cref="WebSocket"/>); the tests give a
/// fake. The function maps the network API's errors to <see cref="IrcTransportException"/>.
/// </para>
/// <para>
/// Reading: a message may come in several frames, and is decoded as UTF-8 with the Latin-1
/// fallback of <see cref="IrcEncoding"/> (a binary message, or text from an old server). A message
/// that holds several lines (a server that frames like a stream) gives each line. A message larger
/// than <see cref="MaxMessageBytes"/> is dropped, as a server would drop a line too long. Writing:
/// one text message per line, UTF-8.
/// </para>
/// </remarks>
public sealed class WebSocketIrcTransport : IIrcTransport
{
    /// <summary>The sub-protocol of IRC lines as WebSocket text messages (IRCv3).</summary>
    public const string TextSubProtocol = "text.ircv3.net";

    /// <summary>The largest message kept: the IRCv3 tags (8191 bytes) and the line (512 bytes), with room to spare.</summary>
    public const int MaxMessageBytes = 16 * 1024;

    private readonly Func<CancellationToken, Task<WebSocket>> _open;
    private readonly Queue<string> _pending = new();
    private readonly byte[] _buffer = new byte[4096];
    private WebSocket? _socket;
    private bool _closed;

    /// <summary>Creates the transport; nothing is opened before <see cref="ConnectAsync"/>.</summary>
    /// <param name="open">Opens the WebSocket (and throws an <see cref="IrcTransportException"/> when it cannot).</param>
    public WebSocketIrcTransport(Func<CancellationToken, Task<WebSocket>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_socket is not null)
        {
            throw new InvalidOperationException("A transport is used once.");
        }

        _socket = await _open(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("The transport is not connected.");
        while (_pending.Count == 0)
        {
            if (_closed || socket.State is not (WebSocketState.Open or WebSocketState.CloseSent))
            {
                return null;
            }

            var message = await ReceiveMessageAsync(socket, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return null;
            }

            foreach (var line in SplitLines(message))
            {
                _pending.Enqueue(line);
            }
        }

        return _pending.Dequeue();
    }

    /// <inheritdoc />
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var socket = _socket ?? throw new InvalidOperationException("The transport is not connected.");
        if (_closed || socket.State != WebSocketState.Open)
        {
            throw new IOException("The WebSocket is closed.");
        }

        try
        {
            await socket.SendAsync(IrcEncoding.Encode(line), WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebSocketException exception)
        {
            throw new IrcTransportException(IrcTransportErrors.Lost, exception.Message, isPermanent: false, exception);
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket is not { } socket || _closed)
        {
            return;
        }

        _closed = true;
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                // Only our half: a pending read gets the server's close (or the abort of DisposeAsync).
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Already gone.
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _closed = true;
        if (_socket is { } socket)
        {
            if (socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
            {
                socket.Abort();
            }

            socket.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Cuts a message into lines: CR LF, LF or CR end a line; empty lines are dropped.</summary>
    internal static IEnumerable<string> SplitLines(string message) =>
        message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Reads one whole message; null when the server closed.</summary>
    private async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var tooLong = false;
        while (true)
        {
            ValueWebSocketReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException exception)
            {
                if (_closed)
                {
                    return null;
                }

                throw new IrcTransportException(IrcTransportErrors.Lost, exception.Message, isPermanent: false, exception);
            }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                _closed = true;
                return null;
            }

            if (!tooLong && message.Length + received.Count <= MaxMessageBytes)
            {
                message.Write(_buffer, 0, received.Count);
            }
            else
            {
                tooLong = true;
            }

            if (received.EndOfMessage)
            {
                // A message too long is dropped whole; an empty string has no line.
                return tooLong ? string.Empty : IrcEncoding.Decode(message.GetBuffer().AsSpan(0, (int)message.Length));
            }
        }
    }
}
