namespace Kazo.Irc.Protocol.Messages;

/// <summary>
/// The source (prefix) of a message: <c>nick!user@host</c> for a user, a server name for a server.
/// </summary>
/// <param name="Name">The nickname, or the server name.</param>
/// <param name="User">The user name (ident), when the source gives it.</param>
/// <param name="Host">The host, when the source gives it.</param>
public sealed record IrcSource(string Name, string? User = null, string? Host = null)
{
    /// <summary>The nickname, or the server name: the same value as <see cref="Name"/>.</summary>
    public string Nick => Name;

    /// <summary>
    /// True when the source looks like a server: no user, no host, and a dot in the name (a
    /// nickname cannot hold a dot).
    /// </summary>
    public bool IsServer => User is null && Host is null && Name.Contains('.', StringComparison.Ordinal);

    /// <summary>Reads <c>nick!user@host</c>, <c>nick@host</c>, <c>nick</c> or a server name.</summary>
    /// <exception cref="ArgumentException">The source is empty.</exception>
    public static IrcSource Parse(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        string? host = null;
        string? user = null;
        var rest = source;
        var at = rest.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            host = rest[(at + 1)..];
            rest = rest[..at];
        }

        var bang = rest.IndexOf('!', StringComparison.Ordinal);
        if (bang >= 0)
        {
            user = rest[(bang + 1)..];
            rest = rest[..bang];
        }

        return new IrcSource(rest, user, host);
    }

    /// <summary>The source as it is written on the line, without the leading colon.</summary>
    public override string ToString()
    {
        var text = Name;
        if (User is not null)
        {
            text += "!" + User;
        }

        if (Host is not null)
        {
            text += "@" + Host;
        }

        return text;
    }
}
