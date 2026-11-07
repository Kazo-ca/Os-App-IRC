namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// Client-to-client queries (CTCP) carried inside PRIVMSG and NOTICE between two <c>\x01</c>:
/// <c>ACTION</c> for <c>/me</c>, and the <c>VERSION</c>, <c>PING</c>, <c>TIME</c>, <c>CLIENTINFO</c>
/// queries a real client answers.
/// </summary>
public static class IrcCtcp
{
    /// <summary>The character that opens and closes a CTCP message.</summary>
    public const char Delimiter = '\x01';

    /// <summary>The CTCP command of <c>/me</c>.</summary>
    public const string Action = "ACTION";

    /// <summary>Reads a CTCP message, or returns false when the text is a plain message.</summary>
    /// <remarks>The closing <c>\x01</c> may be missing, as some clients forget it.</remarks>
    public static bool TryParse(string text, out string command, out string arguments)
    {
        command = string.Empty;
        arguments = string.Empty;
        if (text.Length < 2 || text[0] != Delimiter)
        {
            return false;
        }

        var body = text[1..];
        if (body.EndsWith(Delimiter))
        {
            body = body[..^1];
        }

        if (body.Length == 0)
        {
            return false;
        }

        var space = body.IndexOf(' ', StringComparison.Ordinal);
        command = (space < 0 ? body : body[..space]).ToUpperInvariant();
        arguments = space < 0 ? string.Empty : body[(space + 1)..];
        return true;
    }

    /// <summary>Writes a CTCP message.</summary>
    public static string Format(string command, string? arguments = null) =>
        string.IsNullOrEmpty(arguments)
            ? $"{Delimiter}{command}{Delimiter}"
            : $"{Delimiter}{command} {arguments}{Delimiter}";
}
