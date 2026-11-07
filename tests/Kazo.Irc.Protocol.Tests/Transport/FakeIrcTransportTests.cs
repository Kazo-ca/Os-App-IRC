using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Protocol.Tests.Transport;

public sealed class FakeIrcTransportTests
{
    [Fact]
    public async Task Writing_before_connecting_or_after_closing_is_refused()
    {
        await using var transport = new FakeIrcTransport();

        await Assert.ThrowsAsync<IOException>(() => transport.WriteLineAsync("NICK neo", CancellationToken.None));
        await transport.ConnectAsync(CancellationToken.None);
        await transport.WriteLineAsync("NICK neo", CancellationToken.None);
        await transport.CloseAsync(CancellationToken.None);

        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => transport.WriteLineAsync("NICK neo", CancellationToken.None));
        Assert.Equal(["NICK neo"], transport.SentLines);
    }

    [Fact]
    public async Task A_connect_failure_is_thrown()
    {
        await using var transport = new FakeIrcTransport { ConnectFailure = new IOException("refused") };

        await Assert.ThrowsAsync<IOException>(() => transport.ConnectAsync(CancellationToken.None));
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task Lines_from_the_server_are_read_in_order()
    {
        await using var transport = new FakeIrcTransport();
        await transport.ConnectAsync(CancellationToken.None);

        transport.ServerSend("one");
        transport.ServerSend("two");
        transport.ServerClose();

        Assert.Equal("one", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Equal("two", await transport.ReadLineAsync(CancellationToken.None));
        Assert.Null(await transport.ReadLineAsync(CancellationToken.None));
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task Waiting_for_a_line_never_sent_times_out()
    {
        await using var transport = new FakeIrcTransport();

        await Assert.ThrowsAsync<TimeoutException>(() => transport.WaitForSentAsync("X", TimeSpan.FromMilliseconds(20)));
    }
}
