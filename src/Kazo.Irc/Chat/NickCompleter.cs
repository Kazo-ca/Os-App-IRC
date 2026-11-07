namespace Kazo.Irc.Chat;

/// <summary>
/// Tab completion of nicknames in the input box: completes the last word with the nicknames of
/// the conversation; each further Tab gives the next match. At the start of the line it adds
/// <c>": "</c>, as IRC users write to address someone.
/// </summary>
public sealed class NickCompleter
{
    private string? _lastResult;
    private string _stem = string.Empty;
    private List<string> _matches = [];
    private int _index;

    /// <summary>Completes <paramref name="input"/>.</summary>
    /// <param name="input">The text of the input box (the word to complete is its end).</param>
    /// <param name="nicks">The nicknames that can be offered.</param>
    /// <returns>The completed text, or <paramref name="input"/> when nothing matches.</returns>
    public string Complete(string input, IEnumerable<string> nicks)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(nicks);
        if (_lastResult is not null && input == _lastResult && _matches.Count > 0)
        {
            _index = (_index + 1) % _matches.Count;
            return _lastResult = Build(_matches[_index]);
        }

        var start = input.LastIndexOf(' ') + 1;
        var word = input[start..];
        if (word.Length == 0)
        {
            Reset();
            return input;
        }

        _stem = input[..start];
        _matches = nicks
            .Where(nick => nick.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_matches.Count == 0)
        {
            Reset();
            return input;
        }

        _index = 0;
        return _lastResult = Build(_matches[0]);
    }

    /// <summary>Forgets the cycle (the user typed something else).</summary>
    public void Reset()
    {
        _lastResult = null;
        _matches = [];
        _index = 0;
    }

    private string Build(string nick) => _stem.Length == 0 ? nick + ": " : _stem + nick + " ";
}
