namespace Kazo.Irc.Protocol.Messages;

/// <summary>
/// Length limits of the IRC line (RFC 1459 §2.3, RFC 2812 §2.3 and IRCv3 message-tags).
/// </summary>
/// <remarks>
/// The client is strict when it writes (a server would cut or refuse a longer line) and lenient
/// when it reads: a server that sends a longer line is still understood.
/// </remarks>
public static class IrcLimits
{
    /// <summary>Bytes of the message part (source, command, parameters) including the final CR LF.</summary>
    public const int MaxLineBytes = 512;

    /// <summary>Bytes of the tag part sent by a client, including the leading <c>@</c> and the space after it.</summary>
    public const int MaxClientTagBytes = 4096;

    /// <summary>Bytes of the tag part a server may send (IRCv3 message-tags: 8191, plus the leading <c>@</c> and the space).</summary>
    public const int MaxServerTagBytes = 8191;

    /// <summary>Bytes a line may hold while it is read from a byte stream before it is refused.</summary>
    public const int MaxReadLineBytes = MaxServerTagBytes + MaxLineBytes + 1;

    /// <summary>Bytes of the CR LF that ends every line on a byte stream.</summary>
    public const int LineEndingBytes = 2;
}
