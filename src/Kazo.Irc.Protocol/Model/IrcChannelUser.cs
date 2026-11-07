namespace Kazo.Irc.Protocol.Model;

/// <summary>A user in one channel, with the user modes it has there (operator, voice...).</summary>
public sealed class IrcChannelUser
{
    internal IrcChannelUser(IrcUser user)
    {
        User = user;
    }

    /// <summary>The user (shared with the other channels).</summary>
    public IrcUser User { get; }

    /// <summary>The user's current nickname.</summary>
    public string Nick => User.Nick;

    /// <summary>The channel user modes, highest first (<c>ov</c>).</summary>
    public string Modes { get; internal set; } = string.Empty;

    /// <summary>The symbols of those modes, highest first (<c>@+</c>; with IRCv3 multi-prefix the server sends them all).</summary>
    public string Prefixes { get; internal set; } = string.Empty;

    /// <summary>The highest symbol (<c>@</c>), or null for a user with no mode.</summary>
    public char? HighestPrefix => Prefixes.Length > 0 ? Prefixes[0] : null;

    /// <inheritdoc />
    public override string ToString() => Prefixes.Length > 0 ? Prefixes[0] + Nick : Nick;
}
