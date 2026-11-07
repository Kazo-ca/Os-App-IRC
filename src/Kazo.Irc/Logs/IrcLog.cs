using System.Globalization;
using System.Text;
using Kazo.Irc.Chat;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Logs;

/// <summary>
/// Where and how a conversation is logged (decision 15): one text file per day in
/// <c>~/.local/share/ca.kazo.irc/logs/&lt;network&gt;/&lt;conversation&gt;/YYYY-MM-DD.log</c>, one line
/// per message: <c>[HH:MM:SS] &lt;nick&gt; text</c>.
/// </summary>
public static class IrcLog
{
    /// <summary>The longest folder name made from a network or conversation name.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The logs folder of one user.</summary>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    /// <returns><c>&lt;home&gt;/.local/share/ca.kazo.irc/logs</c>.</returns>
    public static string RootFor(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(homeDirectory);
        return homeDirectory.TrimEnd('/') + "/.local/share/ca.kazo.irc/logs";
    }

    /// <summary>The log file of a conversation for one day.</summary>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="network">The network's name.</param>
    /// <param name="conversation">The channel or nickname, already folded to one case.</param>
    /// <param name="day">The local day of the line.</param>
    /// <returns>The file's absolute path.</returns>
    public static string PathFor(string homeDirectory, string network, string conversation, DateOnly day) =>
        $"{RootFor(homeDirectory)}/{SafeName(network)}/{SafeName(conversation)}/{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

    /// <summary>
    /// A name made safe as one folder: no <c>/</c>, <c>\</c>, control character or NUL (each
    /// becomes <c>_</c>), never <c>.</c> nor <c>..</c> nor a hidden name, never empty, at most
    /// <see cref="MaxNameLength"/> characters. A channel name from the network can therefore never
    /// leave the logs folder.
    /// </summary>
    /// <param name="name">A network, channel or nickname.</param>
    /// <returns>The folder name.</returns>
    public static string SafeName(string? name)
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            builder.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? '_' : c);
        }

        var safe = builder.ToString().Trim();
        if (safe.Length > MaxNameLength)
        {
            safe = safe[..MaxNameLength];
        }

        if (safe.Length == 0)
        {
            return "_";
        }

        // ".", "..", ".hidden": a leading dot becomes "_".
        return safe[0] == '.' ? "_" + safe[1..] : safe;
    }

    /// <summary>One line of a log, without its line end.</summary>
    /// <param name="line">The line.</param>
    /// <param name="local">The line's time, in the OS's time zone.</param>
    /// <returns>For example <c>[14:05:09] &lt;neo&gt; hello</c>; null for a line that is not logged (server lines).</returns>
    public static string? Format(ChatLine line, DateTimeOffset local)
    {
        ArgumentNullException.ThrowIfNull(line);
        var time = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var text = OneLine(line.Text);
        return line.Kind switch
        {
            ChatLineKind.Message => $"[{time}] <{line.Nick}> {text}",
            ChatLineKind.Action => $"[{time}] * {line.Nick} {text}",
            ChatLineKind.Notice => $"[{time}] -{line.Nick}- {text}",
            ChatLineKind.Activity => $"[{time}] *** {Activity(line, text)}",
            _ => null,
        };
    }

    private static string Activity(ChatLine line, string detail)
    {
        var reason = detail.Length > 0 ? $" ({detail})" : string.Empty;
        return line.Activity switch
        {
            IrcActivityKind.Join => $"{line.Nick} joined",
            IrcActivityKind.Part => $"{line.Nick} left{reason}",
            IrcActivityKind.Kick => $"{line.TargetNick} was kicked by {line.Nick}{reason}",
            IrcActivityKind.Quit => $"{line.Nick} quit{reason}",
            IrcActivityKind.Nick => $"{line.Nick} is now known as {detail}",
            IrcActivityKind.Topic => $"{line.Nick} changed the topic to: {detail}",
            IrcActivityKind.Mode => $"{line.Nick} sets mode {detail}",
            IrcActivityKind.Away => $"{line.Nick} is away{reason}",
            IrcActivityKind.Back => $"{line.Nick} is back",
            _ => $"{line.Nick} {detail}",
        };
    }

    /// <summary>One line of plain text: the IRC formatting codes removed (work item 5), line ends made spaces.</summary>
    private static string OneLine(string text) => IrcFormatting.Strip(text).Replace('\r', ' ').Replace('\n', ' ');
}
