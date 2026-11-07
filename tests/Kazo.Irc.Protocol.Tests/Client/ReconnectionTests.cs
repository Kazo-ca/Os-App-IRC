using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Protocol.Tests.Client;

public sealed class ReconnectionTests
{
    [Fact]
    public void Policy_delay_grows_and_is_capped()
    {
        var policy = new IrcReconnectPolicy { InitialDelay = TimeSpan.FromSeconds(2), MaxDelay = TimeSpan.FromSeconds(30) };

        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(16), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(500));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(0));
        Assert.False(IrcReconnectPolicy.Disabled.Enabled);
    }

    [Fact]
    public async Task A_lost_connection_reconnects_and_rejoins_the_channels()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo trinity");
        await harness.Client.JoinAsync("#secret", "key");
        await harness.JoinAsync("#secret");
        await harness.JoinAsync("#left");
        await harness.Client.PartAsync("#left");
        var first = harness.Transport;

        first.ServerClose();
        await ClientHarness.WaitUntilAsync(() => harness.Reconnects.Count == 1);

        var kazo = harness.Client.GetChannel("#kazo")!;
        Assert.False(kazo.IsJoined);
        Assert.Equal(0, kazo.UserCount);
        Assert.Null(harness.Client.GetUser("trinity"));
        Assert.Equal(IrcClientState.Reconnecting, harness.Client.State);
        Assert.Equal(new IrcReconnectInfo(1, TimeSpan.FromSeconds(2)), harness.Reconnects[0]);

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await ClientHarness.WaitUntilAsync(() => harness.Transports.Count == 2);
        var second = harness.Transport;
        await second.WaitForSentAsync("USER ");
        await second.ServerSendAsync(":srv CAP * LS :", ":srv 001 neo :Welcome back");

        await second.WaitForSentAsync(l => l == "JOIN #secret,#kazo key");
        Assert.DoesNotContain(second.SentLines, l => l.Contains("#left", StringComparison.Ordinal));
        Assert.Equal(2, harness.RegisteredTimes.Count);
    }

    [Fact]
    public async Task Failed_attempts_wait_longer_each_time_then_give_up()
    {
        var options = new IrcClientOptions
        {
            Nick = "neo",
            Reconnect = new IrcReconnectPolicy { InitialDelay = TimeSpan.FromSeconds(1), MaxAttempts = 3 },
        };
        await using var harness = new ClientHarness(options, (transport, index) =>
        {
            if (index > 0)
            {
                transport.ConnectFailure = new IOException("Connection refused");
            }
        });
        await harness.RegisterAsync();

        harness.Transport.ServerClose();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await ClientHarness.WaitUntilAsync(() => harness.Reconnects.Count == attempt);
            harness.Time.Advance(harness.Reconnects[^1].Delay);
        }

        await harness.Client.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([1, 2, 4], harness.Reconnects.Select(r => (int)r.Delay.TotalSeconds));
        Assert.Equal(3, harness.Errors.Count(e => e.Code == "TRANSPORT" && e.Message == "Connection refused"));
        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
        Assert.Contains(IrcClientState.Connecting, harness.States);
    }

    [Fact]
    public async Task A_permanent_transport_failure_is_reported_with_its_code_and_not_retried()
    {
        await using var harness = new ClientHarness(prepareTransport: (transport, _) =>
            transport.ConnectFailure = new IrcTransportException(IrcTransportErrors.NoServer, "no Kazo server", isPermanent: true));

        harness.Client.Start();
        await harness.Client.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        var error = Assert.Single(harness.Errors);
        Assert.Equal(IrcTransportErrors.NoServer, error.Code);
        Assert.Empty(harness.Reconnects);
        Assert.Single(harness.Transports);
        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
    }

    [Fact]
    public async Task A_transport_failure_that_can_pass_is_retried_with_its_code()
    {
        await using var harness = new ClientHarness(prepareTransport: (transport, index) =>
        {
            if (index == 0)
            {
                transport.ConnectFailure = new IrcTransportException(IrcTransportErrors.Unreachable, "unreachable", isPermanent: false);
            }
        });

        harness.Client.Start();
        await ClientHarness.WaitUntilAsync(() => harness.Reconnects.Count == 1);
        harness.Time.Advance(harness.Reconnects[0].Delay);
        await ClientHarness.WaitUntilAsync(() => harness.Transports.Count == 2);
        await harness.Transport.WaitForSentAsync("USER ");

        Assert.Equal(IrcTransportErrors.Unreachable, Assert.Single(harness.Errors).Code);
    }

    [Fact]
    public async Task A_disabled_policy_does_not_reconnect()
    {
        await using var harness = new ClientHarness(new IrcClientOptions { Nick = "neo", Reconnect = IrcReconnectPolicy.Disabled });
        await harness.RegisterAsync();

        harness.Transport.ServerClose();
        await harness.Client.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(harness.Reconnects);
        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task Dispose_while_waiting_stops_at_once()
    {
        var harness = new ClientHarness();
        await harness.RegisterAsync();
        harness.Transport.ServerClose();
        await ClientHarness.WaitUntilAsync(() => harness.Reconnects.Count == 1);

        await harness.DisposeAsync();

        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
        Assert.Single(harness.Transports);
    }

    [Fact]
    public async Task The_client_can_start_again_after_a_quit()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        var quit = harness.Client.QuitAsync();
        harness.Transport.ServerClose();
        await quit;

        await harness.StartAsync();

        Assert.Equal(2, harness.Transports.Count);
        Assert.Equal(IrcClientState.Registering, harness.Client.State);
    }
}

public sealed class KeepAliveTests
{
    [Fact]
    public async Task Silence_sends_a_ping_then_a_timeout_reconnects()
    {
        await using var harness = new ClientHarness(new IrcClientOptions
        {
            Nick = "neo",
            PingInterval = TimeSpan.FromSeconds(90),
            PingTimeout = TimeSpan.FromSeconds(60),
        });
        await harness.RegisterAsync();
        var first = harness.Transport;

        harness.Time.Advance(TimeSpan.FromSeconds(90));
        await first.WaitForSentAsync("PING kazo-");

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await ClientHarness.WaitUntilAsync(() => harness.Reconnects.Count == 1);
        Assert.Contains(harness.Errors, e => e.Code == "PING_TIMEOUT");
        Assert.True(first.ClosedByClient);
    }

    [Fact]
    public async Task A_line_from_the_server_resets_the_silence()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        var transport = harness.Transport;

        harness.Time.Advance(TimeSpan.FromSeconds(90));
        await transport.WaitForSentAsync("PING kazo-");
        await transport.ServerSendAsync(":srv PONG srv :kazo");
        harness.Time.Advance(TimeSpan.FromSeconds(80));

        Assert.Empty(harness.Reconnects);
        Assert.Equal(IrcClientState.Connected, harness.Client.State);
        Assert.Single(transport.SentLines, l => l.StartsWith("PING", StringComparison.Ordinal));
    }
}
