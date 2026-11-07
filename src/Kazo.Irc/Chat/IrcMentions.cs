namespace Kazo.Irc.Chat;

/// <summary>Whether a message names the user (decision 14).</summary>
public static class IrcMentions
{
    private const string NickSymbols = "-_[]\\^{}|`";

    /// <summary>
    /// True when <paramref name="text"/> contains <paramref name="nick"/> as a whole word, in any
    /// case: <c>neo: hi</c>, <c>hi Neo!</c>; not <c>neon</c> nor <c>neo_</c>.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="nick">The user's current nickname.</param>
    /// <returns>True for a mention.</returns>
    public static bool Contains(string? text, string? nick)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(nick))
        {
            return false;
        }

        var index = text.IndexOf(nick, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + nick.Length;
            if ((index == 0 || !IsNickChar(text[index - 1])) && (end == text.Length || !IsNickChar(text[end])))
            {
                return true;
            }

            index = text.IndexOf(nick, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>True for a character a nickname may hold (RFC 2812, plus the dash).</summary>
    /// <param name="c">The character.</param>
    /// <returns>True for a letter, a digit or one of <c>-_[]\^{}|`</c>.</returns>
    public static bool IsNickChar(char c) => char.IsLetterOrDigit(c) || NickSymbols.Contains(c, StringComparison.Ordinal);
}
