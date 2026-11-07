using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// Cuts a byte stream (the relay's TCP stream) into IRC lines: a line ends with LF, a CR before it
/// is dropped, and each line is decoded by <see cref="IrcEncoding.Decode"/>.
/// </summary>
/// <remarks>
/// A WebSocket transport with the <c>text.ircv3.net</c> sub-protocol does not need it: there,
/// one frame is one line. A line longer than <see cref="IrcLimits.MaxReadLineBytes"/> without an
/// LF means the stream is not IRC, and <see cref="Append"/> throws.
/// </remarks>
public sealed class IrcLineBuffer
{
    private readonly List<byte> _pending = [];

    /// <summary>Bytes received that do not end a line yet.</summary>
    public int PendingBytes => _pending.Count;

    /// <summary>Adds received bytes and returns the lines they complete (empty lines are skipped).</summary>
    /// <exception cref="IrcProtocolException">A line grows past <see cref="IrcLimits.MaxReadLineBytes"/>.</exception>
    public IReadOnlyList<string> Append(ReadOnlySpan<byte> bytes)
    {
        var lines = new List<string>();
        foreach (var b in bytes)
        {
            if (b == (byte)'\n')
            {
                var count = _pending.Count;
                if (count > 0 && _pending[count - 1] == (byte)'\r')
                {
                    count--;
                }

                if (count > 0)
                {
                    lines.Add(IrcEncoding.Decode(_pending.ToArray().AsSpan(0, count)));
                }

                _pending.Clear();
                continue;
            }

            _pending.Add(b);
            if (_pending.Count > IrcLimits.MaxReadLineBytes)
            {
                _pending.Clear();
                throw new IrcProtocolException($"An IRC line grew past {IrcLimits.MaxReadLineBytes} bytes without an end.");
            }
        }

        return lines;
    }
}
