using System.Globalization;
using System.Text;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Model;

/// <summary>
/// What the server says it supports in its <c>005</c> (ISUPPORT) replies: the channel prefixes,
/// the user modes of a channel and their symbols, how names compare, the channel modes that take a
/// parameter, and the network's name.
/// </summary>
/// <remarks>Before any <c>005</c>, the values are the RFC defaults.</remarks>
public sealed class IrcServerSupport
{
    private const string DefaultPrefixModes = "ov";
    private const string DefaultPrefixSymbols = "@+";
    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every token received, values unescaped (<c>\x20</c> is a space); a token without a value has the empty string.</summary>
    public IReadOnlyDictionary<string, string> Tokens => _tokens;

    /// <summary>The network's name (<c>NETWORK</c>), or null.</summary>
    public string? NetworkName => _tokens.GetValueOrDefault("NETWORK");

    /// <summary>How names compare (<c>CASEMAPPING</c>).</summary>
    public IrcCaseMapping CaseMapping => IrcCaseFolding.ParseMapping(_tokens.GetValueOrDefault("CASEMAPPING"));

    /// <summary>The characters a channel name starts with (<c>CHANTYPES</c>, default <c>#&amp;</c>).</summary>
    public string ChannelTypes => _tokens.GetValueOrDefault("CHANTYPES") ?? "#&";

    /// <summary>User modes of a channel, highest first (<c>PREFIX=(ov)@+</c> gives <c>ov</c>).</summary>
    public string PrefixModes { get; private set; } = DefaultPrefixModes;

    /// <summary>Symbols of those modes, in the same order (<c>@+</c>).</summary>
    public string PrefixSymbols { get; private set; } = DefaultPrefixSymbols;

    /// <summary>Channel modes of type A (lists, such as bans): always a parameter.</summary>
    public string ListModes => ChannelModeGroup(0, "beI");

    /// <summary>Channel modes of type B (such as the key): always a parameter.</summary>
    public string AlwaysParameterModes => ChannelModeGroup(1, "k");

    /// <summary>Channel modes of type C (such as the limit): a parameter only when set.</summary>
    public string SetParameterModes => ChannelModeGroup(2, "l");

    /// <summary>Channel modes of type D (flags): never a parameter.</summary>
    public string FlagModes => ChannelModeGroup(3, "imnpst");

    /// <summary>The longest nickname the server accepts (<c>NICKLEN</c>), or null.</summary>
    public int? NickLength =>
        int.TryParse(_tokens.GetValueOrDefault("NICKLEN"), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            ? length
            : null;

    /// <summary>True when <paramref name="name"/> is a channel name (starts with a <see cref="ChannelTypes"/> character).</summary>
    public bool IsChannel(string? name) => !string.IsNullOrEmpty(name) && ChannelTypes.Contains(name[0], StringComparison.Ordinal);

    /// <summary>The lower-case form of a name for comparisons.</summary>
    public string Fold(string name) => IrcCaseFolding.Fold(name, CaseMapping);

    /// <summary>The symbol of a channel user mode (<c>o</c> → <c>@</c>), or null.</summary>
    public char? SymbolForMode(char mode)
    {
        var index = PrefixModes.IndexOf(mode, StringComparison.Ordinal);
        return index >= 0 ? PrefixSymbols[index] : null;
    }

    /// <summary>The channel user mode of a symbol (<c>@</c> → <c>o</c>), or null.</summary>
    public char? ModeForSymbol(char symbol)
    {
        var index = PrefixSymbols.IndexOf(symbol, StringComparison.Ordinal);
        return index >= 0 ? PrefixModes[index] : null;
    }

    /// <summary>Puts <paramref name="modes"/> in rank order, highest first, and drops unknown or repeated ones.</summary>
    public string SortPrefixModes(IEnumerable<char> modes)
    {
        var set = new HashSet<char>(modes);
        return new string(PrefixModes.Where(set.Contains).ToArray());
    }

    /// <summary>The symbols of <paramref name="modes"/>, highest first (<c>ov</c> → <c>@+</c>).</summary>
    public string SymbolsForModes(string modes) =>
        new(SortPrefixModes(modes).Select(m => SymbolForMode(m)!.Value).ToArray());

    /// <summary>Reads the tokens of one <c>005</c> reply (the first parameter is the nickname, the last one the text).</summary>
    public void Apply(IReadOnlyList<string> parameters)
    {
        for (var i = 1; i < parameters.Count - 1; i++)
        {
            ApplyToken(parameters[i]);
        }
    }

    /// <summary>Forgets every token, before a new connection.</summary>
    public void Reset()
    {
        _tokens.Clear();
        PrefixModes = DefaultPrefixModes;
        PrefixSymbols = DefaultPrefixSymbols;
    }

    private void ApplyToken(string token)
    {
        if (token.Length == 0)
        {
            return;
        }

        if (token[0] == '-')
        {
            var removed = token[1..];
            _tokens.Remove(removed);
            if (removed.Equals("PREFIX", StringComparison.OrdinalIgnoreCase))
            {
                PrefixModes = DefaultPrefixModes;
                PrefixSymbols = DefaultPrefixSymbols;
            }

            return;
        }

        var equals = token.IndexOf('=', StringComparison.Ordinal);
        var key = equals < 0 ? token : token[..equals];
        var value = equals < 0 ? string.Empty : UnescapeValue(token[(equals + 1)..]);
        _tokens[key] = value;
        if (key.Equals("PREFIX", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPrefix(value);
        }
    }

    private void ApplyPrefix(string value)
    {
        // PREFIX=(qaohv)~&@%+ ; an empty value means the server has no user modes.
        var close = value.IndexOf(')', StringComparison.Ordinal);
        if (value.StartsWith('(') && close > 0)
        {
            var modes = value[1..close];
            var symbols = value[(close + 1)..];
            if (modes.Length == symbols.Length)
            {
                PrefixModes = modes;
                PrefixSymbols = symbols;
            }
        }
        else if (value.Length == 0)
        {
            PrefixModes = string.Empty;
            PrefixSymbols = string.Empty;
        }
    }

    private string ChannelModeGroup(int index, string fallback)
    {
        if (!_tokens.TryGetValue("CHANMODES", out var value))
        {
            return fallback;
        }

        var groups = value.Split(',');
        return index < groups.Length ? groups[index] : string.Empty;
    }

    private static string UnescapeValue(string value)
    {
        if (!value.Contains("\\x", StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length && value[i + 1] == 'x'
                && byte.TryParse(value.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                builder.Append((char)code);
                i += 3;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }
}
