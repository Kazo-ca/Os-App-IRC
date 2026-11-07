using System.Globalization;
using Kazo.Irc.Logs;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Input;

namespace Kazo.Irc.Chat;

/// <summary>Why a typed line was not sent.</summary>
public enum ChatInputProblem
{
    /// <summary>Sent.</summary>
    None,

    /// <summary>Nothing typed.</summary>
    Empty,

    /// <summary>An unknown <c>/command</c>.</summary>
    UnknownCommand,

    /// <summary>A command without its argument (<c>/join</c> alone).</summary>
    MissingArgument,

    /// <summary>Text typed in the server tab, which is no conversation.</summary>
    NoConversation,

    /// <summary>A channel command (<c>/topic</c>, <c>/part</c>) outside a channel.</summary>
    NotAChannel,

    /// <summary>No network, or the network is not connected.</summary>
    NotConnected,

    /// <summary>The server refused the line (too long, invalid).</summary>
    Refused,
}

/// <summary>
/// The window's chat, without Blazor: the networks (<see cref="IrcSession"/>), the open
/// conversation, what the typed lines do, and what the user is told (decision 14): unread
/// conversations, mentions and private messages counted for the window title, an OS notification
/// when the window does not have the focus; and the logs (decision 15).
/// </summary>
public sealed class IrcChat : IAsyncDisposable
{
    private readonly List<IrcSession> _sessions = [];
    private readonly HashSet<string> _pendingSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIrcNotifier _notifier;
    private readonly IrcLogWriter? _logs;
    private readonly IrcChatTexts _texts;
    private readonly TimeProvider _time;

    /// <summary>Creates the chat.</summary>
    /// <param name="catalog">The networks and their transports.</param>
    /// <param name="notifier">Shows the OS notifications.</param>
    /// <param name="logs">Writes the logs; null for none.</param>
    /// <param name="texts">The translated texts.</param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    public IrcChat(IrcNetworkCatalog catalog, IIrcNotifier notifier, IrcLogWriter? logs = null, IrcChatTexts? texts = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(notifier);
        Catalog = catalog;
        _notifier = notifier;
        _logs = logs;
        _texts = texts ?? new IrcChatTexts();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Anything shown changed: lines, conversations, counters, the open conversation.</summary>
    public event Action? Changed;

    /// <summary>The networks offered.</summary>
    public IrcNetworkCatalog Catalog { get; }

    /// <summary>The networks connected (or connecting), in the order they were opened.</summary>
    public IReadOnlyList<IrcSession> Sessions => _sessions;

    /// <summary>The open conversation, or null before the first network.</summary>
    public Conversation? Selected { get; private set; }

    /// <summary>Whether the window has the focus (and is not minimized).</summary>
    public bool IsWindowActive { get; private set; } = true;

    /// <summary>Whether the conversations are logged (decision 15).</summary>
    public bool LogsEnabled { get; set; } = true;

    /// <summary>The mentions and private messages not seen yet: the window title's counter.</summary>
    public int AttentionCount => _sessions.Sum(s => s.Conversations.Sum(c => c.HighlightCount));

    /// <summary>
    /// Opens a network: creates its session, shows its server tab and starts connecting. A network
    /// already open is only shown.
    /// </summary>
    /// <param name="network">A network of the catalog.</param>
    /// <param name="nick">The nickname (decision 12: the OS user name by default).</param>
    /// <returns>The session.</returns>
    public IrcSession Connect(IrcNetwork network, string nick)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (_sessions.FirstOrDefault(s => s.Network.Id == network.Id) is { } open)
        {
            Select(open.Server);
            return open;
        }

        var options = new IrcClientOptions
        {
            Nick = nick,
            AlternateNicks = [nick + "_", nick + "__"],
            AutoJoin = [.. network.AutoJoin.Select(channel => new IrcChannelJoin(channel))],
        };
        var session = new IrcSession(network, options, Catalog.TransportsFor(network), _texts, _time);
        session.LineAdded += (conversation, line) => OnLineAdded(conversation, line);
        session.Changed += () => Changed?.Invoke();
        session.Joined += OnJoined;
        _sessions.Add(session);
        Select(session.Server);
        session.Start();
        return session;
    }

    /// <summary>Opens a conversation: it is now seen (when the window has the focus).</summary>
    /// <param name="conversation">The conversation.</param>
    public void Select(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        Selected = conversation;
        if (IsWindowActive)
        {
            conversation.MarkSeen();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The window gained or lost the focus (or was minimized). Back in front, the open conversation
    /// is seen.
    /// </summary>
    /// <param name="active">True when the window has the focus.</param>
    public void SetWindowActive(bool active)
    {
        if (IsWindowActive == active)
        {
            return;
        }

        IsWindowActive = active;
        if (active)
        {
            Selected?.MarkSeen();
        }

        Changed?.Invoke();
    }

    /// <summary>Does what a typed line asks for in the open conversation (decision 18).</summary>
    /// <param name="text">The line typed.</param>
    /// <returns>What stopped it, or <see cref="ChatInputProblem.None"/>.</returns>
    public async Task<ChatInputProblem> SubmitAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ChatInputProblem.Empty;
        }

        if (Selected is not { } conversation)
        {
            return ChatInputProblem.NotConnected;
        }

        var session = conversation.Session;
        var command = IrcInputParser.Parse(text, conversation.Target, session.Client.ServerSupport.ChannelTypes);
        if (command.Kind == IrcInputCommandKind.Invalid)
        {
            return command.Error switch
            {
                IrcInputError.EmptyInput => ChatInputProblem.Empty,
                IrcInputError.UnknownCommand => ChatInputProblem.UnknownCommand,
                IrcInputError.MissingArgument => ChatInputProblem.MissingArgument,
                IrcInputError.NoConversation => ChatInputProblem.NoConversation,
                _ => ChatInputProblem.NotAChannel,
            };
        }

        if (command.Kind != IrcInputCommandKind.Query && session.Client.State != IrcClientState.Connected)
        {
            return ChatInputProblem.NotConnected;
        }

        RememberSelection(command);
        try
        {
            await session.Client.ExecuteAsync(command);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Protocol.Messages.IrcProtocolException)
        {
            return exception is InvalidOperationException ? ChatInputProblem.NotConnected : ChatInputProblem.Refused;
        }

        if (command.Kind == IrcInputCommandKind.Query && session.Find(command.Target!) is { } query)
        {
            Select(query);
        }

        return ChatInputProblem.None;
    }

    /// <summary>Closes a conversation (leaves a channel, forgets a private one) and opens the server tab.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <returns>A task that completes once it is closed.</returns>
    public async Task CloseConversationAsync(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        await conversation.Session.CloseAsync(conversation);
        if (Selected == conversation)
        {
            Select(conversation.Session.Server);
        }

        Changed?.Invoke();
    }

    /// <summary>Sends <c>QUIT</c> on every network (decision 8: the window closes).</summary>
    /// <param name="message">The quit message.</param>
    /// <returns>A task that completes once every connection is closed.</returns>
    public Task QuitAllAsync(string? message = null) =>
        Task.WhenAll(_sessions.Where(s => s.Client.State != IrcClientState.Disconnected).Select(s => s.QuitAsync(message)));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }

    private void RememberSelection(IrcInputCommand command)
    {
        // The conversation the user asked for opens when the server confirms it (JOIN), not a
        // channel joined again after a reconnection.
        if (command.Kind == IrcInputCommandKind.Join)
        {
            foreach (var channel in command.Channels)
            {
                _pendingSelections.Add(channel);
            }
        }
    }

    private void OnJoined(Conversation conversation)
    {
        if (_pendingSelections.Remove(conversation.Name))
        {
            Select(conversation);
        }
    }

    private void OnLineAdded(Conversation conversation, ChatLine line)
    {
        if (LogsEnabled)
        {
            _logs?.Append(conversation, line);
        }

        var seen = IsWindowActive && Selected == conversation;
        if (line.IsChat && !line.IsOwn && !seen)
        {
            conversation.UnreadCount++;
        }

        // Decision 14: what is addressed to the user is a mention, or a private message.
        var important = line.IsChat && !line.IsOwn && line.Kind != ChatLineKind.Notice
            && (line.IsMention || conversation.Kind == ConversationKind.Private);
        if (important && !seen)
        {
            conversation.HighlightCount++;
        }

        if (important && !IsWindowActive)
        {
            var title = conversation.Kind == ConversationKind.Private
                ? string.Format(CultureInfo.CurrentCulture, _texts.PrivateTitle, line.Nick)
                : string.Format(CultureInfo.CurrentCulture, _texts.MentionTitle, line.Nick, conversation.Name);
            _notifier.Notify(title, line.Kind == ChatLineKind.Action ? $"* {line.Nick} {line.Text}" : line.Text);
        }

        Changed?.Invoke();
    }
}
