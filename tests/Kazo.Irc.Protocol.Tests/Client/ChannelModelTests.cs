using Kazo.Irc.Protocol.Client;

namespace Kazo.Irc.Protocol.Tests.Client;

public sealed class ChannelModelTests
{
    [Fact]
    public async Task Own_join_and_names_fill_the_channel_with_prefixes()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync("PREFIX=(qaohv)~&@%+");

        await harness.JoinAsync("#kazo", "~@morpheus @neo %trinity +tank dozer");

        var channel = harness.Client.GetChannel("#KAZO")!;
        Assert.True(channel.IsJoined);
        Assert.True(channel.WantsJoin);
        Assert.Equal(["morpheus", "neo", "trinity", "tank", "dozer"], channel.Users.Select(u => u.Nick));
        var morpheus = channel.GetUser("Morpheus")!;
        Assert.Equal("qo", morpheus.Modes);
        Assert.Equal("~@", morpheus.Prefixes);
        Assert.Equal('~', morpheus.HighestPrefix);
        Assert.Equal("~morpheus", morpheus.ToString());
        Assert.Null(channel.GetUser("dozer")!.HighestPrefix);
        Assert.Equal(5, channel.UserCount);
        var join = Assert.Single(harness.Activities);
        Assert.Equal(IrcActivityKind.Join, join.Kind);
        Assert.True(join.IsOwn);
    }

    [Fact]
    public async Task A_handled_line_is_announced_once_the_model_has_it()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        var usersAtEndOfNames = -1;
        harness.Client.MessageHandled += (_, message) =>
        {
            if (message.Command == "366")
            {
                usersAtEndOfNames = harness.Client.GetChannel("#kazo")!.UserCount;
            }
        };

        await harness.JoinAsync("#kazo", "@neo trinity morpheus");

        Assert.Equal(3, usersAtEndOfNames);
    }

    [Fact]
    public async Task Names_with_userhost_record_the_host()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.JoinAsync("#kazo", "@neo!~neo@kazo.ca trinity!~t@zion.net");

        var trinity = harness.Client.GetUser("trinity")!;
        Assert.Equal("~t", trinity.UserName);
        Assert.Equal("zion.net", trinity.Host);
    }

    [Fact]
    public async Task Others_join_part_kick_and_quit()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo");
        await harness.JoinAsync("#zion", "@neo");
        var transport = harness.Transport;

        await transport.ServerSendAsync(
            ":trinity!~t@zion.net JOIN #kazo",
            ":trinity!~t@zion.net JOIN #zion",
            ":tank!~k@zion.net JOIN #kazo",
            ":smith!~s@matrix.net JOIN #kazo");
        var channel = harness.Client.GetChannel("#kazo")!;
        Assert.Equal(4, channel.UserCount);
        Assert.Equal("zion.net", harness.Client.GetUser("trinity")!.Host);

        await transport.ServerSendAsync(":tank!~k@zion.net PART #kazo :brb");
        Assert.False(channel.HasUser("tank"));
        Assert.Null(harness.Client.GetUser("tank"));

        await transport.ServerSendAsync(":neo!~neo@kazo.ca KICK #kazo smith :Get out");
        Assert.False(channel.HasUser("smith"));
        var kick = harness.Activities[^1];
        Assert.Equal(IrcActivityKind.Kick, kick.Kind);
        Assert.Equal("neo", kick.Nick);
        Assert.Equal("smith", kick.TargetNick);
        Assert.Equal("Get out", kick.Detail);

        await transport.ServerSendAsync(":trinity!~t@zion.net QUIT :Quit: bye");
        Assert.False(channel.HasUser("trinity"));
        Assert.False(harness.Client.GetChannel("#zion")!.HasUser("trinity"));
        Assert.Null(harness.Client.GetUser("trinity"));
        var quits = harness.Activities.Where(a => a.Kind == IrcActivityKind.Quit).ToList();
        Assert.Equal(["#kazo", "#zion"], quits.Select(q => q.Conversation).Order());
        Assert.All(quits, q => Assert.Equal("Quit: bye", q.Detail));
    }

    [Fact]
    public async Task Own_part_and_kick_leave_the_channel_for_good()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo trinity");
        await harness.JoinAsync("#zion", "@neo");

        await harness.Transport.ServerSendAsync(":neo!~neo@kazo.ca PART #kazo");
        await harness.Transport.ServerSendAsync(":smith!s@m KICK #zion neo :bye");

        var kazo = harness.Client.GetChannel("#kazo")!;
        Assert.False(kazo.IsJoined);
        Assert.False(kazo.WantsJoin);
        Assert.Equal(0, kazo.UserCount);
        Assert.False(harness.Client.GetChannel("#zion")!.WantsJoin);
        Assert.True(harness.Activities[^1].IsOwn);
    }

    [Fact]
    public async Task Part_of_several_channels_at_once()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#a", "@neo trinity");
        await harness.JoinAsync("#b", "@neo trinity");

        await harness.Transport.ServerSendAsync(":trinity!t@z PART #a,#b,#unknown");

        Assert.False(harness.Client.GetChannel("#a")!.HasUser("trinity"));
        Assert.False(harness.Client.GetChannel("#b")!.HasUser("trinity"));
        Assert.Equal(2, harness.Activities.Count(a => a.Kind == IrcActivityKind.Part));
    }

    [Fact]
    public async Task Nick_changes_rename_everywhere()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo +trinity");
        harness.Client.OpenQuery("trinity");

        await harness.Transport.ServerSendAsync(":trinity!t@z NICK :trin");
        await harness.Transport.ServerSendAsync(":neo!~neo@kazo.ca NICK theone");

        var channel = harness.Client.GetChannel("#kazo")!;
        Assert.Equal("+", channel.GetUser("trin")!.Prefixes);
        Assert.False(channel.HasUser("trinity"));
        Assert.Equal(["trin"], harness.Client.Queries);
        Assert.Equal("theone", harness.Client.CurrentNick);
        Assert.True(harness.Client.IsMe("THEONE"));
        Assert.NotNull(channel.GetUser("theone"));
        var nicks = harness.Activities.Where(a => a.Kind == IrcActivityKind.Nick).ToList();
        Assert.Contains(nicks, a => a.Conversation == "trin" && a.Detail == "trin");
        Assert.Contains(nicks, a => a.Conversation == "#kazo" && a.Nick == "neo" && a.IsOwn);
    }

    [Fact]
    public async Task Topic_numerics_and_changes()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo");
        var transport = harness.Transport;

        await transport.ServerSendAsync(
            ":srv 332 neo #kazo :Welcome to Kazo OS",
            ":srv 333 neo #kazo morpheus!m@zion 1762488000");
        var channel = harness.Client.GetChannel("#kazo")!;
        Assert.Equal("Welcome to Kazo OS", channel.Topic);
        Assert.Equal("morpheus", channel.TopicSetBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762488000), channel.TopicSetAt);

        await transport.ServerSendAsync("@time=2026-11-07T05:00:00.000Z :trinity!t@z TOPIC #kazo :New topic");
        Assert.Equal("New topic", channel.Topic);
        Assert.Equal("trinity", channel.TopicSetBy);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 5, 0, 0, TimeSpan.Zero), channel.TopicSetAt);
        Assert.Equal(IrcActivityKind.Topic, harness.Activities[^1].Kind);

        await transport.ServerSendAsync(":trinity!t@z TOPIC #kazo :");
        Assert.Null(channel.Topic);

        await transport.ServerSendAsync(":srv 332 neo #kazo :Back", ":srv 331 neo #kazo :No topic is set");
        Assert.Null(channel.Topic);
    }

    [Fact]
    public async Task Mode_changes_user_prefixes_and_channel_modes()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync("PREFIX=(ohv)@%+ CHANMODES=beI,k,l,imnpst");
        await harness.JoinAsync("#kazo", "@neo trinity tank");
        var channel = harness.Client.GetChannel("#kazo")!;

        await harness.Transport.ServerSendAsync(":neo!~neo@kazo.ca MODE #kazo +ovbkl-t trinity tank *!*@matrix secret 50");

        Assert.Equal("@", channel.GetUser("trinity")!.Prefixes);
        Assert.Equal("+", channel.GetUser("tank")!.Prefixes);
        Assert.Equal("secret", channel.Key);
        Assert.Equal("50", channel.Modes['l']);
        Assert.False(channel.Modes.ContainsKey('b'));
        var mode = harness.Activities[^1];
        Assert.Equal(IrcActivityKind.Mode, mode.Kind);
        Assert.Equal("+ovbkl-t trinity tank *!*@matrix secret 50", mode.Detail);

        await harness.Transport.ServerSendAsync(":neo!~neo@kazo.ca MODE #kazo -o+h-kl trinity trinity secret");

        Assert.Equal("%", channel.GetUser("trinity")!.Prefixes);
        Assert.Null(channel.Key);
        Assert.False(channel.Modes.ContainsKey('l'));
    }

    [Fact]
    public async Task Channel_mode_reply_replaces_the_modes()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo");

        await harness.Transport.ServerSendAsync(":srv 324 neo #kazo +ntk key");

        var channel = harness.Client.GetChannel("#kazo")!;
        Assert.Equal(["k", "n", "t"], channel.Modes.Keys.Select(k => k.ToString()).Order());
        Assert.Equal("key", channel.Key);
    }

    [Fact]
    public async Task Own_user_mode_goes_to_the_server_tab()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":neo MODE neo :+iw");

        Assert.Contains(harness.ServerTexts, t => t.Command == "MODE");
    }

    [Fact]
    public async Task Away_notify_and_account_notify_update_users()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo trinity");
        var transport = harness.Transport;

        await transport.ServerSendAsync(":trinity!t@z AWAY :Gone fishing", ":trinity!t@z ACCOUNT trin");
        var trinity = harness.Client.GetUser("trinity")!;
        Assert.True(trinity.IsAway);
        Assert.Equal("Gone fishing", trinity.AwayMessage);
        Assert.Equal("trin", trinity.Account);
        Assert.Equal(IrcActivityKind.Away, harness.Activities[^1].Kind);

        await transport.ServerSendAsync(":trinity!t@z AWAY", ":trinity!t@z ACCOUNT *");
        Assert.False(trinity.IsAway);
        Assert.Null(trinity.Account);
        Assert.Equal(IrcActivityKind.Back, harness.Activities[^1].Kind);
    }

    [Fact]
    public async Task Extended_join_records_the_account()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo");

        await harness.Transport.ServerSendAsync(":trinity!t@z JOIN #kazo trin :Trinity", ":tank!k@z JOIN #kazo * :Tank");

        Assert.Equal("trin", harness.Client.GetUser("trinity")!.Account);
        Assert.Null(harness.Client.GetUser("tank")!.Account);
    }

    [Fact]
    public async Task Own_away_state_follows_305_and_306()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Client.SetAwayAsync("brb");
        await harness.Transport.WaitForSentAsync("AWAY brb");
        await harness.Transport.ServerSendAsync(":srv 306 neo :You have been marked as being away");
        Assert.True(harness.Client.IsAway);

        await harness.Client.SetAwayAsync(null);
        await harness.Transport.WaitForSentAsync(l => l == "AWAY");
        await harness.Transport.ServerSendAsync(":srv 305 neo :You are no longer marked as being away");
        Assert.False(harness.Client.IsAway);
    }

    [Fact]
    public async Task Lines_for_unknown_channels_are_ignored()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(
            ":t!t@z PART #nowhere",
            ":t!t@z KICK #nowhere x",
            ":t!t@z TOPIC #nowhere :x",
            ":t!t@z MODE #nowhere +o x",
            ":srv 353 neo = #nowhere :a b",
            ":srv 366 neo #nowhere :End",
            ":srv 324 neo #nowhere +n",
            ":srv 333 neo #nowhere x 1",
            ":ghost!g@z AWAY :x",
            ":ghost!g@z ACCOUNT x",
            "JOIN #nosource",
            ":x QUIT",
            "NICK nosource");

        Assert.Empty(harness.Client.Channels);
        Assert.Equal(IrcClientState.Connected, harness.Client.State);
    }
}
