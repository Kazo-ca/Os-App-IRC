using Kazo.Irc.Chat;

namespace Kazo.Irc.Tests.Chat;

/// <summary>Mentions, the default nickname, the input history and the nickname completion.</summary>
public sealed class ChatTextTests
{
    [Theory]
    [InlineData("neo: hi", true)]
    [InlineData("hi Neo!", true)]
    [InlineData("hey @neo, look", true)]
    [InlineData("NEO", true)]
    [InlineData("neon lights", false)]
    [InlineData("neo_ is someone else", false)]
    [InlineData("trinity-neo", false)]
    [InlineData("", false)]
    public void A_mention_is_the_nickname_as_a_whole_word(string text, bool expected)
    {
        Assert.Equal(expected, IrcMentions.Contains(text, "neo"));
    }

    [Fact]
    public void Without_a_nickname_nothing_is_a_mention()
    {
        Assert.False(IrcMentions.Contains("neo", null));
    }

    [Theory]
    [InlineData("neo", "neo")]
    [InlineData("Jean Dupont", "JeanDupont")]
    [InlineData("1337-hax", "hax")]
    [InlineData("élodie", "lodie")]
    [InlineData("a-very-long-user-name-indeed", "a-very-long-user")]
    [InlineData("", "kazo-user")]
    [InlineData("123", "kazo-user")]
    [InlineData(null, "kazo-user")]
    public void The_default_nickname_is_the_user_name_made_valid(string? userName, string expected)
    {
        Assert.Equal(expected, IrcNick.FromUserName(userName));
    }

    [Fact]
    public void The_history_walks_back_and_returns_to_the_draft()
    {
        var history = new InputHistory();
        history.Add("/join #kazo");
        history.Add("hello");
        history.Add("hello"); // not twice in a row

        Assert.Equal(["/join #kazo", "hello"], history.Lines);
        Assert.Equal("hello", history.Previous("draft"));
        Assert.Equal("/join #kazo", history.Previous("hello"));
        Assert.Equal("/join #kazo", history.Previous("/join #kazo"));
        Assert.Equal("hello", history.Next("/join #kazo"));
        Assert.Equal("draft", history.Next("hello"));
        Assert.Equal("draft", history.Next("draft"));
    }

    [Fact]
    public void The_history_keeps_its_capacity()
    {
        var history = new InputHistory();
        for (var i = 0; i < InputHistory.Capacity + 5; i++)
        {
            history.Add("line " + i);
        }

        Assert.Equal(InputHistory.Capacity, history.Lines.Count);
        Assert.Equal("line 5", history.Lines[0]);
    }

    [Fact]
    public void Tab_completes_a_nickname_and_cycles_through_the_matches()
    {
        var completer = new NickCompleter();
        string[] nicks = ["morpheus", "Mouse", "trinity"];

        var first = completer.Complete("mo", nicks);
        Assert.Equal("morpheus: ", first);
        var second = completer.Complete(first, nicks);
        Assert.Equal("Mouse: ", second);
        Assert.Equal("morpheus: ", completer.Complete(second, nicks));
    }

    [Fact]
    public void Tab_in_the_middle_of_a_line_adds_a_space_only()
    {
        var completer = new NickCompleter();

        Assert.Equal("hello trinity ", completer.Complete("hello tri", ["trinity"]));
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("hello ")]
    public void Tab_without_a_match_leaves_the_line(string input)
    {
        Assert.Equal(input, new NickCompleter().Complete(input, ["trinity"]));
    }
}
