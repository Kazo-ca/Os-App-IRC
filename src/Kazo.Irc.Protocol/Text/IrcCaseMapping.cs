namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// How a server compares nicknames and channel names (ISUPPORT <c>CASEMAPPING</c>).
/// </summary>
public enum IrcCaseMapping
{
    /// <summary><c>rfc1459</c>, the default: A-Z = a-z, and <c>[]\~</c> = <c>{}|^</c>.</summary>
    Rfc1459,

    /// <summary><c>strict-rfc1459</c>: A-Z = a-z, and <c>[]\</c> = <c>{}|</c> (not <c>~</c> and <c>^</c>).</summary>
    StrictRfc1459,

    /// <summary><c>ascii</c>: only A-Z = a-z.</summary>
    Ascii,
}

/// <summary>Folds names to the form a server compares them in.</summary>
public static class IrcCaseFolding
{
    /// <summary>Reads the value of the ISUPPORT <c>CASEMAPPING</c> token; an unknown value gives <see cref="IrcCaseMapping.Rfc1459"/>.</summary>
    public static IrcCaseMapping ParseMapping(string? value) => value?.ToLowerInvariant() switch
    {
        "ascii" => IrcCaseMapping.Ascii,
        "strict-rfc1459" => IrcCaseMapping.StrictRfc1459,
        _ => IrcCaseMapping.Rfc1459,
    };

    /// <summary>The lower-case form of <paramref name="name"/> under <paramref name="mapping"/>.</summary>
    public static string Fold(string name, IrcCaseMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(name);
        return string.Create(name.Length, (name, mapping), static (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = FoldChar(state.name[i], state.mapping);
            }
        });
    }

    /// <summary>True when the two names are the same name for the server.</summary>
    public static bool AreEqual(string? left, string? right, IrcCaseMapping mapping)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Length == right.Length && Fold(left, mapping) == Fold(right, mapping);
    }

    private static char FoldChar(char c, IrcCaseMapping mapping)
    {
        if (c is >= 'A' and <= 'Z')
        {
            return (char)(c + 32);
        }

        return mapping switch
        {
            IrcCaseMapping.Rfc1459 => c switch { '[' => '{', ']' => '}', '\\' => '|', '~' => '^', _ => c },
            IrcCaseMapping.StrictRfc1459 => c switch { '[' => '{', ']' => '}', '\\' => '|', _ => c },
            _ => c,
        };
    }
}
