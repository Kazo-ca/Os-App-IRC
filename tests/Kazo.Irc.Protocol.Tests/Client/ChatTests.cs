using Kazo.Irc.Protocol.Client;

namespace Kazo.Irc.Protocol.Tests.Client;

public sealed class ChatTests
{
    [Fact]
    public async Task Channel_message_uses_the_server_time()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#Kazo", "@neo trinity");

        await harness.Transport.ServerSendAsync("@time=2026-11-07T03:59:00.500Z;account=trin :trinity!t@z PRIVMSG #kazo :hello neo");

        var chat = Assert.Single(harness.Chats);
        Assert.Equal(IrcChatMessageKind.Message, chat.Kind);
        Assert.Equal("#Kazo", chat.Conversation);
        Assert.Equal("hello neo", chat.Text);
        Assert.False(chat.IsPrivate);
        Assert.False(chat.IsOwn);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 3, 59, 0, 500, TimeSpan.Zero), chat.Time);
        Assert.Equal("trin", harness.Client.GetUser("trinity")!.Account);
    }

    [Fact]
    public async Task Message_without_server_time_uses_the_clock()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":trinity!t@z PRIVMSG #kazo :hi", "@time=garbage :trinity!t@z PRIVMSG #kazo :hi");

        Assert.All(harness.Chats, c => Assert.Equal(harness.Time.GetUtcNow(), c.Time));
    }

    [Fact]
    public async Task Private_message_opens_a_query()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":trinity!t@z PRIVMSG neo :are you there?", ":trinity!t@z PRIVMSG neo :neo?");

        Assert.Equal(["trinity"], harness.OpenedQueries);
        Assert.Equal(["trinity"], harness.Client.Queries);
        Assert.All(harness.Chats, c =>
        {
            Assert.True(c.IsPrivate);
            Assert.Equal("trinity", c.Conversation);
        });
    }

    [Fact]
    public async Task Private_notice_does_not_open_a_query()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":NickServ!s@services NOTICE neo :This nickname is registered");

        var chat = Assert.Single(harness.Chats);
        Assert.Equal(IrcChatMessageKind.Notice, chat.Kind);
        Assert.Empty(harness.Client.Queries);
    }

    [Fact]
    public async Task Server_notices_and_notices_before_registration_go_to_the_server_tab()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Transport.ServerSendAsync(
            ":irc.kazo.ca NOTICE * :*** Looking up your hostname",
            "NOTICE AUTH :*** no source",
            ":services!s@x NOTICE neo :before 001");

        Assert.Empty(harness.Chats);
        Assert.Equal(3, harness.ServerTexts.Count(t => t.Command == "NOTICE"));
    }

    [Fact]
    public async Task Action_is_read_from_ctcp()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(":trinity!t@z PRIVMSG #kazo :\u0001ACTION waves\u0001");

        var chat = Assert.Single(harness.Chats);
        Assert.Equal(IrcChatMessageKind.Action, chat.Kind);
        Assert.Equal("waves", chat.Text);
    }

    [Fact]
    public async Task Status_message_to_operators_belongs_to_the_channel()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo");

        await harness.Transport.ServerSendAsync(":trinity!t@z NOTICE @#kazo :ops only");

        var chat = Assert.Single(harness.Chats);
        Assert.Equal("#kazo", chat.Conversation);
        Assert.False(chat.IsPrivate);
    }

    [Theory]
    [InlineData("VERSION", "", "NOTICE trinity :\u0001VERSION Kazo OS IRC\u0001")]
    [InlineData("PING", "12345", "NOTICE trinity :\u0001PING 12345\u0001")]
    [InlineData("CLIENTINFO", "", "NOTICE trinity :\u0001CLIENTINFO ACTION CLIENTINFO PING TIME VERSION\u0001")]
    [InlineData("TIME", "", "NOTICE trinity :\u0001TIME Sat, 07 Nov 2026 04:00:00 GMT\u0001")]
    public async Task Ctcp_queries_are_answered(string command, string arguments, string answer)
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        var query = arguments.Length == 0 ? $"\u0001{command}\u0001" : $"\u0001{command} {arguments}\u0001";

        await harness.Transport.ServerSendAsync($":trinity!t@z PRIVMSG neo :{query}");

        await harness.Transport.WaitForSentAsync(l => l == answer);
        Assert.Empty(harness.Chats);
        Assert.Contains(harness.ServerTexts, t => t.Command == "CTCP" && t.Text == $"CTCP {command} from trinity");
    }

    [Fact]
    public async Task Unknown_ctcp_and_ctcp_replies_are_only_shown()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        harness.Transport.ClearSent();

        await harness.Transport.ServerSendAsync(
            ":trinity!t@z PRIVMSG neo :\u0001DCC SEND file\u0001",
            ":trinity!t@z NOTICE neo :\u0001VERSION mIRC\u0001");

        Assert.Empty(harness.Transport.SentLines);
        Assert.Contains(harness.ServerTexts, t => t.Text == "CTCP VERSION reply from trinity: mIRC");
    }

    [Fact]
    public async Task Whois_is_gathered_until_its_end()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo trinity");

        await harness.Client.WhoisAsync("trinity");
        await harness.Transport.WaitForSentAsync("WHOIS trinity");
        await harness.Transport.ServerSendAsync(
            ":srv 311 neo trinity ~t zion.net * :Trinity",
            ":srv 312 neo trinity irc.kazo.ca :Kazo server",
            ":srv 313 neo trinity :is an IRC operator",
            ":srv 317 neo trinity 42 1762488000 :seconds idle, signon time",
            ":srv 319 neo trinity :@#kazo +#zion",
            ":srv 330 neo trinity trin :is logged in as",
            ":srv 301 neo trinity :Gone fishing",
            ":srv 318 neo trinity :End of /WHOIS list.");

        var info = Assert.Single(harness.Whois);
        Assert.Equal("~t", info.UserName);
        Assert.Equal("zion.net", info.Host);
        Assert.Equal("Trinity", info.RealName);
        Assert.Equal("irc.kazo.ca", info.Server);
        Assert.Equal("Kazo server", info.ServerInfo);
        Assert.True(info.IsOperator);
        Assert.Equal(42, info.IdleSeconds);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762488000), info.SignedOnAt);
        Assert.Equal(["@#kazo", "+#zion"], info.Channels);
        Assert.Equal("trin", info.Account);
        Assert.Equal("Gone fishing", info.AwayMessage);
        Assert.True(info.Exists);
        Assert.Equal("Gone fishing", harness.Client.GetUser("trinity")!.AwayMessage);
    }

    [Fact]
    public async Task Whois_of_an_unknown_nick_says_it_does_not_exist()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Client.WhoisAsync("ghost");
        await harness.Transport.ServerSendAsync(":srv 401 neo ghost :No such nick", ":srv 318 neo ghost :End");

        Assert.False(Assert.Single(harness.Whois).Exists);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task Away_reply_outside_whois_goes_to_the_server_tab_and_errors_are_raised()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();

        await harness.Transport.ServerSendAsync(
            ":srv 301 neo trinity :Gone fishing",
            ":srv 401 neo ghost :No such nick/channel",
            ":srv 251 neo :There are 3 users",
            ":srv ERROR :Closing link");

        Assert.Contains(harness.ServerTexts, t => t.Command == "301" && t.Text == "trinity Gone fishing");
        Assert.Contains(harness.ServerTexts, t => t.Command == "251");
        Assert.Contains(harness.Errors, e => e.Code == "401" && e.Message == "ghost No such nick/channel");
        Assert.Contains(harness.Errors, e => e.Code == "ERROR" && e.Message == "Closing link");
    }

    [Fact]
    public async Task An_own_message_is_announced_before_it_is_written()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync();
        var writtenWhenAnnounced = true;
        harness.Client.ChatMessage += (_, chat) =>
        {
            if (chat.IsOwn)
            {
                writtenWhenAnnounced = harness.Transport.SentLines.Any(line => line.StartsWith("PRIVMSG #kazo", StringComparison.Ordinal));
            }
        };

        await harness.Client.SendMessageAsync("#kazo", "hello");

        // An answer the server sends at once can never be shown before the line it answers.
        Assert.False(writtenWhenAnnounced);
        Assert.Contains("PRIVMSG #kazo hello", harness.Transport.SentLines);
    }
}
