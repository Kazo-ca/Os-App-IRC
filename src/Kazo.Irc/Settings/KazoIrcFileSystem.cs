using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.FileSystem;

namespace Kazo.Irc.Settings;

/// <summary><see cref="IIrcFileSystem"/> on the Kazo OS file system (<see cref="KazoFile"/>).</summary>
public sealed class KazoIrcFileSystem : IIrcFileSystem
{
    /// <summary>Mode of the app's own folder (<c>0755</c>, as <c>mkdir</c> with umask 022).</summary>
    private static readonly FileSystemPermissions FolderMode = FileSystemPermissions.FromMode(0b111_101_101);

    /// <summary>The one instance.</summary>
    public static KazoIrcFileSystem Instance { get; } = new();

    private readonly HashSet<string> _knownFolders = new(StringComparer.Ordinal);

    private KazoIrcFileSystem()
    {
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        KazoFile.ReadAllBytesAsync(path, cancellationToken);

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (KazoPath.GetDirectoryName(path) is { Length: > 0 } directory
            && !await KazoDirectory.ExistsAsync(directory, cancellationToken))
        {
            await CreateFolderAsync(directory, cancellationToken);
        }

        await KazoFile.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The logs live under <c>~/.local/share/ca.kazo.irc/</c>, in the user's home: the app asks for
    /// <c>filesystem.user-home.read</c> and <c>.write</c>, so <c>KazoDirectory.CreateDirectoryAsync</c>
    /// may look at every parent (like <c>mkdir -p</c>). A folder found or created once is not looked
    /// at again.
    /// </remarks>
    public async Task AppendAllTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        if (KazoPath.GetDirectoryName(path) is { Length: > 0 } directory && !_knownFolders.Contains(directory))
        {
            if (!await KazoDirectory.ExistsAsync(directory, cancellationToken))
            {
                await KazoDirectory.CreateDirectoryAsync(directory, cancellationToken);
            }

            _knownFolders.Add(directory);
        }

        await KazoFile.AppendAllTextAsync(path, text, cancellationToken);
    }

    /// <summary>
    /// Creates the app's own folder (<c>~/.config/ca.kazo.irc/</c>), whose parent already exists.
    /// </summary>
    /// <remarks>
    /// <c>KazoDirectory.CreateDirectoryAsync</c> looks at every parent first (like <c>mkdir -p</c>),
    /// and looking at <c>~/.config</c> needs <c>filesystem.user-home.read</c>, which the app does
    /// not ask for: with only <c>filesystem.private.*</c> it fails. The folder itself is created
    /// here, the way <c>mkdir</c> without <c>-p</c> does.
    /// </remarks>
    private static async Task CreateFolderAsync(string directory, CancellationToken cancellationToken)
    {
        FileSystemMutationResult created = await KazoExecutionContext.Current.FileSystem.CreateAsync(
            new FileSystemCreateRequest(VirtualPath.Parse(directory), FileSystemEntryKind.Directory, FolderMode),
            cancellationToken);
        if (!created.Succeeded && created.Transaction.Error?.Code != FileSystemErrorCode.AlreadyExists)
        {
            FileSystemError? error = created.Transaction.Error;
            throw error?.Code is FileSystemErrorCode.PermissionDenied or FileSystemErrorCode.CapabilityDenied or FileSystemErrorCode.AuthorityDenied
                ? new UnauthorizedAccessException($"Access to the path '{directory}' is denied ({error.Code}).")
                : new IOException($"The folder '{directory}' could not be created ({error?.Code}).");
        }
    }
}
