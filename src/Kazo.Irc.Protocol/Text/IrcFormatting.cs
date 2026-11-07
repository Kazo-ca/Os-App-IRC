using System.Globalization;
using System.Text;

namespace Kazo.Irc.Protocol.Text;

/// <summary>
/// How a piece of a message is drawn, from the IRC formatting codes before it: bold, italics,
/// underline, strikethrough, monospace, reverse and the mIRC colors.
/// </summary>
/// <param name="Bold">Bold (<c>\x02</c>).</param>
/// <param name="Italic">Italics (<c>\x1D</c>).</param>
/// <param name="Underline">Underlined (<c>\x1F</c>).</param>
/// <param name="Strikethrough">Struck through (<c>\x1E</c>).</param>
/// <param name="Monospace">Monospace (<c>\x11</c>).</param>
/// <param name="Reverse">Text and background colors swapped (<c>\x16</c>).</param>
/// <param name="Foreground">The mIRC text color, 0 to 98; null for the default.</param>
/// <param name="Background">The mIRC background color, 0 to 98; null for none.</param>
public sealed record IrcTextStyle(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false,
    bool Monospace = false,
    bool Reverse = false,
    int? Foreground = null,
    int? Background = null)
{
    /// <summary>Text with no formatting.</summary>
    public static IrcTextStyle Plain { get; } = new();

    /// <summary>True when nothing changes the text's look.</summary>
    public bool IsPlain => this == Plain;
}

/// <summary>A piece of a message drawn in one style, its formatting codes removed.</summary>
/// <param name="Text">The text, without any formatting code.</param>
/// <param name="Style">How it is drawn.</param>
public sealed record IrcTextRun(string Text, IrcTextStyle Style);

/// <summary>
/// The formatting codes clients put in messages (the de facto standard described at
/// <c>modern.ircdocs.horse/formatting</c>): read them into styled runs to draw a message, or remove
/// them for a log, a notification or a search.
/// </summary>
/// <remarks>
/// Codes: <c>\x02</c> bold, <c>\x1D</c> italics, <c>\x1F</c> underline, <c>\x1E</c> strikethrough,
/// <c>\x11</c> monospace, <c>\x16</c> reverse (each one toggles), <c>\x0F</c> resets everything,
/// <c>\x03</c> mIRC color (<c>\x03</c><i>fg</i>[<c>,</c><i>bg</i>], one or two digits each; alone it
/// resets the colors), <c>\x04</c> hexadecimal color (<c>\x04</c><i>RRGGBB</i>[<c>,</c><i>RRGGBB</i>]).
/// Hexadecimal colors are read and dropped (the text keeps its default colors): the app draws
/// colors with CSS classes, and Kazo OS forbids inline styles (ADR 0007). Color 99 means "default",
/// as in mIRC.
/// </remarks>
public static class IrcFormatting
{
    /// <summary>Bold.</summary>
    public const char Bold = '\x02';

    /// <summary>mIRC color.</summary>
    public const char Color = '\x03';

    /// <summary>Hexadecimal color.</summary>
    public const char HexColor = '\x04';

    /// <summary>Resets every style.</summary>
    public const char Reset = '\x0F';

    /// <summary>Monospace.</summary>
    public const char Monospace = '\x11';

    /// <summary>Reverse (text and background colors swapped).</summary>
    public const char Reverse = '\x16';

    /// <summary>Italics.</summary>
    public const char Italic = '\x1D';

    /// <summary>Strikethrough.</summary>
    public const char Strikethrough = '\x1E';

    /// <summary>Underline.</summary>
    public const char Underline = '\x1F';

    /// <summary>The mIRC color that means "the default color".</summary>
    public const int DefaultColor = 99;

    private const int HexDigits = 6;
    private const int MaxColorDigits = 2;

    private static readonly char[] CodeChars =
        [Bold, Color, HexColor, Reset, Monospace, Reverse, Italic, Strikethrough, Underline];

    /// <summary>True when <paramref name="text"/> holds at least one formatting code.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when there is something to read or remove.</returns>
    public static bool HasCodes(string? text) =>
        !string.IsNullOrEmpty(text) && text.AsSpan().IndexOfAny(CodeChars) >= 0;

    /// <summary>Removes every formatting code, keeping the text: <c>\x02hi\x02</c> gives <c>hi</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text alone; the same instance when it had no code.</returns>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!HasCodes(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var run in Parse(text))
        {
            builder.Append(run.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a message into runs of text that share a style. Codes are removed; runs with no text
    /// are not returned; two neighbors with the same style are one run.
    /// </summary>
    /// <param name="text">The message, as received.</param>
    /// <returns>The runs, in order; empty for an empty text.</returns>
    public static IReadOnlyList<IrcTextRun> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!HasCodes(text))
        {
            return text.Length == 0 ? [] : [new IrcTextRun(text, IrcTextStyle.Plain)];
        }

        var runs = new List<IrcTextRun>();
        var style = IrcTextStyle.Plain;
        var current = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (Array.IndexOf(CodeChars, c) < 0)
            {
                current.Append(c);
                i++;
                continue;
            }

            var next = Apply(text, ref i, style);
            if (next != style)
            {
                Flush(runs, current, style);
                style = next;
            }
        }

        Flush(runs, current, style);
        return runs;
    }

    /// <summary>Reads the code at <paramref name="i"/> (and its color digits), moving past it.</summary>
    private static IrcTextStyle Apply(string text, ref int i, IrcTextStyle style)
    {
        var code = text[i++];
        switch (code)
        {
            case Bold:
                return style with { Bold = !style.Bold };
            case Italic:
                return style with { Italic = !style.Italic };
            case Underline:
                return style with { Underline = !style.Underline };
            case Strikethrough:
                return style with { Strikethrough = !style.Strikethrough };
            case Monospace:
                return style with { Monospace = !style.Monospace };
            case Reverse:
                return style with { Reverse = !style.Reverse };
            case Reset:
                return IrcTextStyle.Plain;
            case Color:
                return ReadColor(text, ref i, style);
            default:
                SkipHexColor(text, ref i);
                return style with { Foreground = null, Background = null };
        }
    }

    /// <summary>
    /// <c>\x03</c>: one or two digits of text color, then optionally a comma and one or two digits
    /// of background. A comma not followed by a digit is text. No digit resets both colors.
    /// </summary>
    private static IrcTextStyle ReadColor(string text, ref int i, IrcTextStyle style)
    {
        var foreground = ReadNumber(text, ref i);
        if (foreground is null)
        {
            return style with { Foreground = null, Background = null };
        }

        var background = style.Background;
        if (i + 1 < text.Length && text[i] == ',' && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            background = Normalize(ReadNumber(text, ref i));
        }

        return style with { Foreground = Normalize(foreground), Background = background };
    }

    private static int? ReadNumber(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && i - start < MaxColorDigits && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        return i == start ? null : int.Parse(text.AsSpan(start, i - start), CultureInfo.InvariantCulture);
    }

    /// <summary>99 is "the default color".</summary>
    private static int? Normalize(int? color) => color is null or DefaultColor ? null : color;

    /// <summary><c>\x04</c>: six hexadecimal digits, then optionally a comma and six more.</summary>
    private static void SkipHexColor(string text, ref int i)
    {
        if (!IsHex(text, i))
        {
            return;
        }

        i += HexDigits;
        if (i < text.Length && text[i] == ',' && IsHex(text, i + 1))
        {
            i += 1 + HexDigits;
        }
    }

    private static bool IsHex(string text, int start)
    {
        if (start + HexDigits > text.Length)
        {
            return false;
        }

        for (var k = start; k < start + HexDigits; k++)
        {
            if (!char.IsAsciiHexDigit(text[k]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Flush(List<IrcTextRun> runs, StringBuilder current, IrcTextStyle style)
    {
        if (current.Length == 0)
        {
            return;
        }

        // Codes that cancel each other ("\x02\x02") leave the style as it was: one run, not two.
        if (runs.Count > 0 && runs[^1].Style == style)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text + current };
        }
        else
        {
            runs.Add(new IrcTextRun(current.ToString(), style));
        }

        current.Clear();
    }
}
