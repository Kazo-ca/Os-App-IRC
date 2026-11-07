using System.Globalization;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Text;
using Kazo.Irc.Protocol.Transport;
using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Chat;

/// <summary>
/// One network of the window: its <see cref="IrcClient"/> and its conversations (the server tab,
/// channels and private conversations), filled from the client's events. It decides where each
/// event is shown; <see cref="IrcChat"/> decides what the user is told about it.
/// </summary>
public sealed class IrcSession : IAsyncDisposable
{
    private readonly List<Conversation> _conversations = [];
    private readonly IrcChatTexts _texts;

    /// <summary>Creates the session; nothing is sent before <see cref="Start"/>.</summary>
    /// <param name="network">The network.</param>
    /// <param name="options">How to register (nickname, channels to join).</param>
    /// <param name="transports">Gives a new transport at every connection attempt.</param>
    /// <param name="texts">The texts of the connection lines.</param>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    public IrcSession(IrcNetwork network, IrcClientOptions options, Func<IIrcTransport> transports, IrcChatTexts? texts = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        Network = network;
        _texts = texts ?? new IrcChatTexts();
        Time = timeProvider ?? TimeProvider.System;
        Client = new IrcClient(options, transports, Time);
        Server = new Conversation(this, network.Name, ConversationKind.Server);
        _conversations.Add(Server);

        Client.StateChanged += OnStateChanged;
        Client.ChatMessage += OnChatMessage;
        Client.Activity += OnActivity;
        Client.ServerText += (_, e) => Add(Server, new ChatLine(e.Time, ChatLineKind.Server, null, e.Text));
        Client.Error += OnError;
        Client.MessageHandled += OnMessageHandled;
        Client.WhoisReceived += OnWhois;
        Client.SaslCompleted += (_, e) => Add(Server, new ChatLine(Time.GetUtcNow(), e.Success ? ChatLineKind.Server : ChatLineKind.Error, null, e.Message));
        Client.Reconnecting += (_, e) => Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Error, null,
            Format(_texts.Reconnecting, (int)Math.Ceiling(e.Delay.TotalSeconds), e.Attempt)));
        Client.QueryOpened += (_, nick) => GetOrAdd(nick, ConversationKind.Private);
    }

    /// <summary>A line was added to a conversation.</summary>
    public event Action<Conversation, ChatLine>? LineAdded;

    /// <summary>Something else changed: the state, the conversations, the users, a topic.</summary>
    public event Action? Changed;

    /// <summary>The user joined a channel (the conversation exists by then).</summary>
    public event Action<Conversation>? Joined;

    /// <summary>The client reported an error (a connection failure carries its <c>IrcTransportErrors</c> code).</summary>
    public event Action<IrcClientError>? Failed;

    /// <summary>The network.</summary>
    public IrcNetwork Network { get; }

    /// <summary>The client of this network.</summary>
    public IrcClient Client { get; }

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>The server tab.</summary>
    public Conversation Server { get; }

    /// <summary>The conversations: the server tab, then the channels, then the private conversations, by name.</summary>
    public IReadOnlyList<Conversation> Conversations =>
        [Server,
         .. _conversations.Where(c => c.Kind == ConversationKind.Channel).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
         .. _conversations.Where(c => c.Kind == ConversationKind.Private).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Starts connecting.</summary>
    public void Start()
    {
        Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Server, null, Format(_texts.Connecting, Network.Name)));
        Client.Start();
    }

    /// <summary>Finds a channel or private conversation by name (IRC case rules).</summary>
    /// <param name="name">The channel or nickname.</param>
    /// <returns>The conversation, or null.</returns>
    public Conversation? Find(string name)
    {
        var key = Client.ServerSupport.Fold(name);
        return _conversations.FirstOrDefault(c => c.Kind != ConversationKind.Server && Client.ServerSupport.Fold(c.Name) == key);
    }

    /// <summary>
    /// Closes a conversation: leaves a joined channel (<c>PART</c>), forgets a private one. The
    /// server tab cannot be closed.
    /// </summary>
    /// <param name="conversation">A conversation of this session.</param>
    /// <returns>A task that completes once the conversation is gone.</returns>
    public async Task CloseAsync(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.Kind == ConversationKind.Server || !_conversations.Contains(conversation))
        {
            return;
        }

        if (conversation.Kind == ConversationKind.Channel && conversation.IsJoined && Client.State == IrcClientState.Connected)
        {
            await Client.PartAsync(conversation.Name);
        }
        else if (conversation.Kind == ConversationKind.Private)
        {
            Client.CloseQuery(conversation.Name);
        }

        _conversations.Remove(conversation);
        Changed?.Invoke();
    }

    /// <summary>Sends <c>QUIT</c> and ends the connection (decision 8: closing the window).</summary>
    /// <param name="message">The quit message, or the client's default.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    public Task QuitAsync(string? message = null) => Client.QuitAsync(message);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Client.DisposeAsync();

    private void OnStateChanged(object? sender, IrcStateChange change)
    {
        if (change.Current == IrcClientState.Connected)
        {
            Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Server, null, Format(_texts.Connected, Client.CurrentNick)));
        }
        else if (change.Current == IrcClientState.Disconnected && change.Previous != IrcClientState.Disconnected)
        {
            Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Server, null, _texts.Disconnected));
        }

        Changed?.Invoke();
    }

    private void OnChatMessage(object? sender, IrcChatMessage message)
    {
        Conversation conversation;
        if (message.Kind == IrcChatMessageKind.Notice && message.IsPrivate)
        {
            // A notice does not open a private conversation: it goes where that person already
            // talks, or to the server tab.
            conversation = Find(message.Conversation) ?? Server;
        }
        else
        {
            conversation = GetOrAdd(message.Conversation, message.IsPrivate ? ConversationKind.Private : ConversationKind.Channel);
        }

        var kind = message.Kind switch
        {
            IrcChatMessageKind.Action => ChatLineKind.Action,
            IrcChatMessageKind.Notice => ChatLineKind.Notice,
            _ => ChatLineKind.Message,
        };
        var mention = !message.IsOwn && IrcMentions.Contains(IrcFormatting.Strip(message.Text), Client.CurrentNick);
        Add(conversation, new ChatLine(message.Time, kind, message.Sender.Name, message.Text, message.IsOwn, mention));
    }

    private void OnActivity(object? sender, IrcActivity activity)
    {
        var conversation = Find(activity.Conversation);
        if (conversation is null && activity.Kind == IrcActivityKind.Nick && Find(activity.Nick) is { Kind: ConversationKind.Private } query)
        {
            // The other person of a private conversation changed nickname: the conversation follows.
            query.Name = activity.Conversation;
            conversation = query;
        }

        if (conversation is null && activity is { Kind: IrcActivityKind.Join, IsOwn: true })
        {
            conversation = GetOrAdd(activity.Conversation, ConversationKind.Channel);
        }

        if (conversation is null)
        {
            return;
        }

        Add(conversation, new ChatLine(activity.Time, ChatLineKind.Activity, activity.Nick, activity.Detail ?? string.Empty,
            activity.IsOwn, Activity: activity.Kind, TargetNick: activity.TargetNick));
        if (activity is { Kind: IrcActivityKind.Join, IsOwn: true })
        {
            Joined?.Invoke(conversation);
        }
    }

    /// <summary>
    /// The replies that change a conversation without adding a line: the end of a <c>NAMES</c>
    /// list (the users), the topic and its author sent on join, the channel's modes. On a real
    /// network they come after the <c>JOIN</c> has been drawn, so the view must be told.
    /// </summary>
    private static readonly HashSet<string> RedrawReplies = new(StringComparer.Ordinal) { "366", "331", "332", "333", "324", "AWAY", "ACCOUNT" };

    private void OnMessageHandled(object? sender, IrcMessage message)
    {
        if (RedrawReplies.Contains(message.Command))
        {
            Changed?.Invoke();
        }
    }

    private void OnError(object? sender, IrcClientError error)
    {
        // A connection failure gets a sentence the user can act on (work item 3); the other
        // errors (a refused command, a nickname taken) show the server's own text.
        var text = _texts.TransportErrors.TryGetValue(error.Code, out var format)
            ? Format(format, Network.Name, error.Message)
            : error.Message;
        Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Error, null, text));
        Failed?.Invoke(error);
    }

    private void OnWhois(object? sender, IrcWhoisInfo info)
    {
        var text = info.Exists
            ? Format(_texts.Whois, info.Nick, $"{info.UserName}@{info.Host}", info.RealName ?? string.Empty, info.Server ?? string.Empty, string.Join(' ', info.Channels))
            : Format(_texts.WhoisUnknown, info.Nick);
        Add(Server, new ChatLine(Time.GetUtcNow(), ChatLineKind.Server, null, text));
    }

    private Conversation GetOrAdd(string name, ConversationKind kind)
    {
        if (Find(name) is { } found)
        {
            return found;
        }

        var conversation = new Conversation(this, name, kind);
        _conversations.Add(conversation);
        Changed?.Invoke();
        return conversation;
    }

    private void Add(Conversation conversation, ChatLine line)
    {
        conversation.Add(line);
        LineAdded?.Invoke(conversation, line);
    }

    private static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
