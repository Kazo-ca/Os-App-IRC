namespace Kazo.Irc.Settings;

/// <summary>
/// The few file operations of the app's settings and logs, so that the tests run them on memory; the app
/// uses <see cref="KazoIrcFileSystem"/>.
/// </summary>
public interface IIrcFileSystem
{
    /// <summary>Reads a whole file; throws an <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/>.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes a whole file, creating its folder and every missing parent.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <param name="bytes">The new content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>Appends UTF-8 text to a file, creating the file, its folder and every missing parent.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <param name="text">The text to add at the end.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendAllTextAsync(string path, string text, CancellationToken cancellationToken);
}
