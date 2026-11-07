using System.Threading.Channels;

namespace Kazo.Irc.Protocol.Transport;

/// <summary>
/// A transport with no network: the test (or a fake server) plays the server side by hand.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServerSendAsync"/> returns once the client has handled the line (it waits until the
/// client asks for the next one), so a test can check the model right after it with no delay.
/// <see cref="WaitForSentAsync(Func{string, bool}, TimeSpan?)"/> waits for a line the client sends.
/// </para>
/// <para>It is thread-safe: the client reads on its own loop while the test writes.</para>
/// </remarks>
public sealed class FakeIrcTransport : IIrcTransport
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly List<string> _sent = [];
    private readonly List<(Func<string, bool> Match, TaskCompletionSource<string> Done)> _sentWaiters = [];
    private readonly List<(long Reads, TaskCompletionSource Done)> _readWaiters = [];
    private readonly object _gate = new();
    private long _enqueued;
    private long _readCalls;
    private bool _closed;

    /// <summary>When set, <see cref="ConnectAsync"/> throws it: the server cannot be reached.</summary>
    public Exception? ConnectFailure { get; set; }

    /// <summary>True between a successful <see cref="ConnectAsync"/> and the end of the connection.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>True once <see cref="CloseAsync"/> or <see cref="DisposeAsync"/> was called by the client.</summary>
    public bool ClosedByClient { get; private set; }

    /// <summary>The lines the client sent, in order.</summary>
    public IReadOnlyList<string> SentLines
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ConnectFailure is not null)
        {
            throw ConnectFailure;
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _readCalls++;
            CompleteReadWaiters();
        }

        try
        {
            var line = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                MarkClosed();
            }

            return line;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            if (!IsConnected)
            {
                throw new IOException("The fake IRC connection is closed.");
            }

            _sent.Add(line);
            for (var i = _sentWaiters.Count - 1; i >= 0; i--)
            {
                if (_sentWaiters[i].Match(line))
                {
                    _sentWaiters[i].Done.TrySetResult(line);
                    _sentWaiters.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        ClosedByClient = true;
        _incoming.Writer.TryWrite(null);
        MarkClosed();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        ClosedByClient = true;
        _incoming.Writer.TryComplete();
        MarkClosed();
        return ValueTask.CompletedTask;
    }

    /// <summary>Queues a line from the server without waiting for the client.</summary>
    public void ServerSend(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            _enqueued++;
        }

        _incoming.Writer.TryWrite(line);
    }

    /// <summary>Sends lines from the server and returns once the client has handled all of them.</summary>
    /// <exception cref="TimeoutException">The client did not handle them within 10 seconds.</exception>
    public Task ServerSendAsync(params string[] lines)
    {
        long target;
        lock (_gate)
        {
            foreach (var line in lines)
            {
                _enqueued++;
                _incoming.Writer.TryWrite(line);
            }

            // Read call n returns line n; read call n + 1 starts once line n has been handled.
            target = _enqueued + 1;
            if (_readCalls >= target || _closed)
            {
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _readWaiters.Add((target, done));
            return done.Task.WaitAsync(DefaultTimeout);
        }
    }

    /// <summary>The server closes the connection: the client's pending read returns null.</summary>
    public void ServerClose()
    {
        lock (_gate)
        {
            _enqueued++;
        }

        _incoming.Writer.TryWrite(null);
    }

    /// <summary>Waits until the client sends a line that <paramref name="match"/> accepts (one already sent counts).</summary>
    /// <exception cref="TimeoutException">No such line within the timeout (10 seconds by default).</exception>
    public Task<string> WaitForSentAsync(Func<string, bool> match, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        lock (_gate)
        {
            var existing = _sent.FirstOrDefault(match);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sentWaiters.Add((match, done));
            return done.Task.WaitAsync(timeout ?? DefaultTimeout);
        }
    }

    /// <summary>Waits until the client sends a line that starts with <paramref name="prefix"/>.</summary>
    public Task<string> WaitForSentAsync(string prefix, TimeSpan? timeout = null) =>
        WaitForSentAsync(line => line.StartsWith(prefix, StringComparison.Ordinal), timeout);

    /// <summary>Forgets the lines sent so far, so the next waits only see new ones.</summary>
    public void ClearSent()
    {
        lock (_gate)
        {
            _sent.Clear();
        }
    }

    private void MarkClosed()
    {
        lock (_gate)
        {
            IsConnected = false;
            _closed = true;
            foreach (var (_, done) in _readWaiters)
            {
                done.TrySetResult();
            }

            _readWaiters.Clear();
        }
    }

    private void CompleteReadWaiters()
    {
        for (var i = _readWaiters.Count - 1; i >= 0; i--)
        {
            if (_readCalls >= _readWaiters[i].Reads)
            {
                _readWaiters[i].Done.TrySetResult();
                _readWaiters.RemoveAt(i);
            }
        }
    }
}
