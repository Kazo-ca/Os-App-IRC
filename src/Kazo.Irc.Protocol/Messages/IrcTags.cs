using System.Text;

namespace Kazo.Irc.Protocol.Messages;

/// <summary>
/// Escaping of IRCv3 tag values (https://ircv3.net/specs/extensions/message-tags):
/// <c>;</c> is <c>\:</c>, a space is <c>\s</c>, <c>\</c> is <c>\\</c>, CR is <c>\r</c>, LF is <c>\n</c>.
/// </summary>
public static class IrcTags
{
    /// <summary>The tag that carries the time the server saw the message (IRCv3 server-time).</summary>
    public const string ServerTime = "time";

    /// <summary>The tag that carries the account of the sender (IRCv3 account-tag).</summary>
    public const string Account = "account";

    /// <summary>The tag that carries the id of the message (IRCv3 message-ids).</summary>
    public const string MessageId = "msgid";

    /// <summary>Reads an escaped tag value.</summary>
    /// <remarks>
    /// An unknown escape <c>\x</c> gives <c>x</c>, and a lone <c>\</c> at the end is dropped, as the
    /// specification asks.
    /// </remarks>
    public static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 >= value.Length)
            {
                break;
            }

            i++;
            builder.Append(value[i] switch
            {
                ':' => ';',
                's' => ' ',
                'r' => '\r',
                'n' => '\n',
                var other => other,
            });
        }

        return builder.ToString();
    }

    /// <summary>Writes a tag value with its escapes.</summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                ';' => "\\:",
                ' ' => "\\s",
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>Reads the tag part of a line, without its leading <c>@</c>.</summary>
    /// <remarks>A tag without a value, or with an empty one, has the empty string; when a key comes twice, the last one wins.</remarks>
    public static IReadOnlyDictionary<string, string> Parse(string raw)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = item.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                tags[item] = string.Empty;
            }
            else if (equals > 0)
            {
                tags[item[..equals]] = Unescape(item[(equals + 1)..]);
            }
        }

        return tags;
    }

    /// <summary>Writes the tags, without the leading <c>@</c>.</summary>
    /// <exception cref="IrcProtocolException">A key is empty or holds a character a key cannot hold.</exception>
    public static string Format(IReadOnlyDictionary<string, string> tags)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in tags)
        {
            ValidateKey(key);
            if (builder.Length > 0)
            {
                builder.Append(';');
            }

            builder.Append(key);
            if (value.Length > 0)
            {
                builder.Append('=').Append(Escape(value));
            }
        }

        return builder.ToString();
    }

    private static void ValidateKey(string key)
    {
        if (key.Length == 0)
        {
            throw new IrcProtocolException("A tag key cannot be empty.");
        }

        foreach (var c in key)
        {
            var valid = char.IsAsciiLetterOrDigit(c) || c is '-' or '/' or '.' or '+';
            if (!valid)
            {
                throw new IrcProtocolException($"The tag key '{key}' holds the character '{c}'.");
            }
        }
    }
}
