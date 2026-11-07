namespace Kazo.Irc.Protocol.Model;

/// <summary>
/// A user the client knows on a network: someone in one of its channels or in a private
/// conversation. The same object is shared by every channel the user is in.
/// </summary>
public sealed class IrcUser
{
    /// <summary>Creates a user.</summary>
    public IrcUser(string nick)
    {
        ArgumentException.ThrowIfNullOrEmpty(nick);
        Nick = nick;
    }

    /// <summary>The current nickname.</summary>
    public string Nick { get; internal set; }

    /// <summary>The user name (ident), once a message from the user showed it.</summary>
    public string? UserName { get; internal set; }

    /// <summary>The host, once a message from the user showed it.</summary>
    public string? Host { get; internal set; }

    /// <summary>The services account the user is logged in as (IRCv3 account-notify), or null.</summary>
    public string? Account { get; internal set; }

    /// <summary>The away message (IRCv3 away-notify, or a WHOIS), or null when the user is here.</summary>
    public string? AwayMessage { get; internal set; }

    /// <summary>True when the user is marked away.</summary>
    public bool IsAway => AwayMessage is not null;

    /// <inheritdoc />
    public override string ToString() => Nick;
}
