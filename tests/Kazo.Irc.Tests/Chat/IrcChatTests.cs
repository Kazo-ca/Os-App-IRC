using Kazo.Irc.Chat;
using Kazo.Irc.Demo;
using Kazo.Irc.Protocol.Client;

namespace Kazo.Irc.Tests.Chat;

/// <summary>The chat on the demonstration network: conversations, input, unread, mentions, notifications (decision 14).</summary>
public sealed class IrcChatTests
{
    [Fact]
    public async Task Connecting_opens_the_server_tab_and_registers_with_the_user_name()
    {
        await using var harness = new ChatHarness();

        var session = await harness.ConnectAsync();

        Assert.Same(session.Server, harness.Chat.Selected);
        Assert.Equal("Kazo Demo", session.Server.Name);
        Assert.Equal("neo", session.Client.CurrentNick);
        await ChatHarness.WaitUntilAsync(() => session.Server.Lines.Any(line => line.Text.Contains("/join #kazo", StringComparison.Ordinal)));
        Assert.Contains(session.Server.Lines, line => line.Text == "Connected as neo.");
    }

    [Fact]
    public async Task Connecting_twice_to_the_same_network_only_shows_it()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();

        Assert.Same(session, harness.Chat.Connect(ChatHarness.Demo, "neo"));
        Assert.Single(harness.Chat.Sessions);
    }

    [Fact]
    public async Task Joining_by_typing_opens_the_channel_with_its_users_and_topic()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();

        var channel = await harness.JoinKazoAsync();

        Assert.Equal(ConversationKind.Channel, channel.Kind);
        Assert.True(channel.IsJoined);
        Assert.Equal("Welcome to the Kazo OS IRC demonstration network", channel.Channel!.Topic);
        Assert.Contains(channel.Users, user => user.Nick == DemoIrcServer.BotNick && user.HighestPrefix == '@');
        Assert.Contains(channel.Lines, line => line is { Kind: ChatLineKind.Activity, Activity: IrcActivityKind.Join, IsOwn: true });
        Assert.Equal(["Kazo Demo", "#kazo"], harness.Session.Conversations.Select(c => c.Name));
    }

    [Fact]
    public async Task A_message_is_shown_as_own_and_the_bot_answers_with_a_mention()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        Assert.Equal(ChatInputProblem.None, await harness.Chat.SubmitAsync("kazo-bot: hello"));
        await ChatHarness.WaitUntilAsync(() => channel.Lines.Any(line => line.Nick == DemoIrcServer.BotNick));

        Assert.Contains(channel.Lines, line => line is { Kind: ChatLineKind.Message, IsOwn: true, Nick: "neo", Text: "kazo-bot: hello" });
        var answer = channel.Lines.Single(line => line.Nick == DemoIrcServer.BotNick);
        Assert.True(answer.IsMention);
        // Seen: the channel is open and the window has the focus.
        Assert.Equal(0, channel.UnreadCount);
        Assert.Equal(0, harness.Chat.AttentionCount);
        Assert.Empty(harness.Notifier.Posted);
    }

    [Fact]
    public async Task A_mention_while_the_window_has_no_focus_notifies_and_counts_until_seen()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        await harness.Chat.SubmitAsync("!mention 5");
        harness.Chat.SetWindowActive(false);
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        await ChatHarness.WaitUntilAsync(() => harness.Notifier.Posted.Count == 1);

        Assert.Equal(("kazo-bot in #kazo", "neo: you asked me to name you, here you are."), harness.Notifier.Posted[0]);
        Assert.Equal(1, channel.UnreadCount);
        Assert.Equal(1, channel.HighlightCount);
        Assert.Equal(1, harness.Chat.AttentionCount);

        harness.Chat.SetWindowActive(true);

        Assert.Equal(0, channel.UnreadCount);
        Assert.Equal(0, harness.Chat.AttentionCount);
    }

    [Fact]
    public async Task A_private_message_opens_a_conversation_without_taking_the_user_away()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        await harness.Chat.SubmitAsync("!private 2");
        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await ChatHarness.WaitUntilAsync(() => harness.Session.Find(DemoIrcServer.BotNick)?.Lines.Count > 0);

        var query = harness.Session.Find(DemoIrcServer.BotNick)!;
        Assert.Equal(ConversationKind.Private, query.Kind);
        Assert.Same(channel, harness.Chat.Selected);
        Assert.Equal(1, query.UnreadCount);
        Assert.Equal(1, query.HighlightCount);
        Assert.Equal(1, harness.Chat.AttentionCount);
        // The window has the focus: no notification, the counter and the list tell.
        Assert.Empty(harness.Notifier.Posted);

        harness.Chat.Select(query);
        Assert.Equal(0, harness.Chat.AttentionCount);
    }

    [Fact]
    public async Task A_private_message_while_the_window_has_no_focus_notifies_as_private()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        await harness.JoinKazoAsync();

        harness.Chat.SetWindowActive(false);
        await harness.Chat.SubmitAsync("!private 1");
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await ChatHarness.WaitUntilAsync(() => harness.Notifier.Posted.Count == 1);

        Assert.Equal("kazo-bot (private)", harness.Notifier.Posted[0].Title);
    }

    [Fact]
    public async Task Messages_in_another_conversation_are_unread_but_not_counted_in_the_title()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();
        harness.Chat.Select(session.Server);

        // The demonstration bot only talks when asked; a line from another user is played here.
        await harness.Chat.SubmitAsync("/msg #kazo kazo-bot: hi");
        await ChatHarness.WaitUntilAsync(() => channel.Lines.Count(line => line.Nick == DemoIrcServer.BotNick) == 1);

        Assert.Equal(1, channel.UnreadCount);
        Assert.Equal(1, channel.HighlightCount);
        Assert.Equal(0, session.Server.UnreadCount);
    }

    [Fact]
    public async Task Query_opens_and_shows_a_private_conversation()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();

        Assert.Equal(ChatInputProblem.None, await harness.Chat.SubmitAsync("/query kazo-bot"));

        Assert.Equal(ConversationKind.Private, harness.Chat.Selected!.Kind);
        Assert.Equal("kazo-bot", harness.Chat.Selected.Name);
        Assert.Equal(ChatInputProblem.None, await harness.Chat.SubmitAsync("hello bot"));
        await ChatHarness.WaitUntilAsync(() => harness.Chat.Selected.Lines.Any(line => line.Text == "You said: hello bot"));
    }

    [Theory]
    [InlineData("/frobnicate", ChatInputProblem.UnknownCommand)]
    [InlineData("/join", ChatInputProblem.MissingArgument)]
    [InlineData("hello", ChatInputProblem.NoConversation)]
    [InlineData("/topic", ChatInputProblem.NoConversation)]
    [InlineData("   ", ChatInputProblem.Empty)]
    public async Task A_line_that_cannot_be_done_says_why(string input, ChatInputProblem expected)
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync(); // the server tab is open

        Assert.Equal(expected, await harness.Chat.SubmitAsync(input));
    }

    [Fact]
    public async Task A_channel_command_in_a_private_conversation_is_refused()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        await harness.Chat.SubmitAsync("/query kazo-bot");

        Assert.Equal(ChatInputProblem.NotAChannel, await harness.Chat.SubmitAsync("/topic"));
    }

    [Fact]
    public async Task Typing_without_a_network_is_not_connected()
    {
        await using var harness = new ChatHarness();

        Assert.Equal(ChatInputProblem.NotConnected, await harness.Chat.SubmitAsync("/join #kazo"));
    }

    [Fact]
    public async Task Closing_a_channel_leaves_it_and_shows_the_server_tab()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        await harness.Chat.CloseConversationAsync(channel);

        Assert.Same(session.Server, harness.Chat.Selected);
        Assert.Equal(["Kazo Demo"], session.Conversations.Select(c => c.Name));
        await ChatHarness.WaitUntilAsync(() => session.Client.GetChannel("#kazo")?.IsJoined != true);
    }

    [Fact]
    public async Task Closing_the_window_sends_quit()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();

        await harness.Chat.QuitAllAsync("bye");

        Assert.Equal(IrcClientState.Disconnected, session.Client.State);
        Assert.Contains(session.Server.Lines, line => line.Text == "Disconnected.");
    }

    [Fact]
    public async Task The_server_tab_is_never_closed()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();

        await harness.Chat.CloseConversationAsync(session.Server);

        Assert.Contains(session.Server, session.Conversations);
    }

    [Fact]
    public void A_network_needs_the_factory_of_its_transport()
    {
        var catalog = new IrcNetworkCatalog();
        var network = new IrcNetwork("x", "X", "websocket", "irc.example", 443);

        Assert.Throws<InvalidOperationException>(() => catalog.TransportsFor(network));
        Assert.Throws<ArgumentException>(() => catalog.Add(network, new DemoOnly()));
    }

    private sealed class DemoOnly : IIrcTransportFactory
    {
        public string Kind => "demo";

        public Protocol.Transport.IIrcTransport Create(IrcNetwork network) => new DemoIrcServer();
    }

    [Fact]
    public async Task A_notification_has_no_formatting_code()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        harness.Chat.SetWindowActive(false);

        await harness.Chat.SubmitAsync("/msg kazo-bot \u0002hi\u0002 \u000304there\u0003");
        await ChatHarness.WaitUntilAsync(() => harness.Notifier.Posted.Count == 1);

        Assert.Equal("You said: hi there", harness.Notifier.Posted[0].Message);
    }

    [Fact]
    public async Task The_bot_sends_links_and_formatted_text_on_request()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        await harness.Chat.SubmitAsync("!links");
        await ChatHarness.WaitUntilAsync(() => channel.Lines.Any(line => line.Nick == DemoIrcServer.BotNick));

        Assert.Equal(DemoIrcServer.LinksMessage, channel.Lines.Single(line => line.Nick == DemoIrcServer.BotNick).Text);
    }
}
