using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kazo.Irc.Settings;

/// <summary>
/// The app's settings, in <c>~/.config/ca.kazo.irc/settings.json</c> (the "Settings" row of
/// doc/en/plan.md). Work item 1 brings the console font (decision 19); work item 4 adds the logs
/// (decision 15).
/// </summary>
/// <param name="MonospaceFont">
/// Whether the messages, the channel and user lists use the OS's monospace font, for a console
/// look (decision 19). Off by default: the app follows the OS theme's interface font.
/// </param>
/// <param name="LogsEnabled">
/// Whether the conversations are written to <c>~/.local/share/ca.kazo.irc/logs/</c> (decision 15).
/// On by default; a file written before work item 4 has no such field and keeps them on.
/// </param>
public sealed record IrcSettings(bool MonospaceFont = false, bool LogsEnabled = true)
{
    /// <summary>Gets the settings file of one user.</summary>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    /// <returns><c>&lt;home&gt;/.config/ca.kazo.irc/settings.json</c>.</returns>
    public static string PathFor(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(homeDirectory);
        return homeDirectory.TrimEnd('/') + "/.config/ca.kazo.irc/settings.json";
    }

    /// <summary>Reads the settings; a missing, unreadable or broken file gives the defaults.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings read, or the defaults.</returns>
    public static async Task<IrcSettings> LoadAsync(IIrcFileSystem files, string homeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            byte[] bytes = await files.ReadAllBytesAsync(PathFor(homeDirectory), cancellationToken);
            return JsonSerializer.Deserialize(bytes, IrcSettingsJsonContext.Default.IrcSettings) ?? new IrcSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A first run has no file, and a file broken by hand must not stop the app.
            return new IrcSettings();
        }
    }

    /// <summary>Writes the settings, creating <c>~/.config/ca.kazo.irc/</c> when needed.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is written.</returns>
    public Task SaveAsync(IIrcFileSystem files, string homeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        string json = JsonSerializer.Serialize(this, IrcSettingsJsonContext.Default.IrcSettings);
        return files.WriteAllBytesAsync(PathFor(homeDirectory), Encoding.UTF8.GetBytes(json), cancellationToken);
    }
}

/// <summary>Trim-safe JSON of <see cref="IrcSettings"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(IrcSettings))]
internal sealed partial class IrcSettingsJsonContext : JsonSerializerContext;
