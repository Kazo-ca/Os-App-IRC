using Kazo.Os.AppSdk;

namespace Kazo.Irc.Settings;

/// <summary>
/// Runs every operation of another <see cref="IIrcFileSystem"/> inside the app's execution context.
/// </summary>
/// <remarks>
/// The OS opens that context around the window's own code (events, lifecycle). The logs are
/// written from the IRC client's read loop, a flow the OS did not start: there,
/// <c>KazoFile</c> finds no app context and refuses. This wrapper enters it for each operation
/// (<see cref="KazoExecutionContext.Enter"/>), as the SDK asks for a flow started outside the app.
/// </remarks>
/// <param name="context">The app's execution context.</param>
/// <param name="inner">The file system that does the work.</param>
public sealed class AppScopedFileSystem(IAppExecutionContext context, IIrcFileSystem inner) : IIrcFileSystem
{
    /// <inheritdoc />
    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        using IDisposable scope = KazoExecutionContext.Enter(context);
        return await inner.ReadAllBytesAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        using IDisposable scope = KazoExecutionContext.Enter(context);
        await inner.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AppendAllTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        using IDisposable scope = KazoExecutionContext.Enter(context);
        await inner.AppendAllTextAsync(path, text, cancellationToken);
    }
}
