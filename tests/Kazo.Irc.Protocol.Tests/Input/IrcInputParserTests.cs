using Kazo.Irc.Protocol.Input;

namespace Kazo.Irc.Protocol.Tests.Input;

public sealed class IrcInputParserTests
{
    [Fact]
    public void Plain_text_is_said_in_the_open_conversation()
    {
        var command = IrcInputParser.Parse("hello all", "#kazo");

        Assert.Equal(IrcInputCommandKind.Say, command.Kind);
        Assert.Equal("#kazo", command.Target);
        Assert.Equal("hello all", command.Text);
    }

    [Fact]
    public void Double_slash_says_the_text_with_one_slash()
    {
        var command = IrcInputParser.Parse("//join is a command", "#kazo");

        Assert.Equal(IrcInputCommandKind.Say, command.Kind);
        Assert.Equal("/join is a command", command.Text);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("/me waves")]
    public void Text_without_a_conversation_is_refused(string input)
    {
        var command = IrcInputParser.Parse(input, null);

        Assert.Equal(IrcInputCommandKind.Invalid, command.Kind);
        Assert.Equal(IrcInputError.NoConversation, command.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_line_is_refused(string input)
    {
        Assert.Equal(IrcInputError.EmptyInput, IrcInputParser.Parse(input, "#kazo").Error);
    }

    [Fact]
    public void Join_reads_channels_and_keys_and_adds_the_missing_prefix()
    {
        var command = IrcInputParser.Parse("/JOIN #a,b,&c key1,key2", null);

        Assert.Equal(IrcInputCommandKind.Join, command.Kind);
        Assert.Equal("join", command.Name);
        Assert.Equal(["#a", "#b", "&c"], command.Channels);
        Assert.Equal(["key1", "key2"], command.Keys);
    }

    [Fact]
    public void J_is_an_alias_of_join()
    {
        Assert.Equal(IrcInputCommandKind.Join, IrcInputParser.Parse("/j kazo", null).Kind);
    }

    [Theory]
    [InlineData("/part", "#kazo", "#kazo", null)]
    [InlineData("/part bye all", "#kazo", "#kazo", "bye all")]
    [InlineData("/part #other bye", "#kazo", "#other", "bye")]
    [InlineData("/part #other", null, "#other", null)]
    public void Part_uses_the_named_channel_or_the_open_one(string input, string? conversation, string target, string? reason)
    {
        var command = IrcInputParser.Parse(input, conversation);

        Assert.Equal(IrcInputCommandKind.Part, command.Kind);
        Assert.Equal(target, command.Target);
        Assert.Equal(reason, command.Text);
    }

    [Fact]
    public void Part_in_a_private_conversation_is_refused()
    {
        Assert.Equal(IrcInputError.NotAChannel, IrcInputParser.Parse("/part", "trinity").Error);
        Assert.Equal(IrcInputError.NoConversation, IrcInputParser.Parse("/part", null).Error);
    }

    [Fact]
    public void Msg_needs_a_target_and_a_text()
    {
        var command = IrcInputParser.Parse("/msg trinity  wake up", null);

        Assert.Equal(IrcInputCommandKind.Message, command.Kind);
        Assert.Equal("trinity", command.Target);
        Assert.Equal("wake up", command.Text);
        Assert.Equal(IrcInputError.MissingArgument, IrcInputParser.Parse("/msg trinity", null).Error);
    }

    [Fact]
    public void Me_is_an_action_in_the_open_conversation()
    {
        var command = IrcInputParser.Parse("/me waves", "#kazo");

        Assert.Equal(IrcInputCommandKind.Action, command.Kind);
        Assert.Equal("#kazo", command.Target);
        Assert.Equal("waves", command.Text);
        Assert.Equal(IrcInputError.MissingArgument, IrcInputParser.Parse("/me", "#kazo").Error);
    }

    [Theory]
    [InlineData("/nick", IrcInputCommandKind.Nick)]
    [InlineData("/whois", IrcInputCommandKind.Whois)]
    [InlineData("/query", IrcInputCommandKind.Query)]
    [InlineData("/raw", IrcInputCommandKind.Raw)]
    public void Commands_without_their_argument_are_refused(string input, IrcInputCommandKind _)
    {
        Assert.Equal(IrcInputError.MissingArgument, IrcInputParser.Parse(input, "#kazo").Error);
    }

    [Fact]
    public void Nick_and_whois_take_one_word()
    {
        Assert.Equal("morpheus", IrcInputParser.Parse("/nick morpheus extra", null).Target);
        var whois = IrcInputParser.Parse("/whois trinity", null);
        Assert.Equal(IrcInputCommandKind.Whois, whois.Kind);
        Assert.Equal("trinity", whois.Target);
    }

    [Theory]
    [InlineData("/topic", "#kazo", "#kazo", null)]
    [InlineData("/topic New topic here", "#kazo", "#kazo", "New topic here")]
    [InlineData("/topic #other New", "#kazo", "#other", "New")]
    public void Topic_reads_or_sets(string input, string conversation, string target, string? text)
    {
        var command = IrcInputParser.Parse(input, conversation);

        Assert.Equal(IrcInputCommandKind.Topic, command.Kind);
        Assert.Equal(target, command.Target);
        Assert.Equal(text, command.Text);
    }

    [Fact]
    public void Topic_outside_a_channel_is_refused()
    {
        Assert.Equal(IrcInputError.NotAChannel, IrcInputParser.Parse("/topic", "trinity").Error);
        Assert.Equal(IrcInputError.NoConversation, IrcInputParser.Parse("/topic", null).Error);
    }

    [Fact]
    public void Away_and_quit_have_an_optional_message()
    {
        Assert.Equal("brb", IrcInputParser.Parse("/away brb", null).Text);
        Assert.Null(IrcInputParser.Parse("/away", null).Text);
        Assert.Equal(IrcInputCommandKind.Quit, IrcInputParser.Parse("/quit", null).Kind);
        Assert.Equal("bye now", IrcInputParser.Parse("/quit bye now", null).Text);
    }

    [Fact]
    public void Query_opens_with_an_optional_first_message()
    {
        var command = IrcInputParser.Parse("/query trinity hi there", null);

        Assert.Equal(IrcInputCommandKind.Query, command.Kind);
        Assert.Equal("trinity", command.Target);
        Assert.Equal("hi there", command.Text);
        Assert.Null(IrcInputParser.Parse("/query trinity", null).Text);
    }

    [Fact]
    public void Raw_and_quote_send_the_rest_of_the_line()
    {
        Assert.Equal("MODE #kazo +m", IrcInputParser.Parse("/raw MODE #kazo +m", null).Text);
        Assert.Equal(IrcInputCommandKind.Raw, IrcInputParser.Parse("/quote VERSION", null).Kind);
    }

    [Fact]
    public void An_unknown_command_is_refused_with_its_name()
    {
        var command = IrcInputParser.Parse("/dance now", "#kazo");

        Assert.Equal(IrcInputCommandKind.Invalid, command.Kind);
        Assert.Equal(IrcInputError.UnknownCommand, command.Error);
        Assert.Equal("dance", command.Name);
    }

    [Fact]
    public void Channel_types_of_the_server_are_used()
    {
        var command = IrcInputParser.Parse("/join kazo", null, "!");

        Assert.Equal(["!kazo"], command.Channels);
    }
}
