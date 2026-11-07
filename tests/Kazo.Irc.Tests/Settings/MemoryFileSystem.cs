using Kazo.Irc.Settings;

namespace Kazo.Irc.Tests.Settings;

/// <summary>An <see cref="IIrcFileSystem"/> in memory, with switches that make every read or write fail.</summary>
internal sealed class MemoryFileSystem : IIrcFileSystem
{
    /// <summary>Gets the files, by full path.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the exception every read throws, if any.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>Gets or sets the exception every write throws, if any.</summary>
    public Exception? WriteFailure { get; set; }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        return Files.TryGetValue(path, out byte[]? bytes)
            ? Task.FromResult(bytes)
            : throw new FileNotFoundException(path);
    }

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        Files[path] = bytes;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendAllTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        lock (Files)
        {
            byte[] before = Files.TryGetValue(path, out byte[]? existing) ? existing : [];
            Files[path] = [.. before, .. System.Text.Encoding.UTF8.GetBytes(text)];
        }

        return Task.CompletedTask;
    }

    /// <summary>Reads a file as UTF-8 text.</summary>
    public string Text(string path)
    {
        lock (Files)
        {
            return System.Text.Encoding.UTF8.GetString(Files[path]);
        }
    }
}
