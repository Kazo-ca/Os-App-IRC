namespace Kazo.Irc.Chat;

/// <summary>The lines typed in the input box, walked with the up and down arrows.</summary>
public sealed class InputHistory
{
    /// <summary>The lines kept.</summary>
    public const int Capacity = 100;

    private readonly List<string> _lines = [];
    private int _position;
    private string _draft = string.Empty;

    /// <summary>The lines, oldest first.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Adds a sent line (not twice in a row) and goes back to the end.</summary>
    /// <param name="line">The line sent.</param>
    public void Add(string line)
    {
        if (!string.IsNullOrWhiteSpace(line) && (_lines.Count == 0 || _lines[^1] != line))
        {
            _lines.Add(line);
            if (_lines.Count > Capacity)
            {
                _lines.RemoveAt(0);
            }
        }

        _position = _lines.Count;
        _draft = string.Empty;
    }

    /// <summary>The line before (up arrow); the first press keeps what was being typed.</summary>
    /// <param name="current">What the input holds now.</param>
    /// <returns>The older line, or <paramref name="current"/> at the oldest.</returns>
    public string Previous(string current)
    {
        if (_position == _lines.Count)
        {
            _draft = current;
        }

        if (_position == 0)
        {
            return current;
        }

        _position--;
        return _lines[_position];
    }

    /// <summary>The line after (down arrow); past the newest, what was being typed.</summary>
    /// <param name="current">What the input holds now.</param>
    /// <returns>The newer line, or the draft.</returns>
    public string Next(string current)
    {
        if (_position >= _lines.Count)
        {
            return current;
        }

        _position++;
        return _position == _lines.Count ? _draft : _lines[_position];
    }
}
