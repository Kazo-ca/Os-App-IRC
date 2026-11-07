namespace Kazo.Irc.Protocol.Messages;

/// <summary>
/// The numeric replies the client reads (RFC 1459 §6, RFC 2812 §5, IRCv3 SASL 3.1).
/// </summary>
public static class IrcNumerics
{
    /// <summary>The registration is complete; the first parameter is the nickname the server gave.</summary>
    public const string Welcome = "001";
    /// <summary>Features of the server (ISUPPORT): PREFIX, CHANTYPES, CASEMAPPING, NETWORK...</summary>
    public const string ISupport = "005";
    /// <summary>WHOIS: the target is away.</summary>
    public const string Away = "301";
    /// <summary>You are no longer marked away.</summary>
    public const string UnAway = "305";
    /// <summary>You are now marked away.</summary>
    public const string NowAway = "306";
    /// <summary>WHOIS: nick, user, host and real name.</summary>
    public const string WhoisUser = "311";
    /// <summary>WHOIS: the server the target is on.</summary>
    public const string WhoisServer = "312";
    /// <summary>WHOIS: the target is an IRC operator.</summary>
    public const string WhoisOperator = "313";
    /// <summary>WHOIS: seconds idle and signon time.</summary>
    public const string WhoisIdle = "317";
    /// <summary>End of the WHOIS reply.</summary>
    public const string EndOfWhois = "318";
    /// <summary>WHOIS: the channels of the target.</summary>
    public const string WhoisChannels = "319";
    /// <summary>The modes of a channel.</summary>
    public const string ChannelModeIs = "324";
    /// <summary>WHOIS: the account the target is logged in as.</summary>
    public const string WhoisAccount = "330";
    /// <summary>The channel has no topic.</summary>
    public const string NoTopic = "331";
    /// <summary>The topic of a channel.</summary>
    public const string Topic = "332";
    /// <summary>Who set the topic, and when.</summary>
    public const string TopicWhoTime = "333";
    /// <summary>One line of the NAMES reply.</summary>
    public const string NamesReply = "353";
    /// <summary>End of the NAMES reply.</summary>
    public const string EndOfNames = "366";
    /// <summary>The nickname or channel does not exist.</summary>
    public const string NoSuchNick = "401";
    /// <summary>The command is unknown (a server without CAP answers this to CAP).</summary>
    public const string UnknownCommand = "421";
    /// <summary>The nickname is not valid.</summary>
    public const string ErroneousNickname = "432";
    /// <summary>The nickname is already in use.</summary>
    public const string NicknameInUse = "433";
    /// <summary>The nickname collides with another one.</summary>
    public const string NickCollision = "436";
    /// <summary>SASL: you are now logged in.</summary>
    public const string LoggedIn = "900";
    /// <summary>SASL: you are now logged out.</summary>
    public const string LoggedOut = "901";
    /// <summary>SASL: the account is locked.</summary>
    public const string NickLocked = "902";
    /// <summary>SASL: the authentication succeeded.</summary>
    public const string SaslSuccess = "903";
    /// <summary>SASL: the authentication failed.</summary>
    public const string SaslFail = "904";
    /// <summary>SASL: the message was too long.</summary>
    public const string SaslTooLong = "905";
    /// <summary>SASL: the client aborted the authentication.</summary>
    public const string SaslAborted = "906";
    /// <summary>SASL: the client is already authenticated.</summary>
    public const string SaslAlready = "907";
    /// <summary>SASL: the mechanisms the server accepts.</summary>
    public const string SaslMechanisms = "908";
}
