using Kazo.Irc.Text;

namespace Kazo.Irc.Tests.Text;

/// <summary>The web addresses of a message (work item 5, decision 17).</summary>
public sealed class MessageLinksTests
{
    [Theory]
    [InlineData("see https://kazo.ca", "https://kazo.ca")]
    [InlineData("https://kazo.ca/docs?a=1&b=2#top", "https://kazo.ca/docs?a=1&b=2#top")]
    [InlineData("HTTP://KAZO.CA/X", "HTTP://KAZO.CA/X")]
    [InlineData("go to http://docs.hack/guide.", "http://docs.hack/guide")]
    [InlineData("really? https://kazo.ca!", "https://kazo.ca")]
    [InlineData("list: https://kazo.ca, https://os.kazo.ca;", "https://kazo.ca")]
    [InlineData("(https://kazo.ca)", "https://kazo.ca")]
    [InlineData("(see https://kazo.ca/a).", "https://kazo.ca/a")]
    [InlineData("https://en.wikipedia.org/wiki/IRC_(protocol)", "https://en.wikipedia.org/wiki/IRC_(protocol)")]
    [InlineData("(https://en.wikipedia.org/wiki/IRC_(protocol))", "https://en.wikipedia.org/wiki/IRC_(protocol)")]
    [InlineData("[https://kazo.ca]", "https://kazo.ca")]
    [InlineData("<https://kazo.ca>", "https://kazo.ca")]
    [InlineData("\"https://kazo.ca\"", "https://kazo.ca")]
    [InlineData("«https://kazo.ca»", "https://kazo.ca")]
    [InlineData("https://kazo.ca...", "https://kazo.ca")]
    [InlineData("https://kazo.ca/\"><script>alert(1)</script>", "https://kazo.ca/")]
    [InlineData("https://例え.テスト/パス", "https://例え.テスト/パス")]
    [InlineData("port https://localhost:8080/x", "https://localhost:8080/x")]
    public void An_address_ends_where_the_sentence_takes_over(string text, string expected)
    {
        var link = MessageLinks.Find(text)[0];

        Assert.Equal(expected, text.Substring(link.Start, link.Length));
        Assert.True(link.Uri.IsAbsoluteUri);
    }

    [Fact]
    public void Several_addresses_are_found_in_order()
    {
        const string text = "a https://kazo.ca b http://docs.hack/ c";

        var links = MessageLinks.Find(text);

        Assert.Equal(["https://kazo.ca/", "http://docs.hack/"], links.Select(link => link.Uri.AbsoluteUri));
        Assert.Equal([2, 20], links.Select(link => link.Start));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no address here")]
    [InlineData("https://")]
    [InlineData("https://.")]
    [InlineData("http:// spaced")]
    [InlineData("xhttps://kazo.ca")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://kazo.ca")]
    [InlineData("irc://irc.libera.chat/#kazo")]
    [InlineData("www.kazo.ca")]
    [InlineData("http://")]
    public void Only_http_and_https_addresses_are_links(string? text)
    {
        Assert.Empty(MessageLinks.Find(text));
    }

    [Fact]
    public void An_address_cannot_start_inside_a_word_but_can_after_punctuation()
    {
        Assert.Single(MessageLinks.Find("link:https://kazo.ca"));
        Assert.Single(MessageLinks.Find("(https://kazo.ca"));
    }
}
