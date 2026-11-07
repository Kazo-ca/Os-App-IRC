using System.Text;

namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// Cuts a long text into pieces that fit an IRC line, at a space when it can, never inside a
/// character (the count is in UTF-8 bytes, a surrogate pair stays whole).
/// </summary>
public static class IrcTextSplitter
{
    /// <summary>Cuts <paramref name="text"/> into pieces of at most <paramref name="maxBytes"/> UTF-8 bytes.</summary>
    /// <remarks>A piece cut at a space does not keep the space. Empty text gives no piece.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is lower than 4 (one character may need 4 bytes).</exception>
    public static IReadOnlyList<string> Split(string text, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 4);

        var pieces = new List<string>();
        var rest = text;
        while (rest.Length > 0)
        {
            if (IrcEncoding.ByteCount(rest) <= maxBytes)
            {
                pieces.Add(rest);
                break;
            }

            var cut = LongestPrefix(rest, maxBytes);
            var space = rest.LastIndexOf(' ', cut - 1, cut);
            if (space > 0)
            {
                pieces.Add(rest[..space]);
                rest = rest[(space + 1)..];
            }
            else
            {
                pieces.Add(rest[..cut]);
                rest = rest[cut..];
            }
        }

        return pieces;
    }

    /// <summary>The number of chars of the longest start of <paramref name="text"/> that fits <paramref name="maxBytes"/>.</summary>
    private static int LongestPrefix(string text, int maxBytes)
    {
        var bytes = 0;
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > maxBytes)
            {
                break;
            }

            bytes += size;
            index += rune.Utf16SequenceLength;
        }

        return index;
    }
}
