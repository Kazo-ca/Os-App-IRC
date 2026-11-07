using Kazo.Os.AppSdk;
using Kazo.Os.Simulation.Abstractions.Notifications;

namespace Kazo.Irc.Chat;

/// <summary>
/// <see cref="IIrcNotifier"/> on the OS notifications (<c>notifications.post</c>): the OS shows a
/// bubble, keeps it in the notification panel and plays the notification sound (decision 14).
/// </summary>
/// <param name="context">The app's execution context.</param>
public sealed class KazoIrcNotifier(IAppExecutionContext context) : IIrcNotifier
{
    /// <inheritdoc />
    public void Notify(string title, string message)
    {
        try
        {
            context.Notifications.Post(NotificationSeverity.Information, title, message);
        }
        catch (UnauthorizedAccessException)
        {
            // The user withdrew notifications.post: the window title's counter still tells.
        }
    }
}
