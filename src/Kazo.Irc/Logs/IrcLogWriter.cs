using Kazo.Irc.Chat;
using Kazo.Irc.Settings;

namespace Kazo.Irc.Logs;

/// <summary>
/// Appends the lines of the conversations to their log files (decision 15), one write at a time
/// and in order. A failed write is reported once by <see cref="Failed"/> and does not stop the
/// chat.
/// </summary>
public sealed class IrcLogWriter
{
    private readonly IIrcFileSystem _files;
    private readonly string _home;
    private readonly Func<TimeZoneInfo> _timeZone;
    private Task _tail = Task.CompletedTask;
    private bool _failureReported;

    /// <summary>Creates the writer.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="timeZone">The OS's time zone (read at each line: the user may change it).</param>
    public IrcLogWriter(IIrcFileSystem files, string homeDirectory, Func<TimeZoneInfo>? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(homeDirectory);
        _files = files;
        _home = homeDirectory;
        _timeZone = timeZone ?? (() => TimeZoneInfo.Utc);
    }

    /// <summary>The first write that failed (the next ones are not reported again).</summary>
    public event Action<Exception>? Failed;

    /// <summary>The writes queued so far: completes once they are all done.</summary>
    public Task Idle => _tail;

    /// <summary>Queues a line of a conversation; the server tab and server lines are not logged.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="line">The line.</param>
    /// <returns>True when the line is queued.</returns>
    public bool Append(Conversation conversation, ChatLine line)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(line);
        if (conversation.Kind == ConversationKind.Server)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(line.Time, _timeZone());
        if (IrcLog.Format(line, local) is not { } text)
        {
            return false;
        }

        // One folder per conversation whatever the case the server used (#Kazo and #kazo).
        var name = conversation.Session.Client.ServerSupport.Fold(conversation.Name);
        var path = IrcLog.PathFor(_home, conversation.Session.Network.Name, name, DateOnly.FromDateTime(local.DateTime));
        _tail = WriteAfterAsync(_tail, path, text + "\n");
        return true;
    }

    private async Task WriteAfterAsync(Task previous, string path, string text)
    {
        await previous;
        try
        {
            await _files.AppendAllTextAsync(path, text, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Any failure (a denied folder, a full disk, an error of the file system) is reported
            // and never breaks the queue: the next lines are still tried, and the chat goes on.
            if (!_failureReported)
            {
                _failureReported = true;
                Failed?.Invoke(exception);
            }
        }
    }
}
