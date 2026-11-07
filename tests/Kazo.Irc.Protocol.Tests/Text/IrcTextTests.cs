using System.Text;
using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Tests.Text;

public sealed class IrcEncodingTests
{
    [Fact]
    public void Decode_reads_valid_utf8()
    {
        Assert.Equal("Ça marche ✓", IrcEncoding.Decode(Encoding.UTF8.GetBytes("Ça marche ✓")));
    }

    [Fact]
    public void Decode_falls_back_to_latin1_for_invalid_utf8()
    {
        var latin1 = Encoding.Latin1.GetBytes("Ça été");

        Assert.Equal("Ça été", IrcEncoding.Decode(latin1));
    }

    [Fact]
    public void Encode_writes_utf8_without_bom()
    {
        Assert.Equal([0xC3, 0xA9], IrcEncoding.Encode("é"));
        Assert.Equal(2, IrcEncoding.ByteCount("é"));
    }
}

public sealed class IrcLineBufferTests
{
    [Fact]
    public void Append_cuts_lines_on_lf_and_drops_cr()
    {
        var buffer = new IrcLineBuffer();

        var first = buffer.Append(Encoding.UTF8.GetBytes("PING a\r\nPI"));
        var second = buffer.Append(Encoding.UTF8.GetBytes("NG b\n\r\n"));

        Assert.Equal(["PING a"], first);
        Assert.Equal(["PING b"], second);
        Assert.Equal(0, buffer.PendingBytes);
    }

    [Fact]
    public void Append_decodes_each_line_on_its_own()
    {
        var buffer = new IrcLineBuffer();
        var bytes = Encoding.Latin1.GetBytes("été\r\n").Concat(Encoding.UTF8.GetBytes("été\r\n")).ToArray();

        Assert.Equal(["été", "été"], buffer.Append(bytes));
    }

    [Fact]
    public void Append_keeps_a_multibyte_character_split_between_reads()
    {
        var buffer = new IrcLineBuffer();
        var bytes = Encoding.UTF8.GetBytes("é\n");

        Assert.Empty(buffer.Append(bytes.AsSpan(0, 1)));
        Assert.Equal(["é"], buffer.Append(bytes.AsSpan(1)));
    }

    [Fact]
    public void Append_refuses_an_endless_line()
    {
        var buffer = new IrcLineBuffer();

        Assert.Throws<IrcProtocolException>(() => buffer.Append(new byte[IrcLimits.MaxReadLineBytes + 1]));
        Assert.Equal(0, buffer.PendingBytes);
    }
}

public sealed class IrcTextSplitterTests
{
    [Fact]
    public void Split_keeps_a_short_text_whole()
    {
        Assert.Equal(["hello"], IrcTextSplitter.Split("hello", 10));
        Assert.Empty(IrcTextSplitter.Split("", 10));
    }

    [Fact]
    public void Split_cuts_at_the_last_space()
    {
        Assert.Equal(["one two", "three"], IrcTextSplitter.Split("one two three", 9));
    }

    [Fact]
    public void Split_cuts_a_long_word_at_the_limit()
    {
        Assert.Equal(["abcd", "efgh", "ij"], IrcTextSplitter.Split("abcdefghij", 4));
    }

    [Fact]
    public void Split_never_cuts_inside_a_character()
    {
        var pieces = IrcTextSplitter.Split("éééé😀😀", 5);

        Assert.All(pieces, p => Assert.True(Encoding.UTF8.GetByteCount(p) <= 5));
        Assert.Equal("éééé😀😀", string.Concat(pieces));
        Assert.Equal(["éé", "éé", "😀", "😀"], pieces);
    }

    [Fact]
    public void Split_refuses_a_limit_below_one_character()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IrcTextSplitter.Split("abc", 3));
    }
}

public sealed class IrcCtcpTests
{
    [Fact]
    public void TryParse_reads_an_action()
    {
        Assert.True(IrcCtcp.TryParse("\u0001ACTION waves\u0001", out var command, out var arguments));
        Assert.Equal("ACTION", command);
        Assert.Equal("waves", arguments);
    }

    [Fact]
    public void TryParse_accepts_a_missing_closing_delimiter_and_no_arguments()
    {
        Assert.True(IrcCtcp.TryParse("\u0001version", out var command, out var arguments));
        Assert.Equal("VERSION", command);
        Assert.Equal("", arguments);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("\u0001")]
    [InlineData("\u0001\u0001")]
    public void TryParse_refuses_a_plain_text(string text)
    {
        Assert.False(IrcCtcp.TryParse(text, out _, out _));
    }

    [Fact]
    public void Format_writes_with_or_without_arguments()
    {
        Assert.Equal("\u0001VERSION\u0001", IrcCtcp.Format("VERSION"));
        Assert.Equal("\u0001PING 42\u0001", IrcCtcp.Format("PING", "42"));
    }
}

public sealed class IrcCaseFoldingTests
{
    [Theory]
    [InlineData("Neo[]\\~", IrcCaseMapping.Rfc1459, "neo{}|^")]
    [InlineData("Neo[]\\~", IrcCaseMapping.StrictRfc1459, "neo{}|~")]
    [InlineData("Neo[]\\~", IrcCaseMapping.Ascii, "neo[]\\~")]
    [InlineData("ÉTÉ", IrcCaseMapping.Ascii, "ÉtÉ")]
    public void Fold_follows_the_mapping(string name, IrcCaseMapping mapping, string expected)
    {
        Assert.Equal(expected, IrcCaseFolding.Fold(name, mapping));
    }

    [Theory]
    [InlineData("ascii", IrcCaseMapping.Ascii)]
    [InlineData("STRICT-RFC1459", IrcCaseMapping.StrictRfc1459)]
    [InlineData("rfc1459", IrcCaseMapping.Rfc1459)]
    [InlineData("rfc7613", IrcCaseMapping.Rfc1459)]
    [InlineData(null, IrcCaseMapping.Rfc1459)]
    public void ParseMapping_reads_the_isupport_value(string? value, IrcCaseMapping expected)
    {
        Assert.Equal(expected, IrcCaseFolding.ParseMapping(value));
    }

    [Fact]
    public void AreEqual_compares_folded_names()
    {
        Assert.True(IrcCaseFolding.AreEqual("Neo[", "neo{", IrcCaseMapping.Rfc1459));
        Assert.False(IrcCaseFolding.AreEqual("Neo[", "neo{", IrcCaseMapping.Ascii));
        Assert.True(IrcCaseFolding.AreEqual(null, null, IrcCaseMapping.Ascii));
        Assert.False(IrcCaseFolding.AreEqual("a", null, IrcCaseMapping.Ascii));
    }
}
