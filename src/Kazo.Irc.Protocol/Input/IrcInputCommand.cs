namespace Kazo.Irc.Protocol.Input;

/// <summary>What a line typed in the input box asks for.</summary>
public enum IrcInputCommandKind
{
    /// <summary>Plain text: a message to the open conversation.</summary>
    Say,
    /// <summary><c>/me text</c>: an action in the open conversation.</summary>
    Action,
    /// <summary><c>/msg target text</c>.</summary>
    Message,
    /// <summary><c>/join #a,#b [keys]</c>.</summary>
    Join,
    /// <summary><c>/part [#channel] [reason]</c>.</summary>
    Part,
    /// <summary><c>/nick new</c>.</summary>
    Nick,
    /// <summary><c>/topic [#channel] [text]</c>: without text, asks for the topic.</summary>
    Topic,
    /// <summary><c>/whois nick</c>.</summary>
    Whois,
    /// <summary><c>/away [message]</c>: without a message, back.</summary>
    Away,
    /// <summary><c>/quit [message]</c>.</summary>
    Quit,
    /// <summary><c>/query nick [text]</c>: opens a private conversation.</summary>
    Query,
    /// <summary><c>/raw line</c>: a line sent as it is.</summary>
    Raw,
    /// <summary>A line that cannot be done: see <see cref="IrcInputCommand.Error"/>.</summary>
    Invalid,
}

/// <summary>Why a typed line cannot be done.</summary>
public enum IrcInputError
{
    /// <summary>No error.</summary>
    None,
    /// <summary>The line is empty.</summary>
    EmptyInput,
    /// <summary>The command is unknown (<see cref="IrcInputCommand.Name"/> holds it).</summary>
    UnknownCommand,
    /// <summary>A required argument is missing.</summary>
    MissingArgument,
    /// <summary>The command needs an open conversation, and none is open (the server tab).</summary>
    NoConversation,
    /// <summary>The command needs a channel and the open conversation is not one.</summary>
    NotAChannel,
}

/// <summary>A typed line once read: the command and its arguments.</summary>
public sealed record IrcInputCommand
{
    /// <summary>What it asks for.</summary>
    public required IrcInputCommandKind Kind { get; init; }

    /// <summary>The command name as typed, in lower case, without the slash (empty for plain text).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The target: conversation, channel or nickname.</summary>
    public string? Target { get; init; }

    /// <summary>The text: message, reason, topic, away message, new nickname or raw line.</summary>
    public string? Text { get; init; }

    /// <summary>The channels of <c>/join</c>.</summary>
    public IReadOnlyList<string> Channels { get; init; } = [];

    /// <summary>The keys of <c>/join</c>, in the order of the channels.</summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>Why the line cannot be done, for <see cref="IrcInputCommandKind.Invalid"/>.</summary>
    public IrcInputError Error { get; init; }
}
