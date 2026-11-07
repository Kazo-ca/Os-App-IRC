using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Client;

/// <summary>The kind of a chat message.</summary>
public enum IrcChatMessageKind
{
    /// <summary>A PRIVMSG.</summary>
    Message,

    /// <summary>A CTCP ACTION (<c>/me</c>).</summary>
    Action,

    /// <summary>A NOTICE.</summary>
    Notice,
}

/// <summary>A message to show in a conversation (channel or private).</summary>
/// <param name="Time">When it was sent: the IRCv3 server-time tag, otherwise the time it was received.</param>
/// <param name="Kind">Message, action or notice.</param>
/// <param name="Sender">Who sent it.</param>
/// <param name="Target">The target of the line: a channel, or a nickname.</param>
/// <param name="Conversation">The conversation it belongs to: the channel, or the other person's nickname.</param>
/// <param name="Text">The text, without the CTCP delimiters of an action.</param>
/// <param name="IsPrivate">True for a message between two users (not in a channel).</param>
/// <param name="IsOwn">True for a message this client sent.</param>
/// <param name="Tags">The IRCv3 tags of the line.</param>
public sealed record IrcChatMessage(
    DateTimeOffset Time,
    IrcChatMessageKind Kind,
    IrcSource Sender,
    string Target,
    string Conversation,
    string Text,
    bool IsPrivate,
    bool IsOwn,
    IReadOnlyDictionary<string, string> Tags);

/// <summary>The kind of a channel event.</summary>
public enum IrcActivityKind
{
    /// <summary>Someone joined the channel.</summary>
    Join,
    /// <summary>Someone left the channel.</summary>
    Part,
    /// <summary>Someone was kicked from the channel.</summary>
    Kick,
    /// <summary>Someone left the network.</summary>
    Quit,
    /// <summary>Someone changed nickname.</summary>
    Nick,
    /// <summary>The topic changed.</summary>
    Topic,
    /// <summary>Modes changed.</summary>
    Mode,
    /// <summary>Someone is now away.</summary>
    Away,
    /// <summary>Someone is back.</summary>
    Back,
}

/// <summary>An event to show in a conversation: join, part, quit, nickname, topic, mode, away.</summary>
/// <param name="Time">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Conversation">The channel (or the private conversation) where it is shown.</param>
/// <param name="Nick">Who did it.</param>
/// <param name="Detail">The reason, new nickname, topic, modes or away message, depending on <paramref name="Kind"/>.</param>
/// <param name="TargetNick">The kicked user for <see cref="IrcActivityKind.Kick"/>.</param>
/// <param name="IsOwn">True when it is this client.</param>
public sealed record IrcActivity(
    DateTimeOffset Time,
    IrcActivityKind Kind,
    string Conversation,
    string Nick,
    string? Detail = null,
    string? TargetNick = null,
    bool IsOwn = false);

/// <summary>A line for the server tab: numerics, notices from the server, CTCP queries.</summary>
/// <param name="Time">When it was received.</param>
/// <param name="Command">The command or numeric.</param>
/// <param name="Text">The text to show.</param>
public sealed record IrcServerText(DateTimeOffset Time, string Command, string Text);

/// <summary>An error from the server (a 4xx or 5xx numeric, <c>ERROR</c>) or from the connection.</summary>
/// <param name="Code">The numeric, <c>ERROR</c>, <c>TRANSPORT</c>, <c>PING_TIMEOUT</c> or <c>NICK_EXHAUSTED</c>.</param>
/// <param name="Message">The text to show.</param>
/// <param name="Exception">The exception, for a connection error.</param>
public sealed record IrcClientError(string Code, string Message, Exception? Exception = null);

/// <summary>The answer to a WHOIS, gathered until its <c>318</c>.</summary>
public sealed record IrcWhoisInfo
{
    /// <summary>The nickname asked for.</summary>
    public required string Nick { get; init; }
    /// <summary>The user name.</summary>
    public string? UserName { get; init; }
    /// <summary>The host.</summary>
    public string? Host { get; init; }
    /// <summary>The real name.</summary>
    public string? RealName { get; init; }
    /// <summary>The server the user is on.</summary>
    public string? Server { get; init; }
    /// <summary>The description of that server.</summary>
    public string? ServerInfo { get; init; }
    /// <summary>The services account.</summary>
    public string? Account { get; init; }
    /// <summary>The away message.</summary>
    public string? AwayMessage { get; init; }
    /// <summary>The channels, with their prefix symbols.</summary>
    public IReadOnlyList<string> Channels { get; init; } = [];
    /// <summary>Seconds idle.</summary>
    public long? IdleSeconds { get; init; }
    /// <summary>When the user connected.</summary>
    public DateTimeOffset? SignedOnAt { get; init; }
    /// <summary>True for an IRC operator.</summary>
    public bool IsOperator { get; init; }
    /// <summary>False when the server said the nickname does not exist.</summary>
    public bool Exists { get; init; } = true;
}

/// <summary>The end of a SASL login.</summary>
/// <param name="Success">True when the server logged the client in.</param>
/// <param name="Code">The numeric (<c>903</c>, <c>904</c>...), or <c>MECHANISM</c> when the server does not offer PLAIN.</param>
/// <param name="Message">The server's text.</param>
public sealed record IrcSaslResult(bool Success, string Code, string Message);

/// <summary>The client waits before a new connection attempt.</summary>
/// <param name="Attempt">The attempt number, from 1.</param>
/// <param name="Delay">The wait.</param>
public sealed record IrcReconnectInfo(int Attempt, TimeSpan Delay);

/// <summary>A change of <see cref="IrcClientState"/>.</summary>
/// <param name="Previous">The state before.</param>
/// <param name="Current">The state now.</param>
public sealed record IrcStateChange(IrcClientState Previous, IrcClientState Current);
