using System.Text;
using System.Threading.Channels;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Protocol.Tests.Transport;

/// <summary>IRC over a byte stream (the Kazo server's relay): lines end with CR LF.</summary>
public sealed class StreamIrcTransportTests
{
    private static async Task<(StreamIrcTransport Transport, FakeStream Stream)> ConnectedAsync()
    {
        var stream = new FakeStream();
        var transport = new StreamIrcTransport(_ => Task.FromResult<Stream>(stream));
        await transport.ConnectAsync(CancellationToken.None);
        return (transport, stream);
    }

    [Fact]
    public async Task A_line_is_written_with_CR_LF_in_UTF_8()
    {
        var (transport, stream) = await ConnectedAsync();

        await transport.WriteLineAsync("PRIVMSG #kazo :héllo", CancellationToken.None);

        Assert.Equal("PRIVMSG #kazo :héllo\r\n", Encoding.UTF8.GetString(stream.Written.ToArray()));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Lines_cut_anywhere_by_the_network_are_put_together()
    {
        var (transport, stream) = await ConnectedAsync();

        stream.ServerSend(":srv 001 neo :Wel");
        stream.ServerSend("come\r\nPING :a\r");
        stream.ServerSend("\n\r\nPING :b\n");

        Assert.Equal(":srv 001 neo :Welcome", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Equal("PING :a", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Equal("PING :b", await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task The_end_of_the_stream_ends_the_reading()
    {
        var (transport, stream) = await ConnectedAsync();

        stream.ServerSend("PING :a\r\nunfinished");
        stream.ServerClose();

        Assert.Equal("PING :a", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => transport.WriteLineAsync("QUIT", CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Closing_disposes_the_stream_and_ends_a_pending_read()
    {
        var (transport, stream) = await ConnectedAsync();
        var read = transport.ReadLineAsync(CancellationToken.None).AsTask();

        await transport.CloseAsync(CancellationToken.None);

        Assert.True(stream.Disposed);
        Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(5)));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task A_broken_stream_is_a_lost_connection()
    {
        var (transport, stream) = await ConnectedAsync();
        stream.Failure = new IOException("reset");

        var reading = await Assert.ThrowsAsync<IrcTransportException>(() => transport.ReadLineAsync(CancellationToken.None).AsTask());
        var writing = await Assert.ThrowsAsync<IrcTransportException>(() => transport.WriteLineAsync("PING x", CancellationToken.None));

        Assert.Equal(IrcTransportErrors.Lost, reading.Code);
        Assert.Equal(IrcTransportErrors.Lost, writing.Code);
        Assert.False(reading.IsPermanent);
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task It_must_be_connected_and_is_used_once()
    {
        await using var fresh = new StreamIrcTransport(_ => Task.FromResult<Stream>(new FakeStream()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.ReadLineAsync(CancellationToken.None).AsTask());

        var (transport, _) = await ConnectedAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ConnectAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    /// <summary>A network stream in memory: what the server sends is queued; what the client writes is kept.</summary>
    private sealed class FakeStream : Stream
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private byte[] _current = [];
        private int _offset;

        public MemoryStream Written { get; } = new();

        public bool Disposed { get; private set; }

        public Exception? Failure { get; set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public void ServerSend(string text) => _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

        public void ServerClose() => _incoming.Writer.TryComplete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            if (_offset == _current.Length)
            {
                try
                {
                    _current = await _incoming.Reader.ReadAsync(cancellationToken);
                    _offset = 0;
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }
            }

            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Failure is { } failure)
            {
                throw failure;
            }

            Written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
