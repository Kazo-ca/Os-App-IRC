using System.Globalization;
using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Model;

namespace Kazo.Irc.Protocol.Client;

/// <summary>What the client does with each line from the server: the model of channels and users.</summary>
public sealed partial class IrcClient
{
    private async Task HandleAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Command)
        {
            case "PING":
                await SendAsync(new IrcMessage("PONG", message.Parameters.Count > 0 ? message.Parameters[^1] : string.Empty), cancellationToken).ConfigureAwait(false);
                return;
            case "PONG":
                return;
            case "CAP":
                await HandleCapAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            case "AUTHENTICATE":
                await HandleAuthenticateAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            case "ERROR":
                Error?.Invoke(this, new IrcClientError("ERROR", message.GetParameter(0) ?? string.Empty));
                return;
            case "PRIVMSG":
            case "NOTICE":
                await HandleChatAsync(message, cancellationToken).ConfigureAwait(false);
                return;
        }

        if (HandleModel(message))
        {
            return;
        }

        if (message.IsNumeric)
        {
            await HandleNumericAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Commands that change the model; returns false for a command it does not know.</summary>
    private bool HandleModel(IrcMessage message)
    {
        switch (message.Command)
        {
            case "JOIN": HandleJoin(message); return true;
            case "PART": HandlePart(message); return true;
            case "KICK": HandleKick(message); return true;
            case "QUIT": HandleQuit(message); return true;
            case "NICK": HandleNick(message); return true;
            case "TOPIC": HandleTopic(message); return true;
            case "MODE": HandleMode(message); return true;
            case "AWAY": HandleAway(message); return true;
            case "ACCOUNT": HandleAccount(message); return true;
            default: return false;
        }
    }

    private async Task HandleNumericAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Command)
        {
            case IrcNumerics.Welcome:
                await HandleWelcomeAsync(message, cancellationToken).ConfigureAwait(false);
                RaiseServerText(message);
                return;
            case IrcNumerics.ISupport:
                ApplyISupport(message);
                return;
            case IrcNumerics.NicknameInUse:
            case IrcNumerics.ErroneousNickname:
            case IrcNumerics.NickCollision:
                await HandleNickRefusedAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            case IrcNumerics.UnknownCommand when string.Equals(message.GetParameter(1), "CAP", StringComparison.OrdinalIgnoreCase):
                _capNegotiating = false; // An old server without CAP: registration goes on with NICK and USER.
                return;
            case IrcNumerics.LoggedIn or IrcNumerics.LoggedOut or IrcNumerics.NickLocked or IrcNumerics.SaslSuccess
                or IrcNumerics.SaslFail or IrcNumerics.SaslTooLong or IrcNumerics.SaslAborted
                or IrcNumerics.SaslAlready or IrcNumerics.SaslMechanisms:
                await HandleSaslNumericAsync(message, cancellationToken).ConfigureAwait(false);
                RaiseServerText(message);
                return;
            case IrcNumerics.Topic:
                SetTopicFromReply(message.GetParameter(1), message.GetParameter(2));
                return;
            case IrcNumerics.NoTopic:
                SetTopicFromReply(message.GetParameter(1), null);
                return;
            case IrcNumerics.TopicWhoTime:
                SetTopicWhoTime(message);
                return;
            case IrcNumerics.NamesReply:
                AddNames(message);
                return;
            case IrcNumerics.EndOfNames:
                EndNames(message.GetParameter(1));
                return;
            case IrcNumerics.ChannelModeIs:
                ApplyChannelModeIs(message);
                return;
            case IrcNumerics.UnAway:
                IsAway = false;
                RaiseServerText(message);
                return;
            case IrcNumerics.NowAway:
                IsAway = true;
                RaiseServerText(message);
                return;
        }

        if (HandleWhoisNumeric(message))
        {
            return;
        }

        var code = int.Parse(message.Command, CultureInfo.InvariantCulture);
        if (code is >= 400 and < 600)
        {
            Error?.Invoke(this, new IrcClientError(message.Command, NumericText(message)));
        }
        else
        {
            RaiseServerText(message);
        }
    }

    private void ApplyISupport(IrcMessage message)
    {
        var mapping = ServerSupport.CaseMapping;
        ServerSupport.Apply(message.Parameters);
        if (mapping != ServerSupport.CaseMapping)
        {
            Refold();
        }
    }

    private void HandleJoin(IrcMessage message)
    {
        var nick = message.Source?.Name;
        var name = message.GetParameter(0);
        if (nick is null || name is null)
        {
            return;
        }

        var user = TrackUser(message.Source!);
        var account = message.GetParameter(1);
        if (account is not null && message.Parameters.Count >= 3)
        {
            user.Account = account == "*" ? null : account; // IRCv3 extended-join
        }

        var channel = GetOrAddChannel(name);
        var own = IsMe(nick);
        if (own)
        {
            channel.Name = name;
            channel.IsJoined = true;
            channel.WantsJoin = true;
            channel.ClearUsers();
            channel.ClearModes();
            _ownSource = message.Source;
        }

        channel.AddUser(user);
        RaiseActivity(message, IrcActivityKind.Join, channel.Name, nick, own: own);
    }

    private void HandlePart(IrcMessage message)
    {
        var nick = message.Source?.Name;
        var names = message.GetParameter(0);
        if (nick is null || names is null)
        {
            return;
        }

        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (GetChannel(name) is not { } channel)
            {
                continue;
            }

            LeaveChannel(channel, nick);
            RaiseActivity(message, IrcActivityKind.Part, channel.Name, nick, message.GetParameter(1), own: IsMe(nick));
        }

        ForgetIfUnseen(nick);
    }

    private void HandleKick(IrcMessage message)
    {
        var name = message.GetParameter(0);
        var kicked = message.GetParameter(1);
        if (name is null || kicked is null || GetChannel(name) is not { } channel)
        {
            return;
        }

        LeaveChannel(channel, kicked);
        RaiseActivity(message, IrcActivityKind.Kick, channel.Name, message.Source?.Name ?? string.Empty, message.GetParameter(2), kicked, IsMe(kicked));
        ForgetIfUnseen(kicked);
    }

    /// <summary>Someone leaves a channel; when it is the client, it does not want to be in it any more.</summary>
    private void LeaveChannel(IrcChannel channel, string nick)
    {
        if (IsMe(nick))
        {
            channel.IsJoined = false;
            channel.WantsJoin = false;
            channel.ClearUsers();
            channel.ClearModes();
            return;
        }

        channel.RemoveUser(nick);
    }

    private void HandleQuit(IrcMessage message)
    {
        var nick = message.Source?.Name;
        if (nick is null)
        {
            return;
        }

        foreach (var conversation in ConversationsOf(nick))
        {
            GetChannel(conversation)?.RemoveUser(nick);
            RaiseActivity(message, IrcActivityKind.Quit, conversation, nick, message.GetParameter(0));
        }

        if (!_queries.ContainsKey(ServerSupport.Fold(nick)))
        {
            _users.Remove(ServerSupport.Fold(nick));
        }
    }

    private void HandleNick(IrcMessage message)
    {
        var oldNick = message.Source?.Name;
        var newNick = message.GetParameter(0);
        if (oldNick is null || string.IsNullOrEmpty(newNick))
        {
            return;
        }

        var conversations = ConversationsOf(oldNick);
        var own = IsMe(oldNick);
        if (own)
        {
            CurrentNick = newNick;
            _wantedNick = newNick;
            _ownSource = _ownSource is null ? null : _ownSource with { Name = newNick };
        }

        if (_users.Remove(ServerSupport.Fold(oldNick), out var user))
        {
            user.Nick = newNick;
            _users[ServerSupport.Fold(newNick)] = user;
        }

        foreach (var channel in _channels.Values)
        {
            channel.RenameUser(oldNick, newNick);
        }

        if (_queries.Remove(ServerSupport.Fold(oldNick)))
        {
            _queries[ServerSupport.Fold(newNick)] = newNick;
        }

        foreach (var conversation in conversations)
        {
            var shownIn = ServerSupport.Fold(conversation) == ServerSupport.Fold(oldNick) ? newNick : conversation;
            RaiseActivity(message, IrcActivityKind.Nick, shownIn, oldNick, newNick, own: own);
        }
    }

    private void HandleTopic(IrcMessage message)
    {
        var name = message.GetParameter(0);
        if (name is null || GetChannel(name) is not { } channel)
        {
            return;
        }

        var topic = message.GetParameter(1);
        channel.Topic = string.IsNullOrEmpty(topic) ? null : topic;
        channel.TopicSetBy = message.Source?.Name;
        channel.TopicSetAt = MessageTime(message);
        RaiseActivity(message, IrcActivityKind.Topic, channel.Name, message.Source?.Name ?? string.Empty, channel.Topic, own: IsMe(message.Source?.Name));
    }

    private void SetTopicFromReply(string? name, string? topic)
    {
        if (name is not null && GetChannel(name) is { } channel)
        {
            channel.Topic = string.IsNullOrEmpty(topic) ? null : topic;
        }
    }

    private void SetTopicWhoTime(IrcMessage message)
    {
        if (message.GetParameter(1) is not { } name || GetChannel(name) is not { } channel)
        {
            return;
        }

        channel.TopicSetBy = message.GetParameter(2) is { } setter ? IrcSource.Parse(setter).Name : null;
        if (long.TryParse(message.GetParameter(3), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            channel.TopicSetAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
    }

    /// <summary><c>353</c>: <c>me = #channel :@op +voice user</c>; with multi-prefix a name may carry several symbols.</summary>
    private void AddNames(IrcMessage message)
    {
        if (message.GetParameter(2) is not { } name || GetChannel(name) is not { } channel)
        {
            return;
        }

        var entries = new List<(string, string)>();
        foreach (var item in (message.GetParameter(3) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var symbols = 0;
            while (symbols < item.Length && ServerSupport.PrefixSymbols.Contains(item[symbols], StringComparison.Ordinal))
            {
                symbols++;
            }

            if (symbols < item.Length)
            {
                entries.Add((item[symbols..], item[..symbols]));
            }
        }

        channel.AddPendingNames(entries);
    }

    private void EndNames(string? name)
    {
        if (name is null || GetChannel(name) is not { } channel)
        {
            return;
        }

        channel.ClearUsers();
        foreach (var (entry, symbols) in channel.TakePendingNames())
        {
            var user = TrackUser(IrcSource.Parse(entry)); // userhost-in-names gives nick!user@host
            var modes = symbols.Select(s => ServerSupport.ModeForSymbol(s)).OfType<char>();
            channel.AddUser(user, new string(modes.ToArray()));
        }
    }

    private void HandleAway(IrcMessage message)
    {
        if (message.Source is not { } source || GetUser(source.Name) is not { } user)
        {
            return;
        }

        var awayMessage = message.GetParameter(0);
        user.AwayMessage = awayMessage;
        var kind = awayMessage is null ? IrcActivityKind.Back : IrcActivityKind.Away;
        foreach (var conversation in ConversationsOf(source.Name))
        {
            RaiseActivity(message, kind, conversation, source.Name, awayMessage);
        }
    }

    private void HandleAccount(IrcMessage message)
    {
        if (message.Source is { } source && GetUser(source.Name) is { } user)
        {
            var account = message.GetParameter(0);
            user.Account = account is null or "*" ? null : account;
        }
    }

    /// <summary>Adds or updates the user of a source.</summary>
    private IrcUser TrackUser(IrcSource source)
    {
        var key = ServerSupport.Fold(source.Name);
        if (!_users.TryGetValue(key, out var user))
        {
            user = new IrcUser(source.Name);
            _users[key] = user;
        }

        user.Nick = source.Name;
        user.UserName = source.User ?? user.UserName;
        user.Host = source.Host ?? user.Host;
        return user;
    }

    /// <summary>Forgets a user who is in no channel and no private conversation any more.</summary>
    private void ForgetIfUnseen(string nick)
    {
        var key = ServerSupport.Fold(nick);
        if (!_queries.ContainsKey(key) && !_channels.Values.Any(c => c.HasUser(nick)))
        {
            _users.Remove(key);
        }
    }

    /// <summary>The channels a user is in, and the private conversation with that user when one is open.</summary>
    private List<string> ConversationsOf(string nick)
    {
        var conversations = _channels.Values.Where(c => c.HasUser(nick)).Select(c => c.Name).ToList();
        if (_queries.TryGetValue(ServerSupport.Fold(nick), out var query))
        {
            conversations.Add(query);
        }

        return conversations;
    }

    private IrcChannel GetOrAddChannel(string name)
    {
        var key = ServerSupport.Fold(name);
        if (!_channels.TryGetValue(key, out var channel))
        {
            channel = new IrcChannel(name, ServerSupport);
            _channels[key] = channel;
        }

        return channel;
    }

    /// <summary>The server changed its CASEMAPPING: fold every key again.</summary>
    private void Refold()
    {
        Rekey(_channels, c => c.Name);
        Rekey(_users, u => u.Nick);
        Rekey(_queries, q => q);
        foreach (var channel in _channels.Values)
        {
            channel.Refold();
        }
    }

    private void Rekey<T>(Dictionary<string, T> dictionary, Func<T, string> name)
    {
        var values = dictionary.Values.ToList();
        dictionary.Clear();
        foreach (var value in values)
        {
            dictionary[ServerSupport.Fold(name(value))] = value;
        }
    }

    private DateTimeOffset MessageTime(IrcMessage message)
    {
        var tag = message.GetTag(IrcTags.ServerTime);
        return tag is not null
               && DateTimeOffset.TryParse(tag, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : Now;
    }

    private void RaiseActivity(IrcMessage message, IrcActivityKind kind, string conversation, string nick, string? detail = null, string? target = null, bool own = false) =>
        Activity?.Invoke(this, new IrcActivity(MessageTime(message), kind, conversation, nick, detail, target, own));

    private void RaiseServerText(IrcMessage message) =>
        ServerText?.Invoke(this, new IrcServerText(MessageTime(message), message.Command, NumericText(message)));

    /// <summary>The text of a numeric without the client's own nickname first.</summary>
    private static string NumericText(IrcMessage message) =>
        string.Join(' ', message.Parameters.Skip(message.IsNumeric ? 1 : 0));
}
