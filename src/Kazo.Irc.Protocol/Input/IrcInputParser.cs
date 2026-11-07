namespace Kazo.Irc.Protocol.Input;

/// <summary>
/// Reads what the user typed: plain text, or a command among <c>/join /part /msg /me /nick /topic
/// /whois /away /quit /query /raw</c> (plan, decision 18), plus the usual aliases <c>/j</c> and
/// <c>/quote</c>. <c>//text</c> sends <c>/text</c> as a message.
/// </summary>
public static class IrcInputParser
{
    /// <summary>Reads one typed line.</summary>
    /// <param name="input">The line.</param>
    /// <param name="conversation">The open conversation (channel or nickname), or null in the server tab.</param>
    /// <param name="channelTypes">The characters a channel starts with (ISUPPORT CHANTYPES).</param>
    public static IrcInputCommand Parse(string input, string? conversation, string channelTypes = "#&")
    {
        ArgumentNullException.ThrowIfNull(input);
        var line = input.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(line))
        {
            return Invalid(string.Empty, IrcInputError.EmptyInput);
        }

        if (line[0] != '/' || line.StartsWith("//", StringComparison.Ordinal))
        {
            var text = line[0] == '/' ? line[1..] : line;
            return conversation is null
                ? Invalid(string.Empty, IrcInputError.NoConversation)
                : new IrcInputCommand { Kind = IrcInputCommandKind.Say, Target = conversation, Text = text };
        }

        var space = line.IndexOf(' ', StringComparison.Ordinal);
        var name = (space < 0 ? line[1..] : line[1..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : line[(space + 1)..].Trim();
        var context = new Context(name, rest, conversation, channelTypes);
        return name switch
        {
            "join" or "j" => Join(context),
            "part" or "leave" => Part(context),
            "msg" => Message(context),
            "me" => Action(context),
            "nick" => Single(context, IrcInputCommandKind.Nick),
            "topic" => Topic(context),
            "whois" => Single(context, IrcInputCommandKind.Whois),
            "away" => new IrcInputCommand { Kind = IrcInputCommandKind.Away, Name = name, Text = NullIfEmpty(rest) },
            "quit" => new IrcInputCommand { Kind = IrcInputCommandKind.Quit, Name = name, Text = NullIfEmpty(rest) },
            "query" => Query(context),
            "raw" or "quote" => rest.Length == 0
                ? Invalid(name, IrcInputError.MissingArgument)
                : new IrcInputCommand { Kind = IrcInputCommandKind.Raw, Name = name, Text = rest },
            _ => Invalid(name, IrcInputError.UnknownCommand),
        };
    }

    private sealed record Context(string Name, string Rest, string? Conversation, string ChannelTypes)
    {
        public bool IsChannel(string? value) => !string.IsNullOrEmpty(value) && ChannelTypes.Contains(value[0], StringComparison.Ordinal);

        public (string First, string Remainder) Split()
        {
            var space = Rest.IndexOf(' ', StringComparison.Ordinal);
            return space < 0 ? (Rest, string.Empty) : (Rest[..space], Rest[(space + 1)..].TrimStart());
        }
    }

    private static IrcInputCommand Join(Context context)
    {
        var (channels, keys) = context.Split();
        if (channels.Length == 0)
        {
            return Invalid(context.Name, IrcInputError.MissingArgument);
        }

        var defaultType = context.ChannelTypes.Length > 0 ? context.ChannelTypes[0] : '#';
        return new IrcInputCommand
        {
            Kind = IrcInputCommandKind.Join,
            Name = context.Name,
            Channels = channels.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => context.IsChannel(c) ? c : defaultType + c)
                .ToList(),
            Keys = keys.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries),
        };
    }

    private static IrcInputCommand Part(Context context)
    {
        var (first, remainder) = context.Split();
        if (context.IsChannel(first))
        {
            return new IrcInputCommand { Kind = IrcInputCommandKind.Part, Name = context.Name, Target = first, Text = NullIfEmpty(remainder) };
        }

        if (context.Conversation is null)
        {
            return Invalid(context.Name, IrcInputError.NoConversation);
        }

        return context.IsChannel(context.Conversation)
            ? new IrcInputCommand { Kind = IrcInputCommandKind.Part, Name = context.Name, Target = context.Conversation, Text = NullIfEmpty(context.Rest) }
            : Invalid(context.Name, IrcInputError.NotAChannel);
    }

    private static IrcInputCommand Message(Context context)
    {
        var (target, text) = context.Split();
        return target.Length == 0 || text.Length == 0
            ? Invalid(context.Name, IrcInputError.MissingArgument)
            : new IrcInputCommand { Kind = IrcInputCommandKind.Message, Name = context.Name, Target = target, Text = text };
    }

    private static IrcInputCommand Action(Context context)
    {
        if (context.Conversation is null)
        {
            return Invalid(context.Name, IrcInputError.NoConversation);
        }

        return context.Rest.Length == 0
            ? Invalid(context.Name, IrcInputError.MissingArgument)
            : new IrcInputCommand { Kind = IrcInputCommandKind.Action, Name = context.Name, Target = context.Conversation, Text = context.Rest };
    }

    private static IrcInputCommand Topic(Context context)
    {
        var (first, remainder) = context.Split();
        if (context.IsChannel(first))
        {
            return new IrcInputCommand { Kind = IrcInputCommandKind.Topic, Name = context.Name, Target = first, Text = NullIfEmpty(remainder) };
        }

        if (context.Conversation is null)
        {
            return Invalid(context.Name, IrcInputError.NoConversation);
        }

        return context.IsChannel(context.Conversation)
            ? new IrcInputCommand { Kind = IrcInputCommandKind.Topic, Name = context.Name, Target = context.Conversation, Text = NullIfEmpty(context.Rest) }
            : Invalid(context.Name, IrcInputError.NotAChannel);
    }

    private static IrcInputCommand Query(Context context)
    {
        var (nick, text) = context.Split();
        return nick.Length == 0
            ? Invalid(context.Name, IrcInputError.MissingArgument)
            : new IrcInputCommand { Kind = IrcInputCommandKind.Query, Name = context.Name, Target = nick, Text = NullIfEmpty(text) };
    }

    /// <summary>A command with one required word (<c>/nick</c>, <c>/whois</c>).</summary>
    private static IrcInputCommand Single(Context context, IrcInputCommandKind kind)
    {
        var (first, _) = context.Split();
        return first.Length == 0
            ? Invalid(context.Name, IrcInputError.MissingArgument)
            : new IrcInputCommand { Kind = kind, Name = context.Name, Target = first, Text = first };
    }

    private static IrcInputCommand Invalid(string name, IrcInputError error) =>
        new() { Kind = IrcInputCommandKind.Invalid, Name = name, Error = error };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
