namespace Kazo.Irc.Text;

/// <summary>A web address found in a message.</summary>
/// <param name="Start">Where it starts in the text.</param>
/// <param name="Length">How many characters it takes.</param>
/// <param name="Uri">The address, absolute, <c>http</c> or <c>https</c>.</param>
public sealed record MessageLink(int Start, int Length, Uri Uri);

/// <summary>
/// Finds the web addresses (<c>http://</c> and <c>https://</c>) in a message (decision 17 of
/// doc/en/plan.md), the way people write them in a sentence: the punctuation that ends the sentence
/// is not part of the address (<c>see https://kazo.ca.</c>), nor a closing bracket that has no
/// opening one inside the address (<c>(https://kazo.ca)</c>), while
/// <c>https://en.wikipedia.org/wiki/IRC_(protocol)</c> keeps its own.
/// </summary>
/// <remarks>
/// Only <c>http</c> and <c>https</c> are links: <c>javascript:</c>, <c>data:</c> or <c>file:</c>
/// never become clickable, nor an address with a user name (<c>https://kazo.ca@evil.example</c>
/// reads like kazo.ca but goes to evil.example). <c>irc://</c> waits for Kazo OS to associate URL schemes with apps
/// (issue #2).
/// </remarks>
public static class MessageLinks
{
    /// <summary>Punctuation that ends a sentence rather than an address.</summary>
    private const string TrailingPunctuation = ".,;:!?'\"*…“”‘’«»";

    /// <summary>Characters an address never holds in a message: they end it.</summary>
    private const string Terminators = "<>\"`{}|\\^";

    private static readonly string[] Schemes = ["https://", "http://"];

    /// <summary>Finds the addresses in <paramref name="text"/>, in order.</summary>
    /// <param name="text">The message, its formatting codes already removed.</param>
    /// <param name="breaks">
    /// Positions in <paramref name="text"/> where a formatting code stood: an address ends there
    /// (<c>\x02https://kazo.ca\x02hello</c> is <c>https://kazo.ca</c>, not <c>https://kazo.cahello</c>).
    /// </param>
    /// <returns>The addresses; empty when there are none.</returns>
    public static IReadOnlyList<MessageLink> Find(string? text, IReadOnlySet<int>? breaks = null)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return [];
        }

        var links = new List<MessageLink>();
        var i = 0;
        while (i < text.Length)
        {
            var start = text.IndexOf("http", i, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                break;
            }

            var scheme = SchemeAt(text, start);
            if (scheme == 0 || (start > 0 && char.IsLetterOrDigit(text[start - 1])))
            {
                i = start + 1;
                continue;
            }

            var end = start + scheme;
            while (end < text.Length && !EndsAddress(text[end]) && !(breaks?.Contains(end) ?? false))
            {
                end++;
            }

            end = TrimEnd(text, start + scheme, end);
            if (end > start + scheme
                && Uri.TryCreate(text[start..end], UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.Host.Length > 0
                && uri.UserInfo.Length == 0)
            {
                links.Add(new MessageLink(start, end - start, uri));
                i = end;
            }
            else
            {
                i = start + scheme;
            }
        }

        return links;
    }

    /// <summary>The length of the scheme at <paramref name="start"/>, or 0 when there is none.</summary>
    private static int SchemeAt(string text, int start)
    {
        foreach (var scheme in Schemes)
        {
            if (string.Compare(text, start, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return scheme.Length;
            }
        }

        return 0;
    }

    private static bool EndsAddress(char c) =>
        char.IsWhiteSpace(c) || char.IsControl(c) || Terminators.Contains(c, StringComparison.Ordinal);

    /// <summary>Gives back the sentence's punctuation and the unbalanced closing brackets.</summary>
    private static int TrimEnd(string text, int bodyStart, int end)
    {
        while (end > bodyStart)
        {
            var last = text[end - 1];
            if (TrailingPunctuation.Contains(last, StringComparison.Ordinal)
                || (last == ')' && Unbalanced(text, bodyStart, end, '(', ')'))
                || (last == ']' && Unbalanced(text, bodyStart, end, '[', ']')))
            {
                end--;
                continue;
            }

            break;
        }

        return end;
    }

    private static bool Unbalanced(string text, int start, int end, char open, char close)
    {
        var depth = 0;
        for (var k = start; k < end; k++)
        {
            if (text[k] == open)
            {
                depth++;
            }
            else if (text[k] == close)
            {
                depth--;
            }
        }

        return depth < 0;
    }
}
