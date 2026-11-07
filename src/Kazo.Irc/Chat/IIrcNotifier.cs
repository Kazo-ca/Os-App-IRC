namespace Kazo.Irc.Chat;

/// <summary>Shows an OS notification (with its sound); tests record them instead.</summary>
public interface IIrcNotifier
{
    /// <summary>Posts a notification.</summary>
    /// <param name="title">Its title: who wrote, and where.</param>
    /// <param name="message">The message.</param>
    void Notify(string title, string message);
}
