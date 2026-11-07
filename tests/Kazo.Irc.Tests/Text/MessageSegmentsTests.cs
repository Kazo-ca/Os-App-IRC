using Kazo.Irc.Components;
using Kazo.Irc.Demo;
using Kazo.Irc.Protocol.Text;
using Kazo.Irc.Text;

namespace Kazo.Irc.Tests.Text;

/// <summary>What the window draws for a message (work item 5): styles, links, and text that stays text.</summary>
public sealed class MessageSegmentsTests
{
    [Fact]
    public void A_message_without_link_is_one_segment_of_text()
    {
        var segment = Assert.Single(MessageSegments.Build("hello"));

        Assert.Null(segment.Link);
        Assert.Equal("hello", segment.Text);
    }

    [Fact]
    public void Empty_text_has_no_segment()
    {
        Assert.Empty(MessageSegments.Build(null));
        Assert.Empty(MessageSegments.Build(string.Empty));
        Assert.Empty(MessageSegments.Build("\u0002\u000F"));
    }

    [Fact]
    public void A_link_is_cut_out_of_the_text_around_it()
    {
        var segments = MessageSegments.Build("see https://kazo.ca/docs.");

        Assert.Equal(["see ", "https://kazo.ca/docs", "."], segments.Select(segment => segment.Text));
        Assert.Equal([null, "https://kazo.ca/docs", null], segments.Select(segment => segment.Link?.AbsoluteUri));
    }

    [Fact]
    public void A_link_whose_style_changes_midway_stays_one_link()
    {
        var segments = MessageSegments.Build("\u0002https://kazo\u0002.ca/x end");

        var link = segments[0];
        Assert.Equal("https://kazo.ca/x", link.Link?.AbsoluteUri);
        Assert.Equal([new IrcTextRun("https://kazo", new IrcTextStyle(Bold: true)), new IrcTextRun(".ca/x", IrcTextStyle.Plain)], link.Runs);
        Assert.Equal(" end", segments[1].Text);
        Assert.Null(segments[1].Link);
    }

    [Fact]
    public void Formatting_codes_never_reach_the_text()
    {
        var segments = MessageSegments.Build(DemoIrcServer.LinksMessage);

        Assert.All(segments, segment => Assert.False(IrcFormatting.HasCodes(segment.Text)));
        Assert.Equal(["http://docs.hack/guide?topic=irc", "https://kazo.ca/"], segments.Where(s => s.Link is not null).Select(s => s.Link!.AbsoluteUri));
    }

    [Fact]
    public void Markup_from_the_network_stays_text_and_never_a_link()
    {
        var segments = MessageSegments.Build("<b>bold?</b> <a href=\"javascript:alert(1)\">x</a> <img src=x onerror=alert(1)>");

        var segment = Assert.Single(segments);
        Assert.Null(segment.Link);
        Assert.Equal("<b>bold?</b> <a href=\"javascript:alert(1)\">x</a> <img src=x onerror=alert(1)>", segment.Text);
    }

    [Fact]
    public void An_address_with_a_quote_cannot_break_out_of_its_link()
    {
        var link = MessageSegments.Build("https://kazo.ca/a\"onmouseover=\"alert(1)").Single(segment => segment.Link is not null);

        Assert.Equal("https://kazo.ca/a", link.Link!.AbsoluteUri);
    }

    [Fact]
    public void The_href_is_the_escaped_absolute_address()
    {
        var link = MessageSegments.Build("https://kazo.ca/a b").Single(segment => segment.Link is not null).Link!;

        Assert.Equal("https://kazo.ca/a", link.AbsoluteUri);
        Assert.DoesNotContain(MessageSegments.Build("https://kazo.ca/é").Single().Link!.AbsoluteUri, c => c > 127);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, null, null, "")]
    [InlineData(true, false, false, false, false, false, null, null, "irc-b")]
    [InlineData(true, true, true, true, true, false, null, null, "irc-b irc-i irc-u irc-s irc-m")]
    [InlineData(false, false, false, false, false, false, 4, 1, "irc-fg-4 irc-bg-1")]
    [InlineData(false, false, false, false, false, false, 42, 50, "")]
    [InlineData(false, false, false, false, false, true, null, null, "irc-fg-reverse irc-bg-reverse")]
    [InlineData(false, false, false, false, false, true, 4, 1, "irc-fg-1 irc-bg-4")]
    [InlineData(false, false, false, false, false, true, 4, null, "irc-fg-reverse irc-bg-4")]
    public void A_style_becomes_css_classes(bool bold, bool italic, bool underline, bool strike, bool mono, bool reverse, int? fg, int? bg, string expected)
    {
        Assert.Equal(expected, MessageText.ClassOf(new IrcTextStyle(bold, italic, underline, strike, mono, reverse, fg, bg)));
    }
}
