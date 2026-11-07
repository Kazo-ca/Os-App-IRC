using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Transport;

/// <summary>
/// IRC over a byte stream (TCP, or the Kazo server's relay with TLS opened by the relay): lines end
/// with CR LF, and <see cref="IrcLineBuffer"/> cuts what arrives into lines.
/// </summary>
/// <remarks>
/// The app gives a function that opens the stream (Kazo OS's <c>KazoTcpClient.ConnectAsync</c>,
/// which returns a <see cref="Stream"/>) and maps the network API's errors to
/// <see cref="IrcTransportException"/>; the tests give a pipe.
/// </remarks>
public sealed class StreamIrcTransport : IIrcTransport
{
    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly IrcLineBuffer _lines = new();
    private readonly Queue<string> _pending = new();
    private readonly byte[] _buffer = new byte[4096];
    private Stream? _stream;
    private bool _closed;

    /// <summary>Creates the transport; nothing is opened before <see cref="ConnectAsync"/>.</summary>
    /// <param name="open">Opens the stream (and throws an <see cref="IrcTransportException"/> when it cannot).</param>
    public StreamIrcTransport(Func<CancellationToken, Task<Stream>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            throw new InvalidOperationException("A transport is used once.");
        }

        _stream = await _open(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("The transport is not connected.");
        while (_pending.Count == 0)
        {
            if (_closed)
            {
                return null;
            }

            int read;
            try
            {
                read = await stream.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException && exception is not IrcTransportException)
            {
                if (_closed)
                {
                    return null;
                }

                throw new IrcTransportException(IrcTransportErrors.Lost, exception.Message, isPermanent: false, exception);
            }

            if (read == 0)
            {
                // The server closed: a last line without CR LF is lost, as with a real socket.
                _closed = true;
                return null;
            }

            foreach (var line in _lines.Append(_buffer.AsSpan(0, read)))
            {
                if (line.Length > 0)
                {
                    _pending.Enqueue(line);
                }
            }
        }

        return _pending.Dequeue();
    }

    /// <inheritdoc />
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        var stream = _stream ?? throw new InvalidOperationException("The transport is not connected.");
        if (_closed)
        {
            throw new IOException("The connection is closed.");
        }

        try
        {
            await stream.WriteAsync(IrcEncoding.Encode(line + "\r\n"), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException && exception is not IrcTransportException)
        {
            throw new IrcTransportException(IrcTransportErrors.Lost, exception.Message, isPermanent: false, exception);
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_stream is not { } stream || _closed)
        {
            return;
        }

        // A TCP stream has no half close here: closing it ends a pending read.
        _closed = true;
        await stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _closed = true;
        if (_stream is { } stream)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
