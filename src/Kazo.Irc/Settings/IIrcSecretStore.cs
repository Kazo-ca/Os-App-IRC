using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Keyring;

namespace Kazo.Irc.Settings;

/// <summary>
/// Where the SASL passwords are kept (decision 12): the OS's encrypted keyring, never a file. The
/// tests use a dictionary; the app uses <see cref="KazoIrcSecretStore"/>.
/// </summary>
public interface IIrcSecretStore
{
    /// <summary>Reads the SASL password of a network.</summary>
    /// <param name="networkId">The network's <c>Id</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The password, or null when none is kept.</returns>
    /// <exception cref="InvalidOperationException">The keyring is locked.</exception>
    ValueTask<string?> GetSaslPasswordAsync(string networkId, CancellationToken cancellationToken = default);

    /// <summary>Keeps (or replaces) the SASL password of a network.</summary>
    /// <param name="networkId">The network's <c>Id</c>.</param>
    /// <param name="networkName">The network's name, for the keyring's label.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="InvalidOperationException">The keyring is locked.</exception>
    ValueTask SetSaslPasswordAsync(string networkId, string networkName, string password, CancellationToken cancellationToken = default);

    /// <summary>Forgets the SASL password of a network (removed, or no longer using SASL).</summary>
    /// <param name="networkId">The network's <c>Id</c>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="InvalidOperationException">The keyring is locked.</exception>
    ValueTask DeleteSaslPasswordAsync(string networkId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The SASL passwords in the app's part of the OS keyring (<c>KazoKeyring</c>, no capability
/// needed). Every call enters the app's context: the editor calls it from a child component.
/// </summary>
public sealed class KazoIrcSecretStore : IIrcSecretStore
{
    private readonly IAppExecutionContext _context;

    /// <summary>Creates the store of the app.</summary>
    /// <param name="context">The app's context.</param>
    public KazoIrcSecretStore(IAppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>The keyring entry of a network's SASL password.</summary>
    /// <param name="networkId">The network's <c>Id</c>.</param>
    /// <returns><c>sasl/&lt;id&gt;</c>.</returns>
    public static string EntryId(string networkId)
    {
        ArgumentException.ThrowIfNullOrEmpty(networkId);
        return "sasl/" + networkId;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetSaslPasswordAsync(string networkId, CancellationToken cancellationToken = default)
    {
        using IDisposable scope = KazoExecutionContext.Enter(_context);
        return await KazoKeyring.GetAsync(_context, EntryId(networkId), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetSaslPasswordAsync(string networkId, string networkName, string password, CancellationToken cancellationToken = default)
    {
        using IDisposable scope = KazoExecutionContext.Enter(_context);
        await KazoKeyring.SetAsync(_context, EntryId(networkId), password, $"IRC: {networkName} (SASL)", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteSaslPasswordAsync(string networkId, CancellationToken cancellationToken = default)
    {
        using IDisposable scope = KazoExecutionContext.Enter(_context);
        await KazoKeyring.DeleteAsync(_context, EntryId(networkId), cancellationToken);
    }
}
