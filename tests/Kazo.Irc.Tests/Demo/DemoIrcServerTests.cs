using Kazo.Irc.Demo;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Tests.Chat;

namespace Kazo.Irc.Tests.Demo;

/// <summary>The demonstration server, seen from a real <see cref="IrcClient"/>.</summary>
public sealed class DemoIrcServerTests
{
    [Theory]
    [InlineData("!mention", true, 3)]
    [InlineData("!mention 5", true, 5)]
    [InlineData("  !MENTION 10 ", true, 10)]
    [InlineData("!mention 999", true, 60)]
    [InlineData("!mention soon", true, 3)]
    [InlineData("!mentions", false, 3)]
    [InlineData("hello !mention", false, 3)]
    public void The_bot_reads_its_commands(string text, bool expected, int seconds)
    {
        Assert.Equal(expected, DemoIrcServer.TryReadCommand(text, "!mention", out var delay));
        Assert.Equal(TimeSpan.FromSeconds(seconds), delay);
    }

    [Fact]
    public async Task A_taken_nickname_gets_the_alternate_one()
    {
        await using var client = new IrcClient(new IrcClientOptions { Nick = "kazo-bot", AlternateNicks = ["neo"] }, () => new DemoIrcServer());
        client.Start();

        await ChatHarness.WaitUntilAsync(() => client.State == IrcClientState.Connected);

        Assert.Equal("neo", client.CurrentNick);
        Assert.Equal("KazoDemo", client.ServerSupport.NetworkName);
    }

    [Fact]
    public async Task Ping_topic_whois_away_nick_and_part_are_answered()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();
        var client = session.Client;

        await harness.Chat.SubmitAsync("/topic red pill");
        await ChatHarness.WaitUntilAsync(() => channel.Channel!.Topic == "red pill");

        await harness.Chat.SubmitAsync("/whois kazo-bot");
        await harness.Chat.SubmitAsync("/whois smith");
        await ChatHarness.WaitUntilAsync(() => session.Server.Lines.Any(line => line.Text == "smith: no such nickname."));
        Assert.Contains(session.Server.Lines, line => line.Text.StartsWith("kazo-bot is bot@demo.kazo.local", StringComparison.Ordinal));

        await harness.Chat.SubmitAsync("/away lunch");
        await ChatHarness.WaitUntilAsync(() => client.IsAway);
        await harness.Chat.SubmitAsync("/away");
        await ChatHarness.WaitUntilAsync(() => !client.IsAway);

        await harness.Chat.SubmitAsync("/nick the-one");
        await ChatHarness.WaitUntilAsync(() => client.CurrentNick == "the-one");
        Assert.True(channel.Channel!.HasUser("the-one"));

        await harness.Chat.SubmitAsync("/raw PING hello");
        await harness.Chat.SubmitAsync("/raw FROB");
        await ChatHarness.WaitUntilAsync(() => session.Server.Lines.Any(line => line.Kind == Irc.Chat.ChatLineKind.Error && line.Text.Contains("Unknown command", StringComparison.Ordinal)));

        await harness.Chat.SubmitAsync("/part");
        await ChatHarness.WaitUntilAsync(() => !channel.IsJoined);
    }

    [Fact]
    public async Task Talking_to_someone_unknown_is_an_error()
    {
        await using var harness = new ChatHarness();
        var session = await harness.ConnectAsync();

        await harness.Chat.SubmitAsync("/msg smith hi");
        await harness.Chat.SubmitAsync("/msg #nowhere hi");

        await ChatHarness.WaitUntilAsync(() => session.Server.Lines.Count(line => line.Kind == Irc.Chat.ChatLineKind.Error) >= 2);
    }

    [Fact]
    public async Task Writing_after_the_end_fails_and_reading_gives_the_end()
    {
        var server = new DemoIrcServer();
        await server.ConnectAsync(CancellationToken.None);
        await server.CloseAsync(CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => server.WriteLineAsync("NICK neo", CancellationToken.None));
        Assert.Null(await server.ReadLineAsync(CancellationToken.None));
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Commands_before_registration_are_refused()
    {
        await using var server = new DemoIrcServer();
        await server.WriteLineAsync("JOIN #kazo", CancellationToken.None);

        Assert.Contains(" 451 ", await server.ReadLineAsync(CancellationToken.None), StringComparison.Ordinal);
        Assert.Null(server.ClientNick);
    }
}
