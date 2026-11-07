using System.Net.WebSockets;
using System.Text;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Protocol.Tests.Transport;

/// <summary>IRC over WebSocket (IRCv3 <c>text.ircv3.net</c>): one message is one line.</summary>
public sealed class WebSocketIrcTransportTests
{
    private static async Task<(WebSocketIrcTransport Transport, FakeWebSocket Socket)> ConnectedAsync()
    {
        var socket = new FakeWebSocket();
        var transport = new WebSocketIrcTransport(_ => Task.FromResult<WebSocket>(socket));
        await transport.ConnectAsync(CancellationToken.None);
        return (transport, socket);
    }

    [Fact]
    public async Task A_line_is_sent_as_one_text_message_without_CR_LF()
    {
        var (transport, socket) = await ConnectedAsync();

        await transport.WriteLineAsync("NICK neo", CancellationToken.None);
        await transport.WriteLineAsync("PRIVMSG #kazo :héllo", CancellationToken.None);

        Assert.Equal([("NICK neo", WebSocketMessageType.Text), ("PRIVMSG #kazo :héllo", WebSocketMessageType.Text)], socket.Sent);
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Each_message_is_a_line_and_a_message_in_several_frames_is_put_together()
    {
        var (transport, socket) = await ConnectedAsync();

        socket.ServerSend(":srv 001 neo :Welcome");
        socket.ServerSend(":srv NOTICE neo :part one ", endOfMessage: false);
        socket.ServerSend("and two", endOfMessage: true);

        Assert.Equal(":srv 001 neo :Welcome", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Equal(":srv NOTICE neo :part one and two", await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task A_message_with_CR_LF_or_several_lines_gives_each_line()
    {
        var (transport, socket) = await ConnectedAsync();

        socket.ServerSend("PING :a\r\nPING :b\n\r\n");

        Assert.Equal("PING :a", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Equal("PING :b", await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task A_binary_message_in_Latin_1_is_read_with_the_fallback()
    {
        var (transport, socket) = await ConnectedAsync();

        socket.ServerSendBytes(Encoding.Latin1.GetBytes(":old PRIVMSG #kazo :café"), type: WebSocketMessageType.Binary);

        Assert.Equal(":old PRIVMSG #kazo :café", await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task A_message_too_long_is_dropped_and_the_next_one_is_read()
    {
        var (transport, socket) = await ConnectedAsync();

        socket.ServerSend(new string('x', WebSocketIrcTransport.MaxMessageBytes + 1));
        socket.ServerSend("PING :after");

        Assert.Equal("PING :after", await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task The_servers_close_ends_the_reading()
    {
        var (transport, socket) = await ConnectedAsync();

        socket.ServerClose();

        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Closing_sends_a_normal_close_then_writing_is_refused()
    {
        var (transport, socket) = await ConnectedAsync();

        await transport.CloseAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.SentCloseStatus);
        await Assert.ThrowsAsync<IOException>(() => transport.WriteLineAsync("QUIT", CancellationToken.None));
        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_aborts_and_disposes_the_socket()
    {
        var (transport, socket) = await ConnectedAsync();

        await transport.DisposeAsync();

        Assert.True(socket.Aborted);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task A_lost_connection_is_a_transport_error_that_can_be_retried()
    {
        var (transport, socket) = await ConnectedAsync();
        socket.ReceiveFailure = new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "gone");

        var failure = await Assert.ThrowsAsync<IrcTransportException>(() => transport.ReadLineAsync(CancellationToken.None).AsTask());

        Assert.Equal(IrcTransportErrors.Lost, failure.Code);
        Assert.False(failure.IsPermanent);
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task The_opening_function_runs_at_connect_and_its_failure_comes_out()
    {
        var calls = 0;
        var refused = new IrcTransportException(IrcTransportErrors.NoPermission, "no", isPermanent: true);
        await using var transport = new WebSocketIrcTransport(_ =>
        {
            calls++;
            throw refused;
        });

        Assert.Equal(0, calls);
        Assert.Same(refused, await Assert.ThrowsAsync<IrcTransportException>(() => transport.ConnectAsync(CancellationToken.None)));
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.WriteLineAsync("NICK neo", CancellationToken.None));
    }

    [Fact]
    public async Task A_transport_is_used_once()
    {
        var (transport, _) = await ConnectedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ConnectAsync(CancellationToken.None));
        await transport.DisposeAsync();
    }

    [Fact]
    public void A_transport_error_needs_a_code()
    {
        Assert.Throws<ArgumentException>(() => new IrcTransportException("", "x", isPermanent: false));
        var error = new IrcTransportException(IrcTransportErrors.Refused, "refused", isPermanent: false, new IOException("inner"));
        Assert.IsAssignableFrom<IOException>(error);
        Assert.Equal("inner", error.InnerException!.Message);
    }
}
