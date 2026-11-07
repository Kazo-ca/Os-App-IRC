using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Tests.Messages;

public sealed class IrcMessageTests
{
    [Fact]
    public void Parse_reads_tags_source_command_and_parameters()
    {
        var message = IrcMessage.Parse("@time=2026-11-07T04:00:00.000Z;msgid=abc :neo!~neo@kazo.ca PRIVMSG #kazo :hello world\r\n");

        Assert.Equal("2026-11-07T04:00:00.000Z", message.GetTag("time"));
        Assert.Equal("abc", message.GetTag("msgid"));
        Assert.Equal(new IrcSource("neo", "~neo", "kazo.ca"), message.Source);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal(["#kazo", "hello world"], message.Parameters);
    }

    [Fact]
    public void Parse_keeps_an_empty_trailing_parameter()
    {
        var message = IrcMessage.Parse(":srv TOPIC #kazo :");

        Assert.Equal(["#kazo", ""], message.Parameters);
    }

    [Fact]
    public void Parse_tolerates_extra_spaces_and_a_lower_case_command()
    {
        var message = IrcMessage.Parse(":srv   notice   neo   :hi  there");

        Assert.Equal("NOTICE", message.Command);
        Assert.Equal(["neo", "hi  there"], message.Parameters);
    }

    [Fact]
    public void Parse_reads_a_line_without_source_or_trailing_colon()
    {
        var message = IrcMessage.Parse("PING kazo");

        Assert.Null(message.Source);
        Assert.Equal("kazo", message.GetParameter(0));
        Assert.Null(message.GetParameter(1));
    }

    [Fact]
    public void Parse_reads_a_numeric()
    {
        var message = IrcMessage.Parse(":irc.kazo.ca 001 neo :Welcome");

        Assert.True(message.IsNumeric);
        Assert.True(message.Source!.IsServer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@a=b ")]
    [InlineData(":source")]
    public void Parse_refuses_a_line_without_command(string line)
    {
        Assert.Throws<IrcProtocolException>(() => IrcMessage.Parse(line));
        Assert.False(IrcMessage.TryParse(line, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void Format_adds_the_colon_only_when_the_last_parameter_needs_it()
    {
        Assert.Equal("NICK neo", new IrcMessage("NICK", "neo").Format());
        Assert.Equal("PRIVMSG #kazo :hello world", new IrcMessage("PRIVMSG", "#kazo", "hello world").Format());
        Assert.Equal("PRIVMSG #kazo ::)", new IrcMessage("PRIVMSG", "#kazo", ":)").Format());
        Assert.Equal("TOPIC #kazo :", new IrcMessage("TOPIC", "#kazo", "").Format());
    }

    [Fact]
    public void Format_writes_tags_and_source()
    {
        var tags = new Dictionary<string, string> { ["+draft/reply"] = "a b;c", ["flag"] = "" };
        var message = new IrcMessage("PRIVMSG", ["#kazo", "hi"], new IrcSource("neo", "u", "h"), tags);

        Assert.Equal(@"@+draft/reply=a\sb\:c;flag :neo!u@h PRIVMSG #kazo hi", message.Format());
    }

    [Fact]
    public void Format_then_parse_gives_the_same_message()
    {
        var tags = new Dictionary<string, string> { ["k"] = "x\\y\r\n z" };
        var original = new IrcMessage("PRIVMSG", ["#kazo", "Ça va ? :) ok"], new IrcSource("neo"), tags);

        var copy = IrcMessage.Parse(original.Format());

        Assert.Equal(original.Tags, copy.Tags);
        Assert.Equal(original.Source, copy.Source);
        Assert.Equal(original.Parameters, copy.Parameters);
    }

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void Format_refuses_line_breaks_and_nul(string text)
    {
        Assert.Throws<IrcProtocolException>(() => new IrcMessage("PRIVMSG", "#kazo", text).Format());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData(":a")]
    public void Format_refuses_a_middle_parameter_that_needs_a_colon(string middle)
    {
        Assert.Throws<IrcProtocolException>(() => new IrcMessage("PRIVMSG", middle, "text").Format());
    }

    [Fact]
    public void Format_refuses_a_line_longer_than_512_bytes()
    {
        var fits = new IrcMessage("PRIVMSG", "#k", new string('a', 512 - 2 - "PRIVMSG #k ".Length));
        var tooLong = new IrcMessage("PRIVMSG", "#k", new string('a', 512 - 1 - "PRIVMSG #k ".Length));

        Assert.Equal(510, fits.Format().Length);
        var error = Assert.Throws<IrcMessageTooLongException>(() => tooLong.Format());
        Assert.Equal("message", error.Part);
        Assert.Equal(513, error.ActualBytes);
        Assert.Equal(512, error.MaxBytes);
    }

    [Fact]
    public void Format_counts_bytes_not_characters()
    {
        var text = new string('é', 250); // 500 bytes in UTF-8
        var message = new IrcMessage("PRIVMSG", "#k", text);

        Assert.Throws<IrcMessageTooLongException>(() => message.Format());
    }

    [Fact]
    public void Format_refuses_tags_longer_than_the_client_limit()
    {
        var tags = new Dictionary<string, string> { ["k"] = new string('v', 4096) };

        var error = Assert.Throws<IrcMessageTooLongException>(() => new IrcMessage("TAGMSG", ["#k"], tags: tags).Format());
        Assert.Equal("tags", error.Part);
    }

    [Fact]
    public void ToString_falls_back_to_a_readable_form()
    {
        Assert.Equal("PRIVMSG #k hi", new IrcMessage("PRIVMSG", "#k", "hi").ToString());
        Assert.Equal("PRIVMSG a b c", new IrcMessage("PRIVMSG", "a b", "c").ToString());
    }

    [Fact]
    public void Constructor_refuses_an_empty_command()
    {
        Assert.Throws<ArgumentException>(() => new IrcMessage(" "));
    }
}
