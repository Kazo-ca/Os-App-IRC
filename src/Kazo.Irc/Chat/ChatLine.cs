using Kazo.Irc.Protocol.Client;

namespace Kazo.Irc.Chat;

/// <summary>What a line of a conversation is.</summary>
public enum ChatLineKind
{
    /// <summary>A message (PRIVMSG).</summary>
    Message,

    /// <summary>An action (<c>/me</c>).</summary>
    Action,

    /// <summary>A notice.</summary>
    Notice,

    /// <summary>A join, part, quit, nickname, topic, mode or away event (<see cref="ChatLine.Activity"/>).</summary>
    Activity,

    /// <summary>A line from the server: numerics, MOTD, CTCP, WHOIS, the connection's state.</summary>
    Server,

    /// <summary>An error from the server or the connection.</summary>
    Error,
}

/// <summary>A line shown in a conversation, and written in its log.</summary>
/// <param name="Time">When it happened (server-time when the server gives it).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Nick">Who wrote it or did it; null for a server line.</param>
/// <param name="Text">The text: the message, or the detail of an activity (reason, new nickname, topic, modes).</param>
/// <param name="IsOwn">True for what this client sent or did.</param>
/// <param name="IsMention">True when someone else's message names the user (decision 14).</param>
/// <param name="Activity">The kind of activity, for <see cref="ChatLineKind.Activity"/>.</param>
/// <param name="TargetNick">The kicked user, for a kick.</param>
public sealed record ChatLine(
    DateTimeOffset Time,
    ChatLineKind Kind,
    string? Nick,
    string Text,
    bool IsOwn = false,
    bool IsMention = false,
    IrcActivityKind? Activity = null,
    string? TargetNick = null)
{
    /// <summary>True for a message, an action or a notice: what a person wrote.</summary>
    public bool IsChat => Kind is ChatLineKind.Message or ChatLineKind.Action or ChatLineKind.Notice;
}
