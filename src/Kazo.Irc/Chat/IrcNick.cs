using System.Text;

namespace Kazo.Irc.Chat;

/// <summary>The default nickname (decision 12): the OS user name, made a valid nickname.</summary>
public static class IrcNick
{
    /// <summary>The longest default nickname; most networks accept at least 16.</summary>
    public const int MaxLength = 16;

    /// <summary>The nickname used when the user name has nothing usable.</summary>
    public const string Fallback = "kazo-user";

    /// <summary>
    /// Keeps the characters a nickname may hold, starts with a letter or a symbol (not a digit or a
    /// dash) and cuts at <see cref="MaxLength"/>.
    /// </summary>
    /// <param name="userName">The OS user name, for example <c>neo</c>.</param>
    /// <returns>A valid nickname.</returns>
    public static string FromUserName(string? userName)
    {
        var builder = new StringBuilder();
        foreach (var c in userName ?? string.Empty)
        {
            if (char.IsAscii(c) && IrcMentions.IsNickChar(c))
            {
                builder.Append(c);
            }
        }

        while (builder.Length > 0 && (char.IsAsciiDigit(builder[0]) || builder[0] == '-'))
        {
            builder.Remove(0, 1);
        }

        var nick = builder.Length > MaxLength ? builder.ToString(0, MaxLength) : builder.ToString();
        return nick.Length == 0 ? Fallback : nick;
    }
}
