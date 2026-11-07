using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Tests.Messages;

public sealed class IrcSourceTests
{
    [Theory]
    [InlineData("neo!~neo@kazo.ca", "neo", "~neo", "kazo.ca")]
    [InlineData("neo@kazo.ca", "neo", null, "kazo.ca")]
    [InlineData("neo", "neo", null, null)]
    [InlineData("irc.kazo.ca", "irc.kazo.ca", null, null)]
    public void Parse_reads_each_form(string raw, string name, string? user, string? host)
    {
        var source = IrcSource.Parse(raw);

        Assert.Equal(name, source.Name);
        Assert.Equal(name, source.Nick);
        Assert.Equal(user, source.User);
        Assert.Equal(host, source.Host);
        Assert.Equal(raw, source.ToString());
    }

    [Fact]
    public void IsServer_is_true_only_for_a_dotted_name_without_user_or_host()
    {
        Assert.True(IrcSource.Parse("irc.kazo.ca").IsServer);
        Assert.False(IrcSource.Parse("neo").IsServer);
        Assert.False(IrcSource.Parse("neo!u@irc.kazo.ca").IsServer);
    }

    [Fact]
    public void Parse_refuses_an_empty_source()
    {
        Assert.Throws<ArgumentException>(() => IrcSource.Parse(""));
    }
}
