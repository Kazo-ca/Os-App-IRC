using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Tests.Text;

/// <summary>The IRC formatting codes (work item 5): read into styles, or removed.</summary>
public sealed class IrcFormattingTests
{
    [Fact]
    public void Text_without_codes_is_one_plain_run()
    {
        var runs = IrcFormatting.Parse("hello world");

        Assert.Equal([new IrcTextRun("hello world", IrcTextStyle.Plain)], runs);
        Assert.False(IrcFormatting.HasCodes("hello world"));
    }

    [Fact]
    public void Empty_text_has_no_run()
    {
        Assert.Empty(IrcFormatting.Parse(string.Empty));
        Assert.Empty(IrcFormatting.Parse("\u0002\u0002"));
    }

    [Fact]
    public void Each_style_code_toggles_its_style()
    {
        var runs = IrcFormatting.Parse("a\u0002b\u001Dc\u001Fd\u001Ee\u0011f\u0016g\u0002h");

        Assert.Equal(
            [
                new IrcTextRun("a", IrcTextStyle.Plain),
                new IrcTextRun("b", new IrcTextStyle(Bold: true)),
                new IrcTextRun("c", new IrcTextStyle(Bold: true, Italic: true)),
                new IrcTextRun("d", new IrcTextStyle(Bold: true, Italic: true, Underline: true)),
                new IrcTextRun("e", new IrcTextStyle(Bold: true, Italic: true, Underline: true, Strikethrough: true)),
                new IrcTextRun("f", new IrcTextStyle(Bold: true, Italic: true, Underline: true, Strikethrough: true, Monospace: true)),
                new IrcTextRun("g", new IrcTextStyle(true, true, true, true, true, true)),
                new IrcTextRun("h", new IrcTextStyle(false, true, true, true, true, true)),
            ],
            runs);
    }

    [Fact]
    public void Reset_ends_every_style_and_color()
    {
        var runs = IrcFormatting.Parse("\u0002\u001D\u000304,02x\u000Fy");

        Assert.Equal(new IrcTextStyle(Bold: true, Italic: true, Foreground: 4, Background: 2), runs[0].Style);
        Assert.Equal(new IrcTextRun("y", IrcTextStyle.Plain), runs[1]);
    }

    [Theory]
    [InlineData("\u00034red", 4, null, "red")]
    [InlineData("\u000304red", 4, null, "red")]
    [InlineData("\u000312,1blue", 12, 1, "blue")]
    [InlineData("\u000304,15x", 4, 15, "x")]
    [InlineData("\u0003123", 12, null, "3")]
    [InlineData("\u00034,x", 4, null, ",x")]
    [InlineData("\u000399,99plain", null, null, "plain")]
    [InlineData("\u000342,50x", 42, 50, "x")]
    public void A_color_has_one_or_two_digits_and_an_optional_background(string text, int? foreground, int? background, string rest)
    {
        var run = Assert.Single(IrcFormatting.Parse(text));

        Assert.Equal(foreground, run.Style.Foreground);
        Assert.Equal(background, run.Style.Background);
        Assert.Equal(rest, run.Text);
    }

    [Fact]
    public void A_color_code_without_digits_resets_the_colors()
    {
        var runs = IrcFormatting.Parse("\u000304,02a\u0003b");

        Assert.Equal(new IrcTextRun("b", IrcTextStyle.Plain), runs[1]);
    }

    [Fact]
    public void A_new_text_color_keeps_the_background()
    {
        var runs = IrcFormatting.Parse("\u000304,02a\u000308b");

        Assert.Equal(new IrcTextStyle(Foreground: 8, Background: 2), runs[1].Style);
    }

    [Theory]
    [InlineData("\u0004FF0000red", "red")]
    [InlineData("\u0004ff0000,00ff00x", "x")]
    [InlineData("\u0004FF00zz", "FF00zz")]
    [InlineData("\u0004", "")]
    public void A_hexadecimal_color_is_read_and_dropped(string text, string rest)
    {
        Assert.Equal(rest, IrcFormatting.Strip(text));
        Assert.All(IrcFormatting.Parse(text), run => Assert.True(run.Style.IsPlain));
    }

    [Fact]
    public void Strip_keeps_the_text_only()
    {
        Assert.Equal("Links: red and italics", IrcFormatting.Strip("\u0002Links\u0002: \u000304,01red\u0003 and \u001Ditalics\u001D"));
    }

    [Fact]
    public void Strip_returns_the_same_text_when_there_is_no_code()
    {
        const string text = "plain";

        Assert.Same(text, IrcFormatting.Strip(text));
    }

    [Fact]
    public void Two_codes_that_cancel_each_other_do_not_split_the_text()
    {
        Assert.Single(IrcFormatting.Parse("ab\u0002\u0002cd"));
    }
}
