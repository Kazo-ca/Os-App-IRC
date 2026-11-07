namespace Kazo.Irc.Protocol.Model;

/// <summary>
/// A channel the client is in or was in: its topic, its modes and its users.
/// </summary>
/// <remarks>
/// The client keeps a channel after a disconnection, with <see cref="IsJoined"/> false and no users,
/// so it can join it again on reconnection. A <c>/part</c> or a kick removes the wish to be in it
/// (<see cref="WantsJoin"/>).
/// </remarks>
public sealed class IrcChannel
{
    private readonly IrcServerSupport _support;
    private readonly Dictionary<string, IrcChannelUser> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<char, string> _modes = [];
    private List<(string Nick, string Symbols)>? _pendingNames;

    internal IrcChannel(string name, IrcServerSupport support)
    {
        Name = name;
        _support = support;
    }

    /// <summary>The channel name, as the server wrote it the last time.</summary>
    public string Name { get; internal set; }

    /// <summary>True while the client is in the channel.</summary>
    public bool IsJoined { get; internal set; }

    /// <summary>True when the client wants to be in the channel: it joins it again after a reconnection.</summary>
    public bool WantsJoin { get; internal set; }

    /// <summary>The channel key (<c>+k</c>) used to join it, or null.</summary>
    public string? Key { get; internal set; }

    /// <summary>The topic, or null when there is none.</summary>
    public string? Topic { get; internal set; }

    /// <summary>Who set the topic, when the server said it.</summary>
    public string? TopicSetBy { get; internal set; }

    /// <summary>When the topic was set, when the server said it.</summary>
    public DateTimeOffset? TopicSetAt { get; internal set; }

    /// <summary>The channel modes known to the client, with their parameter (empty for a flag).</summary>
    public IReadOnlyDictionary<char, string> Modes => _modes;

    /// <summary>The users, highest mode first, then by nickname.</summary>
    public IReadOnlyList<IrcChannelUser> Users =>
        _users.Values
            .OrderBy(u => u.Modes.Length == 0 ? int.MaxValue : _support.PrefixModes.IndexOf(u.Modes[0], StringComparison.Ordinal))
            .ThenBy(u => u.Nick, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The number of users.</summary>
    public int UserCount => _users.Count;

    /// <summary>The user with this nickname, or null.</summary>
    public IrcChannelUser? GetUser(string nick) => _users.GetValueOrDefault(_support.Fold(nick));

    /// <summary>True when a user with this nickname is in the channel.</summary>
    public bool HasUser(string nick) => _users.ContainsKey(_support.Fold(nick));

    /// <inheritdoc />
    public override string ToString() => Name;

    internal IrcChannelUser AddUser(IrcUser user, string modes = "")
    {
        var key = _support.Fold(user.Nick);
        if (!_users.TryGetValue(key, out var member))
        {
            member = new IrcChannelUser(user);
            _users[key] = member;
        }

        SetModes(member, modes);
        return member;
    }

    internal bool RemoveUser(string nick) => _users.Remove(_support.Fold(nick));

    internal void RenameUser(string oldNick, string newNick)
    {
        if (_users.Remove(_support.Fold(oldNick), out var member))
        {
            _users[_support.Fold(newNick)] = member;
        }
    }

    internal void ClearUsers() => _users.Clear();

    internal void SetUserMode(string nick, char mode, bool add)
    {
        if (!_users.TryGetValue(_support.Fold(nick), out var member))
        {
            return;
        }

        var modes = add
            ? member.Modes + mode
            : member.Modes.Replace(mode.ToString(), string.Empty, StringComparison.Ordinal);
        SetModes(member, modes);
    }

    internal void SetMode(char mode, string? parameter, bool add)
    {
        if (add)
        {
            _modes[mode] = parameter ?? string.Empty;
        }
        else
        {
            _modes.Remove(mode);
        }

        if (mode == 'k')
        {
            Key = add ? parameter : null;
        }
    }

    internal void ClearModes() => _modes.Clear();

    /// <summary>Keeps the users of a <c>353</c> line until the <c>366</c> that ends the list.</summary>
    internal void AddPendingNames(IEnumerable<(string Nick, string Symbols)> names)
    {
        _pendingNames ??= [];
        _pendingNames.AddRange(names);
    }

    /// <summary>Replaces the users by the list received since the last <c>366</c>.</summary>
    internal IReadOnlyList<(string Nick, string Symbols)> TakePendingNames()
    {
        var names = (IReadOnlyList<(string Nick, string Symbols)>?)_pendingNames ?? [];
        _pendingNames = null;
        return names;
    }

    /// <summary>Folds every key again, after the server changed its CASEMAPPING.</summary>
    internal void Refold()
    {
        var members = _users.Values.ToList();
        _users.Clear();
        foreach (var member in members)
        {
            _users[_support.Fold(member.Nick)] = member;
        }
    }

    private void SetModes(IrcChannelUser member, string modes)
    {
        member.Modes = _support.SortPrefixModes(modes);
        member.Prefixes = _support.SymbolsForModes(member.Modes);
    }
}
