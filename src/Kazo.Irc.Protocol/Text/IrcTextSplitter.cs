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
    /// <remarks>
    /// Walks the UTF-16 text by hand, not with <c>EnumerateRunes</c>: the trimmed Kazo OS runtime that
    /// runs an installed app has no <c>System.Text.Rune</c> (kazopkg refuses the package). A surrogate
    /// pair is one character of 4 UTF-8 bytes and is never cut; a lone surrogate is written as U+FFFD
    /// (3 bytes), as UTF-8 encoding does.
    /// </remarks>
    private static int LongestPrefix(string text, int maxBytes)
    {
        var bytes = 0;
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            var pair = char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
            var size = pair ? 4 : c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
            if (bytes + size > maxBytes)
            {
                break;
            }

            bytes += size;
            index += pair ? 2 : 1;
        }

        return index;
    }
}
