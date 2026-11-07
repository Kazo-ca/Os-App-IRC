using Kazo.Irc.Protocol.Input;
using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Client;

/// <summary>What the user asks for: messages, joins, nickname, topic, WHOIS, away, quit, queries, raw lines.</summary>
public sealed partial class IrcClient
{
    /// <summary>The length of a user name the server may add to the source (ident, with <c>~</c>).</summary>
    private const int AssumedUserNameBytes = 10;

    /// <summary>The longest host name the server may add to the source.</summary>
    private const int AssumedHostBytes = 63;

    /// <summary>Does what a typed line asks for (see <see cref="IrcInputParser"/>).</summary>
    /// <exception cref="ArgumentException">The command is <see cref="IrcInputCommandKind.Invalid"/>.</exception>
    /// <exception cref="InvalidOperationException">The client is not connected (except for <c>/query</c>).</exception>
    public Task ExecuteAsync(IrcInputCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Kind switch
        {
            IrcInputCommandKind.Say or IrcInputCommandKind.Message => SendMessageAsync(command.Target!, command.Text!, cancellationToken),
            IrcInputCommandKind.Action => SendActionAsync(command.Target!, command.Text!, cancellationToken),
            IrcInputCommandKind.Join => JoinAsync(command.Channels, command.Keys, cancellationToken),
            IrcInputCommandKind.Part => PartAsync(command.Target!, command.Text, cancellationToken),
            IrcInputCommandKind.Nick => ChangeNickAsync(command.Target!, cancellationToken),
            IrcInputCommandKind.Topic => TopicAsync(command.Target!, command.Text, cancellationToken),
            IrcInputCommandKind.Whois => WhoisAsync(command.Target!, cancellationToken),
            IrcInputCommandKind.Away => SetAwayAsync(command.Text, cancellationToken),
            IrcInputCommandKind.Quit => QuitAsync(command.Text, cancellationToken),
            IrcInputCommandKind.Query => QueryAsync(command.Target!, command.Text, cancellationToken),
            IrcInputCommandKind.Raw => SendRawAsync(command.Text!, cancellationToken),
            _ => throw new ArgumentException($"The input cannot be done: {command.Error}.", nameof(command)),
        };
    }

    /// <summary>Sends a message, cut into several lines when it is too long, and shows it as the client's own.</summary>
    public Task SendMessageAsync(string target, string text, CancellationToken cancellationToken = default) =>
        SendChatAsync("PRIVMSG", IrcChatMessageKind.Message, target, text, cancellationToken);

    /// <summary>Sends a notice.</summary>
    public Task SendNoticeAsync(string target, string text, CancellationToken cancellationToken = default) =>
        SendChatAsync("NOTICE", IrcChatMessageKind.Notice, target, text, cancellationToken);

    /// <summary>Sends an action (<c>/me</c>).</summary>
    public Task SendActionAsync(string target, string text, CancellationToken cancellationToken = default) =>
        SendChatAsync("PRIVMSG", IrcChatMessageKind.Action, target, text, cancellationToken);

    /// <summary>Joins a channel.</summary>
    public Task JoinAsync(string channel, string? key = null, CancellationToken cancellationToken = default) =>
        JoinAsync([channel], key is null ? [] : [key], cancellationToken);

    /// <summary>Joins channels; the keys go to the first channels, in order.</summary>
    public async Task JoinAsync(IReadOnlyList<string> channels, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var joins = channels.Select((name, i) => new IrcChannelJoin(name, i < keys.Count ? keys[i] : null)).ToList();
        foreach (var join in joins)
        {
            var channel = GetOrAddChannel(join.Name);
            channel.WantsJoin = true;
            channel.Key = join.Key ?? channel.Key;
        }

        await SendJoinsAsync(joins, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Leaves a channel; it is not joined again after a reconnection.</summary>
    public Task PartAsync(string channel, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (GetChannel(channel) is { } known)
        {
            known.WantsJoin = false;
        }

        return SendAsync(reason is null ? new IrcMessage("PART", channel) : new IrcMessage("PART", channel, reason), cancellationToken);
    }

    /// <summary>Asks for another nickname; <see cref="CurrentNick"/> changes when the server confirms it.</summary>
    public Task ChangeNickAsync(string nick, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick);
        if (!_registered)
        {
            _wantedNick = nick;
        }

        return SendAsync(new IrcMessage("NICK", nick), cancellationToken);
    }

    /// <summary>Sets the topic of a channel, or asks for it when <paramref name="topic"/> is null.</summary>
    public Task TopicAsync(string channel, string? topic, CancellationToken cancellationToken = default) =>
        SendAsync(topic is null ? new IrcMessage("TOPIC", channel) : new IrcMessage("TOPIC", channel, topic), cancellationToken);

    /// <summary>Asks who someone is; the answer comes with <see cref="WhoisReceived"/>.</summary>
    public Task WhoisAsync(string nick, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick);
        _whois[ServerSupport.Fold(nick)] = new IrcWhoisInfo { Nick = nick };
        return SendAsync(new IrcMessage("WHOIS", nick), cancellationToken);
    }

    /// <summary>Marks the client away with <paramref name="message"/>, or back when it is null.</summary>
    public Task SetAwayAsync(string? message, CancellationToken cancellationToken = default) =>
        SendAsync(string.IsNullOrEmpty(message) ? new IrcMessage("AWAY") : new IrcMessage("AWAY", message), cancellationToken);

    /// <summary>Opens a private conversation, and sends <paramref name="text"/> in it when given.</summary>
    public async Task QueryAsync(string nick, string? text = null, CancellationToken cancellationToken = default)
    {
        OpenQuery(nick);
        if (!string.IsNullOrEmpty(text))
        {
            await SendMessageAsync(nick, text, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens a private conversation (no line is sent).</summary>
    public void OpenQuery(string nick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick);
        var key = ServerSupport.Fold(nick);
        if (_queries.TryAdd(key, nick))
        {
            TrackUser(new IrcSource(nick));
            QueryOpened?.Invoke(this, nick);
        }
    }

    /// <summary>Closes a private conversation.</summary>
    public void CloseQuery(string nick)
    {
        if (_queries.Remove(ServerSupport.Fold(nick)))
        {
            ForgetIfUnseen(nick);
        }
    }

    /// <summary>Sends a line typed by the user as it is.</summary>
    /// <exception cref="IrcProtocolException">The line is not an IRC message.</exception>
    public Task SendRawAsync(string line, CancellationToken cancellationToken = default) =>
        SendAsync(IrcMessage.Parse(line), cancellationToken);

    private async Task SendChatAsync(string command, IrcChatMessageKind kind, string target, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        var overhead = kind == IrcChatMessageKind.Action ? IrcEncoding.ByteCount(IrcCtcp.Format(IrcCtcp.Action, " ")) - 1 : 0;
        var pieces = IrcTextSplitter.Split(text, MaxTextBytes(command, target) - overhead);
        var self = _ownSource ?? new IrcSource(CurrentNick);
        var conversation = ChannelOf(target) ?? target;
        foreach (var piece in pieces)
        {
            var payload = kind == IrcChatMessageKind.Action ? IrcCtcp.Format(IrcCtcp.Action, piece) : piece;
            await SendAsync(new IrcMessage(command, target, payload), cancellationToken).ConfigureAwait(false);
            ChatMessage?.Invoke(this, new IrcChatMessage(
                Now, kind, self, target, conversation, piece, ChannelOf(target) is null, IsOwn: true, new Dictionary<string, string>()));
        }
    }

    /// <summary>
    /// The bytes of text a <c>PRIVMSG</c> can hold once the server adds the client's source
    /// (<c>:nick!user@host </c>) when relaying it, so no other client gets it cut.
    /// </summary>
    private int MaxTextBytes(string command, string target)
    {
        var sourceBytes = _ownSource is not null
            ? IrcEncoding.ByteCount(_ownSource.ToString())
            : IrcEncoding.ByteCount(CurrentNick) + AssumedUserNameBytes + AssumedHostBytes + 2;
        var fixedBytes = 1 + sourceBytes + 1 + command.Length + 1 + IrcEncoding.ByteCount(target) + 2;
        return IrcLimits.MaxLineBytes - IrcLimits.LineEndingBytes - fixedBytes;
    }
}
