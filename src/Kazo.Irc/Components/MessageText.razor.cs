using System.Globalization;
using System.Text;
using Kazo.Irc.Protocol.Text;
using Kazo.Irc.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Kazo.Irc.Components;

/// <summary>
/// Draws the text of a message (work item 5): the IRC formatting codes as styles, the web addresses
/// as links (decision 17).
/// </summary>
/// <remarks>
/// Styles are CSS classes of <c>MessageText.razor.css</c>, never inline styles (ADR 0007): the
/// sixteen standard mIRC colors (0 to 15) have a class each; the extended colors (16 to 98) and
/// the hexadecimal ones are drawn in the default colors.
/// </remarks>
public sealed partial class MessageText
{
    /// <summary>The colors drawn: the sixteen standard mIRC colors.</summary>
    public const int DrawnColors = 16;

    private string? _parsedText;
    private IReadOnlyList<MessageSegment> _segments = [];

    /// <summary>Gets or sets the text as received (it may hold formatting codes).</summary>
    [Parameter, EditorRequired]
    public string? Text { get; set; }

    /// <summary>Gets or sets what a click on a link does; without it a link does nothing.</summary>
    [Parameter]
    public Func<Uri, Task>? OnOpenLink { get; set; }

    private IReadOnlyList<MessageSegment> Segments
    {
        get
        {
            // A line's text never changes: read it once, not at every render of the conversation.
            if (!string.Equals(_parsedText, Text, StringComparison.Ordinal))
            {
                _parsedText = Text;
                _segments = MessageSegments.Build(Text);
            }

            return _segments;
        }
    }

    /// <summary>The CSS classes of a style, for example <c>irc-b irc-fg-4</c>.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The classes, separated by spaces; empty for plain text.</returns>
    public static string ClassOf(IrcTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var classes = new StringBuilder();
        Add(classes, style.Bold, "irc-b");
        Add(classes, style.Italic, "irc-i");
        Add(classes, style.Underline, "irc-u");
        Add(classes, style.Strikethrough, "irc-s");
        Add(classes, style.Monospace, "irc-m");

        // Reverse swaps the two colors; a missing one is the default of the other side.
        var foreground = Drawn(style.Reverse ? style.Background : style.Foreground);
        var background = Drawn(style.Reverse ? style.Foreground : style.Background);
        if (style.Reverse)
        {
            Add(classes, foreground is null, "irc-fg-reverse");
            Add(classes, background is null, "irc-bg-reverse");
        }

        Add(classes, foreground is not null, string.Create(CultureInfo.InvariantCulture, $"irc-fg-{foreground}"));
        Add(classes, background is not null, string.Create(CultureInfo.InvariantCulture, $"irc-bg-{background}"));
        return classes.ToString();
    }

    private static int? Drawn(int? color) => color is >= 0 and < DrawnColors ? color : null;

    private static void Add(StringBuilder classes, bool condition, string name)
    {
        if (!condition)
        {
            return;
        }

        if (classes.Length > 0)
        {
            classes.Append(' ');
        }

        classes.Append(name);
    }

    private Task OpenAsync(Uri link) => OnOpenLink?.Invoke(link) ?? Task.CompletedTask;

    /// <summary>Enter opens a focused link, as it does for a real link.</summary>
    private Task OnKeyDownAsync(KeyboardEventArgs e, Uri link) => e.Key == "Enter" ? OpenAsync(link) : Task.CompletedTask;
}
