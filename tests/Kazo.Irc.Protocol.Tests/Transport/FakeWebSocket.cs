using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Kazo.Irc.Protocol.Tests.Transport;

/// <summary>
/// A WebSocket in memory, in place of Kazo OS's <c>KazoClientWebSocket</c>: the frames the server
/// sends are queued, the messages the client sends are kept.
/// </summary>
internal sealed class FakeWebSocket : WebSocket
{
    private readonly Channel<Frame> _incoming = Channel.CreateUnbounded<Frame>();
    private Frame? _current;
    private int _offset;
    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;

    /// <summary>The text of each message the client sent, with its type.</summary>
    public List<(string Text, WebSocketMessageType Type)> Sent { get; } = [];

    /// <summary>The status of the client's close, or null when it did not close.</summary>
    public WebSocketCloseStatus? SentCloseStatus { get; private set; }

    public bool Aborted { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>When set, the next receive throws it.</summary>
    public Exception? ReceiveFailure { get; set; }

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;

    public override string? CloseStatusDescription => null;

    public override WebSocketState State => _state;

    public override string? SubProtocol => "text.ircv3.net";

    /// <summary>The server sends a text frame.</summary>
    public void ServerSend(string text, bool endOfMessage = true) =>
        ServerSendBytes(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage);

    /// <summary>The server sends a frame of raw bytes.</summary>
    public void ServerSendBytes(byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Text, bool endOfMessage = true) =>
        _incoming.Writer.TryWrite(new Frame(bytes, type, endOfMessage));

    /// <summary>The server closes the connection normally.</summary>
    public void ServerClose() => _incoming.Writer.TryWrite(new Frame([], WebSocketMessageType.Close, EndOfMessage: true));

    public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (ReceiveFailure is { } failure)
        {
            throw failure;
        }

        if (_current is null || _offset == _current.Bytes.Length && _current.Type != WebSocketMessageType.Close)
        {
            try
            {
                _current = await _incoming.Reader.ReadAsync(cancellationToken);
                _offset = 0;
            }
            catch (ChannelClosedException)
            {
                throw new WebSocketException(WebSocketError.InvalidState, "aborted");
            }
        }

        var frame = _current;
        if (frame.Type == WebSocketMessageType.Close)
        {
            _closeStatus = WebSocketCloseStatus.NormalClosure;
            _state = _state == WebSocketState.CloseSent ? WebSocketState.Closed : WebSocketState.CloseReceived;
            _current = null;
            return new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true);
        }

        var count = Math.Min(buffer.Length, frame.Bytes.Length - _offset);
        frame.Bytes.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        var last = _offset == frame.Bytes.Length;
        if (last)
        {
            _current = null;
        }

        return new ValueWebSocketReceiveResult(count, frame.Type, last && frame.EndOfMessage);
    }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The transport reads into a Memory<byte>.");

    public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        Sent.Add((Encoding.UTF8.GetString(buffer.Span), messageType));
        return ValueTask.CompletedTask;
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        SendAsync(buffer.AsMemory(), messageType, endOfMessage, cancellationToken).AsTask();

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        SentCloseStatus = closeStatus;
        _state = _state == WebSocketState.CloseReceived ? WebSocketState.Closed : WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        SentCloseStatus = closeStatus;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override void Abort()
    {
        Aborted = true;
        _state = WebSocketState.Aborted;
        _incoming.Writer.TryComplete();
    }

    public override void Dispose()
    {
        Disposed = true;
        _incoming.Writer.TryComplete();
    }

    private sealed record Frame(byte[] Bytes, WebSocketMessageType Type, bool EndOfMessage);
}
