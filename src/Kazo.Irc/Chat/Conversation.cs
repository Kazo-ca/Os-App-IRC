using Kazo.Irc.Protocol.Model;

namespace Kazo.Irc.Chat;

/// <summary>What a conversation is.</summary>
public enum ConversationKind
{
    /// <summary>The network's own tab: MOTD, numerics, errors.</summary>
    Server,

    /// <summary>A channel.</summary>
    Channel,

    /// <summary>A private conversation with one person.</summary>
    Private,
}

/// <summary>
/// One conversation of a network: its lines (the last <see cref="MaxLines"/>), and what the user
/// has not seen yet (decision 14).
/// </summary>
public sealed class Conversation
{
    /// <summary>The lines kept in memory per conversation; older ones stay only in the logs.</summary>
    public const int MaxLines = 1000;

    private readonly List<ChatLine> _lines = [];

    internal Conversation(IrcSession session, string name, ConversationKind kind)
    {
        Session = session;
        Name = name;
        Kind = kind;
    }

    /// <summary>The network this conversation belongs to.</summary>
    public IrcSession Session { get; }

    /// <summary>The channel, the other person's nickname, or the network's name for the server tab.</summary>
    public string Name { get; internal set; }

    /// <summary>What the conversation is.</summary>
    public ConversationKind Kind { get; }

    /// <summary>The lines, oldest first.</summary>
    public IReadOnlyList<ChatLine> Lines => _lines;

    /// <summary>Messages written by others since the user last looked at this conversation.</summary>
    public int UnreadCount { get; internal set; }

    /// <summary>Mentions and private messages since the user last looked (the window title's counter).</summary>
    public int HighlightCount { get; internal set; }

    /// <summary>The channel of the client's model, for a channel conversation.</summary>
    public IrcChannel? Channel => Kind == ConversationKind.Channel ? Session.Client.GetChannel(Name) : null;

    /// <summary>False for a channel the user is not in (any more): the input then cannot talk there.</summary>
    public bool IsJoined => Kind != ConversationKind.Channel || Channel?.IsJoined == true;

    /// <summary>The users of a channel; empty for the other conversations.</summary>
    public IReadOnlyList<IrcChannelUser> Users => Channel?.Users ?? [];

    /// <summary>The name to give the input parser: null for the server tab (no conversation).</summary>
    public string? Target => Kind == ConversationKind.Server ? null : Name;

    internal void Add(ChatLine line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    internal void MarkSeen()
    {
        UnreadCount = 0;
        HighlightCount = 0;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
