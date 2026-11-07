using System.Text;

namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// The text encoding of IRC lines: UTF-8 when the client writes; when it reads, UTF-8, and
/// Latin-1 for a line that is not valid UTF-8 (old clients and networks still send Latin-1).
/// </summary>
/// <remarks>The choice is made per line, as most IRC clients do: one bad line does not change the next ones.</remarks>
public static class IrcEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The encoding every line is written in: UTF-8 without a byte order mark.</summary>
    public static Encoding Write { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Decodes one line: UTF-8 when it is valid, Latin-1 otherwise.</summary>
    public static string Decode(ReadOnlySpan<byte> line)
    {
        try
        {
            return StrictUtf8.GetString(line);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(line);
        }
    }

    /// <summary>Encodes one line in UTF-8.</summary>
    public static byte[] Encode(string line) => Write.GetBytes(line);

    /// <summary>The number of bytes <paramref name="text"/> holds in UTF-8.</summary>
    public static int ByteCount(string text) => Write.GetByteCount(text);
}
