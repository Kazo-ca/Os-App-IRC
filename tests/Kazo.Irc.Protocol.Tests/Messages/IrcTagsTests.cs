using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Tests.Messages;

public sealed class IrcTagsTests
{
    [Theory]
    [InlineData(@"a\:b", "a;b")]
    [InlineData(@"a\sb", "a b")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"a\rb\n", "a\rb\n")]
    [InlineData(@"a\bc", "abc")]
    [InlineData(@"trailing\", "trailing")]
    [InlineData("plain", "plain")]
    public void Unescape_follows_the_specification(string raw, string expected)
    {
        Assert.Equal(expected, IrcTags.Unescape(raw));
    }

    [Fact]
    public void Escape_is_the_inverse_of_unescape()
    {
        const string value = "a; b\\c\r\nd";

        Assert.Equal(@"a\:\sb\\c\r\nd", IrcTags.Escape(value));
        Assert.Equal(value, IrcTags.Unescape(IrcTags.Escape(value)));
    }

    [Fact]
    public void Parse_reads_values_flags_and_duplicates()
    {
        var tags = IrcTags.Parse("a=1;flag;empty=;a=2;;=ignored");

        Assert.Equal("2", tags["a"]);
        Assert.Equal("", tags["flag"]);
        Assert.Equal("", tags["empty"]);
        Assert.Equal(3, tags.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad key")]
    [InlineData("bad;key")]
    public void Format_refuses_an_invalid_key(string key)
    {
        Assert.Throws<IrcProtocolException>(() => IrcTags.Format(new Dictionary<string, string> { [key] = "v" }));
    }
}
