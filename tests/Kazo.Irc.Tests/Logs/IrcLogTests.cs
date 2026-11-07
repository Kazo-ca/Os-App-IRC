using Kazo.Irc.Chat;
using Kazo.Irc.Logs;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Tests.Chat;

namespace Kazo.Irc.Tests.Logs;

/// <summary>The logs (decision 15): paths, safe names, line format, and the writer on the chat.</summary>
public sealed class IrcLogTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 14, 5, 9, TimeSpan.Zero);

    [Fact]
    public void A_log_is_one_file_per_day_per_conversation()
    {
        Assert.Equal(
            "/home/neo/.local/share/ca.kazo.irc/logs/Kazo Demo/#kazo/2026-11-07.log",
            IrcLog.PathFor("/home/neo/", "Kazo Demo", "#kazo", new DateOnly(2026, 11, 7)));
    }

    [Theory]
    [InlineData("#kazo", "#kazo")]
    [InlineData("#a/b", "#a_b")]
    [InlineData("..", "_.")]
    [InlineData(".", "_")]
    [InlineData(".hidden", "_hidden")]
    [InlineData("#..\\..", "#.._..")]
    [InlineData("", "_")]
    [InlineData("   ", "_")]
    [InlineData(null, "_")]
    [InlineData("#a\u0001b:c", "#a_b_c")]
    public void A_name_cannot_leave_the_logs_folder(string? name, string expected)
    {
        Assert.Equal(expected, IrcLog.SafeName(name));
    }

    [Fact]
    public void A_long_name_is_cut()
    {
        Assert.Equal(IrcLog.MaxNameLength, IrcLog.SafeName(new string('#', 200)).Length);
    }

    [Fact]
    public void A_path_made_of_hostile_names_stays_under_the_logs_folder()
    {
        var path = IrcLog.PathFor("/home/neo", "../..", "../../etc", new DateOnly(2026, 11, 7));

        Assert.StartsWith("/home/neo/.local/share/ca.kazo.irc/logs/", path, StringComparison.Ordinal);
        Assert.DoesNotContain("/../", path, StringComparison.Ordinal);
        Assert.DoesNotContain("/./", path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChatLineKind.Message, "[14:05:09] <neo> hello")]
    [InlineData(ChatLineKind.Action, "[14:05:09] * neo hello")]
    [InlineData(ChatLineKind.Notice, "[14:05:09] -neo- hello")]
    public void A_line_is_time_nick_text(ChatLineKind kind, string expected)
    {
        Assert.Equal(expected, IrcLog.Format(new ChatLine(At, kind, "neo", "hello"), At));
    }

    [Theory]
    [InlineData(IrcActivityKind.Join, "", "[14:05:09] *** neo joined")]
    [InlineData(IrcActivityKind.Part, "bye", "[14:05:09] *** neo left (bye)")]
    [InlineData(IrcActivityKind.Quit, "", "[14:05:09] *** neo quit")]
    [InlineData(IrcActivityKind.Nick, "the-one", "[14:05:09] *** neo is now known as the-one")]
    [InlineData(IrcActivityKind.Topic, "red pill", "[14:05:09] *** neo changed the topic to: red pill")]
    [InlineData(IrcActivityKind.Mode, "+o trinity", "[14:05:09] *** neo sets mode +o trinity")]
    [InlineData(IrcActivityKind.Away, "lunch", "[14:05:09] *** neo is away (lunch)")]
    [InlineData(IrcActivityKind.Back, "", "[14:05:09] *** neo is back")]
    public void An_activity_is_written_in_words(IrcActivityKind activity, string detail, string expected)
    {
        Assert.Equal(expected, IrcLog.Format(new ChatLine(At, ChatLineKind.Activity, "neo", detail, Activity: activity), At));
    }

    [Fact]
    public void A_kick_names_both_users()
    {
        var line = new ChatLine(At, ChatLineKind.Activity, "morpheus", "spam", Activity: IrcActivityKind.Kick, TargetNick: "smith");

        Assert.Equal("[14:05:09] *** smith was kicked by morpheus (spam)", IrcLog.Format(line, At));
    }

    [Fact]
    public void Server_lines_and_line_ends_are_not_written()
    {
        Assert.Null(IrcLog.Format(new ChatLine(At, ChatLineKind.Server, null, "MOTD"), At));
        Assert.Null(IrcLog.Format(new ChatLine(At, ChatLineKind.Error, null, "oops"), At));
        Assert.Equal("[14:05:09] <neo> a b", IrcLog.Format(new ChatLine(At, ChatLineKind.Message, "neo", "a\nb"), At));
    }

    [Fact]
    public async Task The_chat_writes_its_channel_in_the_days_file_and_not_the_server_tab()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();
        await harness.Chat.SubmitAsync("kazo-bot: hello");
        await ChatHarness.WaitUntilAsync(() => channel.Lines.Any(line => line.Nick == "kazo-bot"));
        await harness.Logs!.Idle;

        const string path = "/home/neo/.local/share/ca.kazo.irc/logs/Kazo Demo/#kazo/2026-11-07.log";
        var lines = harness.Files.Text(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            ["[14:05:09] *** neo joined", "[14:05:09] <neo> kazo-bot: hello", "[14:05:09] <kazo-bot> neo: hello! Try !mention 5 or !private 5."],
            lines);
        Assert.Single(harness.Files.Files.Keys, key => key.Contains("/logs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_day_and_time_are_the_OS_time_zone()
    {
        var files = new Settings.MemoryFileSystem();
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+10", TimeSpan.FromHours(10), "UTC+10", "UTC+10");
        var writer = new IrcLogWriter(files, "/home/neo", () => zone);
        await using var harness = new ChatHarness(withLogs: false);
        var session = await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();

        Assert.True(writer.Append(channel, new ChatLine(At, ChatLineKind.Message, "neo", "late")));
        Assert.False(writer.Append(session.Server, new ChatLine(At, ChatLineKind.Message, "neo", "server tab")));
        await writer.Idle;

        Assert.Equal("[00:05:09] <neo> late\n", files.Text("/home/neo/.local/share/ca.kazo.irc/logs/Kazo Demo/#kazo/2026-11-08.log"));
    }

    [Fact]
    public async Task Logs_turned_off_write_nothing()
    {
        await using var harness = new ChatHarness();
        harness.Chat.LogsEnabled = false;
        await harness.ConnectAsync();
        await harness.JoinKazoAsync();
        await harness.Logs!.Idle;

        Assert.DoesNotContain(harness.Files.Files.Keys, key => key.Contains("/logs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_write_is_reported_once_and_the_chat_goes_on()
    {
        await using var harness = new ChatHarness();
        var failures = new List<Exception>();
        harness.Logs!.Failed += failures.Add;
        harness.Files.WriteFailure = new UnauthorizedAccessException("denied");
        await harness.ConnectAsync();
        var channel = await harness.JoinKazoAsync();
        await harness.Chat.SubmitAsync("one");
        await harness.Chat.SubmitAsync("two");
        await harness.Logs.Idle;

        Assert.Single(failures);
        Assert.Contains(channel.Lines, line => line.Text == "two");
    }

    [Fact]
    public void A_log_line_has_no_formatting_code()
    {
        var line = new ChatLine(At, ChatLineKind.Message, "neo", "\u0002bold\u0002 \u000304,01red\u0003 \u001Ditalics\u001D\u000F");

        Assert.Equal("[14:05:09] <neo> bold red italics", IrcLog.Format(line, At));
    }
}
