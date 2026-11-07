using System.Text;

namespace Kazo.Irc.Protocol.Messages;

/// <summary>
/// One IRC message: IRCv3 tags, an optional source, a command (a word or a three-digit numeric)
/// and its parameters (RFC 1459 §2.3.1, RFC 2812 §2.3.1, IRCv3 message-tags).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Parse"/> is lenient (extra spaces, long lines, a missing source are accepted);
/// <see cref="Format"/> is strict and refuses what a server would refuse: CR, LF or NUL inside a
/// parameter, an empty middle parameter, a middle parameter with a space or a leading colon, and a
/// line longer than <see cref="IrcLimits"/>.
/// </para>
/// <para>The command is kept in upper case, so comparisons can be ordinal.</para>
/// </remarks>
public sealed class IrcMessage
{
    private static readonly IReadOnlyDictionary<string, string> NoTags =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Creates a message.</summary>
    /// <exception cref="ArgumentException">The command is empty.</exception>
    public IrcMessage(
        string command,
        IReadOnlyList<string>? parameters = null,
        IrcSource? source = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        Command = command.ToUpperInvariant();
        Parameters = parameters ?? [];
        Source = source;
        Tags = tags ?? NoTags;
    }

    /// <summary>Creates a message from its command and parameters.</summary>
    public IrcMessage(string command, params string[] parameters)
        : this(command, (IReadOnlyList<string>)parameters)
    {
    }

    /// <summary>IRCv3 tags, values already unescaped.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>The source, or null when the line has none (every line a client sends).</summary>
    public IrcSource? Source { get; }

    /// <summary>The command in upper case: <c>PRIVMSG</c>, <c>001</c>...</summary>
    public string Command { get; }

    /// <summary>The parameters, the trailing one included without its colon.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>True when the command is a three-digit numeric reply.</summary>
    public bool IsNumeric => Command.Length == 3 && Command.All(char.IsAsciiDigit);

    /// <summary>The parameter at <paramref name="index"/>, or null when there is none.</summary>
    public string? GetParameter(int index) => index >= 0 && index < Parameters.Count ? Parameters[index] : null;

    /// <summary>The value of a tag, or null when the message does not carry it.</summary>
    public string? GetTag(string key) => Tags.TryGetValue(key, out var value) ? value : null;

    /// <summary>Reads one line (with or without its CR LF).</summary>
    /// <exception cref="IrcProtocolException">The line holds no command.</exception>
    public static IrcMessage Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var span = line.AsSpan().TrimEnd("\r\n");
        var position = 0;

        IReadOnlyDictionary<string, string>? tags = null;
        if (position < span.Length && span[position] == '@')
        {
            var raw = NextToken(span, ref position);
            tags = IrcTags.Parse(raw[1..].ToString());
        }

        SkipSpaces(span, ref position);
        IrcSource? source = null;
        if (position < span.Length && span[position] == ':')
        {
            var raw = NextToken(span, ref position);
            if (raw.Length > 1)
            {
                source = IrcSource.Parse(raw[1..].ToString());
            }
        }

        SkipSpaces(span, ref position);
        var command = NextToken(span, ref position);
        if (command.IsEmpty)
        {
            throw new IrcProtocolException($"The IRC line '{line}' holds no command.");
        }

        var parameters = new List<string>();
        SkipSpaces(span, ref position);
        while (position < span.Length)
        {
            if (span[position] == ':')
            {
                parameters.Add(span[(position + 1)..].ToString());
                break;
            }

            parameters.Add(NextToken(span, ref position).ToString());
            SkipSpaces(span, ref position);
        }

        return new IrcMessage(command.ToString(), parameters, source, tags);
    }

    /// <summary>Reads one line, or returns false when it holds no message.</summary>
    public static bool TryParse(string line, out IrcMessage? message)
    {
        try
        {
            message = Parse(line);
            return true;
        }
        catch (IrcProtocolException)
        {
            message = null;
            return false;
        }
    }

    /// <summary>Writes the message as one line, without CR LF, checking every IRC rule and limit.</summary>
    /// <exception cref="IrcProtocolException">A parameter cannot be written or the line is too long.</exception>
    public string Format()
    {
        var tagPart = string.Empty;
        if (Tags.Count > 0)
        {
            tagPart = "@" + IrcTags.Format(Tags) + " ";
            var tagBytes = Encoding.UTF8.GetByteCount(tagPart);
            if (tagBytes > IrcLimits.MaxClientTagBytes)
            {
                throw new IrcMessageTooLongException("tags", tagBytes, IrcLimits.MaxClientTagBytes);
            }
        }

        var body = FormatBody();
        var bodyBytes = Encoding.UTF8.GetByteCount(body) + IrcLimits.LineEndingBytes;
        if (bodyBytes > IrcLimits.MaxLineBytes)
        {
            throw new IrcMessageTooLongException("message", bodyBytes, IrcLimits.MaxLineBytes);
        }

        return tagPart + body;
    }

    /// <summary>The line as <see cref="Format"/> writes it, or a readable form when it cannot be written.</summary>
    public override string ToString()
    {
        try
        {
            return Format();
        }
        catch (IrcProtocolException)
        {
            return Command + " " + string.Join(' ', Parameters);
        }
    }

    private string FormatBody()
    {
        var builder = new StringBuilder();
        if (Source is not null)
        {
            builder.Append(':').Append(Source).Append(' ');
        }

        builder.Append(Command);
        for (var i = 0; i < Parameters.Count; i++)
        {
            var parameter = Parameters[i];
            if (parameter.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
            {
                throw new IrcProtocolException($"The parameter {i + 1} of {Command} holds CR, LF or NUL.");
            }

            var isLast = i == Parameters.Count - 1;
            var needsColon = parameter.Length == 0 || parameter[0] == ':' || parameter.Contains(' ', StringComparison.Ordinal);
            if (needsColon && !isLast)
            {
                throw new IrcProtocolException($"The parameter {i + 1} of {Command} is empty, holds a space or starts with ':' but is not the last one.");
            }

            builder.Append(' ');
            if (needsColon)
            {
                builder.Append(':');
            }

            builder.Append(parameter);
        }

        return builder.ToString();
    }

    private static ReadOnlySpan<char> NextToken(ReadOnlySpan<char> span, ref int position)
    {
        var start = position;
        while (position < span.Length && span[position] != ' ')
        {
            position++;
        }

        return span[start..position];
    }

    private static void SkipSpaces(ReadOnlySpan<char> span, ref int position)
    {
        while (position < span.Length && span[position] == ' ')
        {
            position++;
        }
    }
}
