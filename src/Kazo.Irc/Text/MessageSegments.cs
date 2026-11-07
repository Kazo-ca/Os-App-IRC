using System.Text;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Text;

/// <summary>
/// A piece of a message as the window draws it: styled runs of text, and the web address they open
/// when they are a link.
/// </summary>
/// <param name="Runs">The text, in its styles (formatting codes removed).</param>
/// <param name="Link">The address the piece opens; null for plain text.</param>
public sealed record MessageSegment(IReadOnlyList<IrcTextRun> Runs, Uri? Link)
{
    /// <summary>The text of the piece, without its styles.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>
/// Turns a message as received into what the window draws: the IRC formatting codes read into
/// styles (<see cref="IrcFormatting"/>) and the web addresses made links (<see cref="MessageLinks"/>).
/// </summary>
/// <remarks>
/// Everything stays text: the window renders each run as a text node, so nothing from the network
/// is ever read as HTML (<c>&lt;b&gt;</c> is shown as typed). A formatting code ends an address, so
/// a link is always one run (styled when the codes wrap it: <c>\x02https://kazo.ca\x02</c>).
/// </remarks>
public static class MessageSegments
{
    /// <summary>Reads <paramref name="text"/> into segments.</summary>
    /// <param name="text">The message, as received (it may hold formatting codes).</param>
    /// <returns>The segments, in order; empty for an empty text.</returns>
    public static IReadOnlyList<MessageSegment> Build(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var runs = IrcFormatting.Parse(text);
        var plain = new StringBuilder(text.Length);
        var breaks = new HashSet<int>();
        foreach (var run in runs)
        {
            // Two runs meet where a formatting code stood: an address ends there.
            if (plain.Length > 0)
            {
                breaks.Add(plain.Length);
            }

            plain.Append(run.Text);
        }

        var links = MessageLinks.Find(plain.ToString(), breaks);
        if (links.Count == 0)
        {
            return runs.Count == 0 ? [] : [new MessageSegment(runs, null)];
        }

        return Split(runs, links);
    }

    /// <summary>Cuts the runs at the links' edges and groups the pieces by link.</summary>
    private static List<MessageSegment> Split(IReadOnlyList<IrcTextRun> runs, IReadOnlyList<MessageLink> links)
    {
        var segments = new List<MessageSegment>();
        var pieces = new List<IrcTextRun>();
        Uri? pieceLink = null;
        var offset = 0;
        var linkIndex = 0;

        foreach (var run in runs)
        {
            var position = 0;
            while (position < run.Text.Length)
            {
                var absolute = offset + position;
                while (linkIndex < links.Count && links[linkIndex].Start + links[linkIndex].Length <= absolute)
                {
                    linkIndex++;
                }

                var link = linkIndex < links.Count ? links[linkIndex] : null;
                int end;
                Uri? current;
                if (link is not null && absolute >= link.Start)
                {
                    current = link.Uri;
                    end = link.Start + link.Length;
                }
                else
                {
                    current = null;
                    end = link?.Start ?? int.MaxValue;
                }

                var take = Math.Min(run.Text.Length - position, end - absolute);
                if (pieces.Count > 0 && !ReferenceEquals(current, pieceLink))
                {
                    segments.Add(new MessageSegment([.. pieces], pieceLink));
                    pieces.Clear();
                }

                pieceLink = current;
                pieces.Add(run with { Text = run.Text.Substring(position, take) });
                position += take;
            }

            offset += run.Text.Length;
        }

        if (pieces.Count > 0)
        {
            segments.Add(new MessageSegment([.. pieces], pieceLink));
        }

        return segments;
    }
}
