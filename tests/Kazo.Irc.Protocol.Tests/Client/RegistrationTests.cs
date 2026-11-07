using System.Text;
using Kazo.Irc.Protocol.Client;

namespace Kazo.Irc.Protocol.Tests.Client;

public sealed class RegistrationTests
{
    [Fact]
    public async Task Start_sends_cap_ls_pass_nick_and_user()
    {
        await using var harness = new ClientHarness(new IrcClientOptions
        {
            Nick = "neo",
            UserName = "thomas",
            RealName = "Thomas Anderson",
            ServerPassword = "secret",
        });

        await harness.StartAsync();

        Assert.Equal(["CAP LS 302", "PASS secret", "NICK neo", "USER thomas 0 * :Thomas Anderson"], harness.Transport.SentLines);
        Assert.Equal(IrcClientState.Registering, harness.Client.State);
    }

    [Fact]
    public async Task Start_twice_is_refused()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        Assert.Throws<InvalidOperationException>(harness.Client.Start);
    }

    [Fact]
    public async Task Cap_requests_only_wanted_capabilities_the_server_offers_across_lines()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(
            ":irc.kazo.ca CAP * LS * :multi-prefix extended-join sasl=PLAIN",
            ":irc.kazo.ca CAP * LS :server-time away-notify");

        var request = await harness.Transport.WaitForSentAsync("CAP REQ");
        Assert.Equal("CAP REQ :server-time away-notify multi-prefix", request);
        Assert.Equal("PLAIN", harness.Client.AvailableCapabilities["sasl"]);

        await harness.Transport.ServerSendAsync(":irc.kazo.ca CAP neo ACK :server-time away-notify multi-prefix");

        await harness.Transport.WaitForSentAsync("CAP END");
        Assert.True(harness.Client.HasCapability("server-time"));
        Assert.Equal(3, harness.Client.EnabledCapabilities.Count);
    }

    [Fact]
    public async Task Cap_nak_ends_the_negotiation()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv CAP * LS :multi-prefix", ":srv CAP * NAK :multi-prefix");

        await harness.Transport.WaitForSentAsync("CAP END");
        Assert.Empty(harness.Client.EnabledCapabilities);
    }

    [Fact]
    public async Task A_server_without_cap_registers_anyway()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv 421 neo CAP :Unknown command", ":srv 001 neo :Welcome");

        Assert.Equal(IrcClientState.Connected, harness.Client.State);
        Assert.DoesNotContain("CAP END", harness.Transport.SentLines);
        Assert.Single(harness.RegisteredTimes);
        Assert.Equal("srv", harness.Client.ServerName);
    }

    [Fact]
    public async Task Cap_new_and_del_update_the_capabilities_after_registration()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":srv CAP neo NEW :away-notify batch");
        await harness.Transport.WaitForSentAsync("CAP REQ away-notify");
        await harness.Transport.ServerSendAsync(":srv CAP neo ACK :away-notify");
        Assert.True(harness.Client.HasCapability("away-notify"));

        await harness.Transport.ServerSendAsync(":srv CAP neo DEL :away-notify", ":srv CAP neo ACK :-batch");
        Assert.False(harness.Client.HasCapability("away-notify"));
        Assert.False(harness.Client.AvailableCapabilities.ContainsKey("away-notify"));
    }

    [Fact]
    public async Task Sasl_plain_sends_the_credentials_then_ends_cap()
    {
        await using var harness = new ClientHarness(new IrcClientOptions
        {
            Nick = "neo",
            Sasl = new IrcSaslCredentials("neo", "zion"),
        });
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv CAP * LS :sasl=PLAIN,EXTERNAL");
        await harness.Transport.WaitForSentAsync("CAP REQ sasl");
        await harness.Transport.ServerSendAsync(":srv CAP neo ACK :sasl");
        await harness.Transport.WaitForSentAsync("AUTHENTICATE PLAIN");
        Assert.DoesNotContain("CAP END", harness.Transport.SentLines);

        await harness.Transport.ServerSendAsync("AUTHENTICATE +");
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("neo\0neo\0zion"));
        await harness.Transport.WaitForSentAsync("AUTHENTICATE " + expected);

        await harness.Transport.ServerSendAsync(
            ":srv 900 neo neo!~neo@kazo.ca neo :You are now logged in as neo",
            ":srv 903 neo :SASL authentication successful");

        await harness.Transport.WaitForSentAsync("CAP END");
        Assert.Equal("neo", harness.Client.Account);
        var result = Assert.Single(harness.Sasl);
        Assert.True(result.Success);
        Assert.Equal("903", result.Code);
    }

    [Fact]
    public async Task Sasl_failure_is_reported_and_registration_goes_on()
    {
        await using var harness = new ClientHarness(new IrcClientOptions { Nick = "neo", Sasl = new IrcSaslCredentials("neo", "bad") });
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv CAP * LS :sasl", ":srv CAP neo ACK :sasl", "AUTHENTICATE +",
            ":srv 904 neo :SASL authentication failed");

        await harness.Transport.WaitForSentAsync("CAP END");
        var result = Assert.Single(harness.Sasl);
        Assert.False(result.Success);
        Assert.Equal("904", result.Code);
        Assert.Null(harness.Client.Account);
    }

    [Fact]
    public async Task Sasl_is_not_asked_when_the_server_does_not_offer_plain()
    {
        await using var harness = new ClientHarness(new IrcClientOptions { Nick = "neo", Sasl = new IrcSaslCredentials("neo", "zion") });
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv CAP * LS :sasl=EXTERNAL");

        await harness.Transport.WaitForSentAsync("CAP END");
        Assert.DoesNotContain(harness.Transport.SentLines, l => l.StartsWith("CAP REQ", StringComparison.Ordinal));
        Assert.Equal("MECHANISM", Assert.Single(harness.Sasl).Code);
    }

    [Fact]
    public async Task Sasl_is_not_asked_without_credentials()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(":srv CAP * LS :sasl");

        await harness.Transport.WaitForSentAsync("CAP END");
        Assert.Empty(harness.Sasl);
    }

    [Fact]
    public void Sasl_payload_is_cut_in_pieces_of_400()
    {
        Assert.Equal(["abc"], IrcClient.SplitSaslPayload("abc"));
        Assert.Equal(["+"], IrcClient.SplitSaslPayload(""));
        var exact = new string('a', 400);
        Assert.Equal([exact, "+"], IrcClient.SplitSaslPayload(exact));
        var longer = new string('a', 401);
        Assert.Equal([exact, "a"], IrcClient.SplitSaslPayload(longer));
    }

    [Fact]
    public async Task A_taken_nick_tries_the_alternates_then_underscores_and_digits()
    {
        await using var harness = new ClientHarness(new IrcClientOptions { Nick = "neo", AlternateNicks = ["theone"] });
        await harness.StartAsync();
        var transport = harness.Transport;

        await transport.ServerSendAsync(":srv 433 * neo :Nickname is already in use");
        await transport.WaitForSentAsync("NICK theone");
        await transport.ServerSendAsync(":srv 433 * theone :Nickname is already in use");
        await transport.WaitForSentAsync("NICK neo_");
        await transport.ServerSendAsync(":srv 432 * neo_ :Erroneous", ":srv 436 * neo__ :Collision");
        await transport.WaitForSentAsync("NICK neo1");

        await transport.ServerSendAsync(":srv 001 neo1 :Welcome");
        Assert.Equal("neo1", harness.Client.CurrentNick);
    }

    [Fact]
    public async Task Every_nick_taken_stops_without_reconnecting()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();
        var transport = harness.Transport;
        string[] tried = ["neo", "neo_", "neo__", .. Enumerable.Range(1, 9).Select(i => "neo" + i)];

        foreach (var nick in tried)
        {
            await transport.ServerSendAsync($":srv 433 * {nick} :Nickname is already in use");
        }

        await transport.WaitForSentAsync("QUIT");
        transport.ServerClose();
        await harness.Client.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(harness.Errors, e => e.Code == "NICK_EXHAUSTED");
        Assert.Single(harness.Transports);
        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
    }

    [Fact]
    public async Task A_taken_nick_after_registration_is_an_error()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Client.ChangeNickAsync("trinity");
        await harness.Transport.ServerSendAsync(":srv 433 neo trinity :Nickname is already in use");

        Assert.Equal("neo", harness.Client.CurrentNick);
        Assert.Contains(harness.Errors, e => e.Code == "433" && e.Message.StartsWith("trinity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Welcome_joins_the_auto_join_channels_with_keys_first()
    {
        await using var harness = new ClientHarness(new IrcClientOptions
        {
            Nick = "neo",
            AutoJoin = [new IrcChannelJoin("#kazo"), new IrcChannelJoin("#secret", "key")],
        });

        await harness.RegisterAsync();

        await harness.Transport.WaitForSentAsync("JOIN #secret,#kazo key");
        Assert.Equal(IrcClientState.Connected, harness.Client.State);
        Assert.All(harness.Client.Channels, c => Assert.True(c.WantsJoin));
    }

    [Fact]
    public async Task Ping_is_answered_with_pong()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync("PING :irc.kazo.ca", ":srv PONG srv :x");

        await harness.Transport.WaitForSentAsync("PONG irc.kazo.ca");
    }

    [Fact]
    public async Task Isupport_is_read_and_a_new_casemapping_refolds_names()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync("CHANTYPES=# NETWORK=KazoNet");
        await harness.JoinAsync("#Kazo[1]", "@neo Trin[ity]");

        Assert.NotNull(harness.Client.GetChannel("#kazo{1}"));
        await harness.Transport.ServerSendAsync(":srv 005 neo CASEMAPPING=ascii :are supported");

        Assert.Equal("KazoNet", harness.Client.ServerSupport.NetworkName);
        Assert.Null(harness.Client.GetChannel("#kazo{1}"));
        Assert.NotNull(harness.Client.GetChannel("#kazo[1]"));
        Assert.NotNull(harness.Client.GetUser("trin[ity]"));
        Assert.True(harness.Client.GetChannel("#KAZO[1]")!.HasUser("TRIN[ITY]"));
    }
    [Fact]
    public async Task Many_channels_are_joined_in_several_lines_under_the_limit()
    {
        var channels = Enumerable.Range(0, 60).Select(i => new IrcChannelJoin($"#channel-number-{i:D2}")).ToList();
        await using var harness = new ClientHarness(new IrcClientOptions { Nick = "neo", AutoJoin = channels });

        await harness.RegisterAsync();

        var joins = harness.Transport.SentLines.Where(l => l.StartsWith("JOIN ", StringComparison.Ordinal)).ToList();
        Assert.True(joins.Count > 1);
        Assert.All(joins, j => Assert.True(j.Length + 2 <= 512));
        Assert.Equal(60, joins.Sum(j => j[5..].Split(',').Length));
    }

    [Fact]
    public void Sasl_credentials_never_show_the_password()
    {
        Assert.DoesNotContain("zion", new IrcSaslCredentials("neo", "zion").ToString(), StringComparison.Ordinal);
    }
}
