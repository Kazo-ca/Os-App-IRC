using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Input;
using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Tests.Client;

public sealed class CommandTests
{
    private static async Task<ClientHarness> RegisteredAsync()
    {
        var harness = new ClientHarness();
        await harness.RegisterAsync();
        await harness.JoinAsync("#kazo", "@neo trinity");
        harness.Transport.ClearSent();
        return harness;
    }

    private static Task RunAsync(ClientHarness harness, string input, string? conversation = "#kazo") =>
        harness.Client.ExecuteAsync(IrcInputParser.Parse(input, conversation, harness.Client.ServerSupport.ChannelTypes));

    [Theory]
    [InlineData("hello", "PRIVMSG #kazo hello")]
    [InlineData("hello all", "PRIVMSG #kazo :hello all")]
    [InlineData("/msg trinity wake up", "PRIVMSG trinity :wake up")]
    [InlineData("/me waves", "PRIVMSG #kazo :\u0001ACTION waves\u0001")]
    [InlineData("/join #zion,#matrix key", "JOIN #zion,#matrix key")]
    [InlineData("/part bye", "PART #kazo bye")]
    [InlineData("/part", "PART #kazo")]
    [InlineData("/nick theone", "NICK theone")]
    [InlineData("/topic", "TOPIC #kazo")]
    [InlineData("/topic Free your mind", "TOPIC #kazo :Free your mind")]
    [InlineData("/whois trinity", "WHOIS trinity")]
    [InlineData("/away lunch", "AWAY lunch")]
    [InlineData("/away", "AWAY")]
    [InlineData("/query trinity hi", "PRIVMSG trinity hi")]
    [InlineData("/raw MODE #kazo +m", "MODE #kazo +m")]
    public async Task Each_command_sends_its_line(string input, string expected)
    {
        await using var harness = await RegisteredAsync();

        await RunAsync(harness, input);

        await harness.Transport.WaitForSentAsync(l => l == expected);
    }

    [Fact]
    public async Task Own_messages_are_shown()
    {
        await using var harness = await RegisteredAsync();

        await RunAsync(harness, "/me waves");
        await RunAsync(harness, "/msg trinity psst");

        Assert.Equal(2, harness.Chats.Count);
        Assert.All(harness.Chats, c => Assert.True(c.IsOwn));
        Assert.Equal(IrcChatMessageKind.Action, harness.Chats[0].Kind);
        Assert.Equal("waves", harness.Chats[0].Text);
        Assert.Equal("neo!~neo@kazo.ca", harness.Chats[0].Sender.ToString());
        Assert.True(harness.Chats[1].IsPrivate);
    }

    [Fact]
    public async Task A_long_message_is_cut_so_relayed_lines_fit()
    {
        await using var harness = await RegisteredAsync();
        var words = string.Join(' ', Enumerable.Repeat("matrix", 200));

        await harness.Client.SendMessageAsync("#kazo", words);

        var lines = harness.Transport.SentLines;
        Assert.True(lines.Count > 1);
        foreach (var line in lines)
        {
            var relayed = ":neo!~neo@kazo.ca " + line;
            Assert.True(relayed.Length + 2 <= IrcLimits.MaxLineBytes, relayed);
        }

        Assert.Equal(words, string.Join(' ', harness.Chats.Select(c => c.Text)));
    }

    [Fact]
    public async Task A_long_action_is_cut_with_room_for_the_ctcp()
    {
        await using var harness = new ClientHarness();
        await harness.RegisterAsync(); // no JOIN: the client does not know its host, so it assumes the longest one

        await harness.Client.SendActionAsync("#kazo", new string('x', 1000));

        Assert.All(harness.Transport.SentLines.Where(l => l.StartsWith("PRIVMSG", StringComparison.Ordinal)), line =>
            Assert.True(line.Length + 2 + 1 + "neo".Length + 10 + 63 + 3 <= IrcLimits.MaxLineBytes));
    }

    [Fact]
    public async Task An_empty_message_sends_nothing()
    {
        await using var harness = await RegisteredAsync();

        await harness.Client.SendMessageAsync("#kazo", "");
        await harness.Client.SendNoticeAsync("trinity", "notice");

        Assert.Equal(["NOTICE trinity notice"], harness.Transport.SentLines);
    }

    [Fact]
    public async Task Join_remembers_the_key_and_part_forgets_the_channel()
    {
        await using var harness = await RegisteredAsync();

        await harness.Client.JoinAsync("#secret", "key");
        Assert.Equal("key", harness.Client.GetChannel("#secret")!.Key);
        Assert.True(harness.Client.GetChannel("#secret")!.WantsJoin);

        await harness.Client.PartAsync("#kazo");
        Assert.False(harness.Client.GetChannel("#kazo")!.WantsJoin);
    }

    [Fact]
    public async Task Query_without_text_opens_it_and_close_forgets_it()
    {
        await using var harness = await RegisteredAsync();

        await RunAsync(harness, "/query Morpheus");
        await RunAsync(harness, "/query morpheus");

        Assert.Equal(["Morpheus"], harness.Client.Queries);
        Assert.Equal(["Morpheus"], harness.OpenedQueries);
        Assert.NotNull(harness.Client.GetUser("morpheus"));
        Assert.Empty(harness.Transport.SentLines);

        harness.Client.CloseQuery("MORPHEUS");
        Assert.Empty(harness.Client.Queries);
        Assert.Null(harness.Client.GetUser("morpheus"));
    }

    [Fact]
    public async Task An_invalid_command_is_refused()
    {
        await using var harness = await RegisteredAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(harness, "/dance"));
    }

    [Fact]
    public async Task Raw_refuses_what_is_not_a_message()
    {
        await using var harness = await RegisteredAsync();

        await Assert.ThrowsAsync<IrcProtocolException>(() => harness.Client.SendRawAsync(":only-a-source"));
    }

    [Fact]
    public async Task Sending_while_disconnected_is_refused()
    {
        await using var harness = new ClientHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Client.SendMessageAsync("#kazo", "hi"));
    }

    [Fact]
    public async Task Nick_before_registration_becomes_the_wanted_nick()
    {
        await using var harness = new ClientHarness();
        await harness.StartAsync();

        await harness.Client.ChangeNickAsync("theone");
        await harness.Transport.ServerSendAsync(":srv 001 theone :Welcome");

        Assert.Equal("theone", harness.Client.CurrentNick);
    }

    [Fact]
    public async Task Quit_sends_quit_and_stops_for_good()
    {
        await using var harness = await RegisteredAsync();
        var transport = harness.Transport;

        var quit = RunAsync(harness, "/quit see you");
        await transport.WaitForSentAsync("QUIT :see you");
        transport.ServerClose();
        await quit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
        Assert.Single(harness.Transports);
        Assert.Empty(harness.Reconnects);
    }

    [Fact]
    public async Task Quit_closes_the_connection_itself_when_the_server_stays_silent()
    {
        await using var harness = await RegisteredAsync();

        var quit = harness.Client.QuitAsync();
        await harness.Transport.WaitForSentAsync("QUIT :Kazo OS IRC");
        await ClientHarness.WaitUntilAsync(() => { harness.Time.Advance(TimeSpan.FromSeconds(1)); return quit.IsCompleted; });

        await quit;
        Assert.True(harness.Transport.ClosedByClient);
        Assert.Equal(IrcClientState.Disconnected, harness.Client.State);
    }

    [Fact]
    public async Task Quit_of_a_stopped_client_does_nothing()
    {
        await using var harness = new ClientHarness();

        await harness.Client.QuitAsync();

        Assert.Empty(harness.Transports);
    }
}
