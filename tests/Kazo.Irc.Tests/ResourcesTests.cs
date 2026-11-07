using System.Xml.Linq;

namespace Kazo.Irc.Tests;

/// <summary>The app's texts exist in English and in French (Kazo OS ADR 0104).</summary>
public sealed class ResourcesTests
{
    [Fact]
    public void Every_English_text_has_a_French_translation_and_the_reverse()
    {
        string[] english = Keys("Strings.resx");
        string[] french = Keys("Strings.fr.resx");
        Assert.NotEmpty(english);
        Assert.Equal(english, french);
    }

    [Fact]
    public void A_text_with_a_placeholder_keeps_it_in_both_languages()
    {
        Dictionary<string, string> english = Values("Strings.resx");
        Dictionary<string, string> french = Values("Strings.fr.resx");
        foreach ((string key, string text) in english)
        {
            Assert.Equal(text.Contains("{0}", StringComparison.Ordinal), french[key].Contains("{0}", StringComparison.Ordinal));
        }
    }

    private static string[] Keys(string file) => [.. Values(file).Keys.Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> Values(string file) =>
        XDocument.Load(RepositoryRoot.App("Resources", file)).Root!
            .Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);
}
