using Kazo.Irc.Protocol.Model;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Tests.Model;

public sealed class IrcServerSupportTests
{
    [Fact]
    public void Defaults_follow_the_rfc()
    {
        var support = new IrcServerSupport();

        Assert.Equal("ov", support.PrefixModes);
        Assert.Equal("@+", support.PrefixSymbols);
        Assert.Equal("#&", support.ChannelTypes);
        Assert.Equal(IrcCaseMapping.Rfc1459, support.CaseMapping);
        Assert.Equal("beI", support.ListModes);
        Assert.Equal("k", support.AlwaysParameterModes);
        Assert.Equal("l", support.SetParameterModes);
        Assert.Equal("imnpst", support.FlagModes);
        Assert.Null(support.NetworkName);
        Assert.Null(support.NickLength);
    }

    [Fact]
    public void Apply_reads_the_tokens()
    {
        var support = new IrcServerSupport();

        support.Apply(["neo", "PREFIX=(qaohv)~&@%+", "CHANTYPES=#", "CASEMAPPING=ascii", "NETWORK=Kazo\\x20Net",
            "CHANMODES=eIbq,k,flj,CFLMPQScgimnprstuz", "NICKLEN=16", "EXCEPTS", "are supported by this server"]);

        Assert.Equal("qaohv", support.PrefixModes);
        Assert.Equal("~&@%+", support.PrefixSymbols);
        Assert.Equal("#", support.ChannelTypes);
        Assert.Equal(IrcCaseMapping.Ascii, support.CaseMapping);
        Assert.Equal("Kazo Net", support.NetworkName);
        Assert.Equal("eIbq", support.ListModes);
        Assert.Equal("flj", support.SetParameterModes);
        Assert.Equal(16, support.NickLength);
        Assert.Equal("", support.Tokens["EXCEPTS"]);
        Assert.True(support.IsChannel("#kazo"));
        Assert.False(support.IsChannel("&kazo"));
        Assert.False(support.IsChannel(""));
    }

    [Fact]
    public void Apply_removes_a_token_with_a_minus()
    {
        var support = new IrcServerSupport();
        support.Apply(["neo", "PREFIX=(qo)~@", "NETWORK=K", "text"]);

        support.Apply(["neo", "-PREFIX", "-NETWORK", "text"]);

        Assert.Equal("ov", support.PrefixModes);
        Assert.Null(support.NetworkName);
    }

    [Fact]
    public void Apply_reads_an_empty_prefix_and_ignores_a_broken_one()
    {
        var support = new IrcServerSupport();
        support.Apply(["neo", "PREFIX=(ov)@", "text"]);
        Assert.Equal("ov", support.PrefixModes);

        support.Apply(["neo", "PREFIX=", "text"]);
        Assert.Equal("", support.PrefixModes);

        support.Reset();
        Assert.Equal("@+", support.PrefixSymbols);
    }

    [Fact]
    public void Modes_and_symbols_map_both_ways_in_rank_order()
    {
        var support = new IrcServerSupport();
        support.Apply(["neo", "PREFIX=(qaohv)~&@%+", "text"]);

        Assert.Equal('@', support.SymbolForMode('o'));
        Assert.Null(support.SymbolForMode('x'));
        Assert.Equal('h', support.ModeForSymbol('%'));
        Assert.Null(support.ModeForSymbol('!'));
        Assert.Equal("ohv", support.SortPrefixModes("vxho"));
        Assert.Equal("@+", support.SymbolsForModes("vo"));
    }
}
