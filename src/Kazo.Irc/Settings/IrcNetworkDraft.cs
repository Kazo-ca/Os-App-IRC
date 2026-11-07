using System.Globalization;
using Kazo.Irc.Chat;

namespace Kazo.Irc.Settings;

/// <summary>What the network editor gives back when the user saves.</summary>
/// <param name="Network">The network, as it goes into <c>networks.json</c>.</param>
/// <param name="NewPassword">A new SASL password for the keyring, or null to keep the one there.</param>
/// <param name="ForgetPassword">Whether the keyring's SASL password must be removed (no SASL account any more).</param>
public sealed record IrcNetworkEdit(IrcNetwork Network, string? NewPassword, bool ForgetPassword);

/// <summary>
/// The fields of the network editor (decision 13), bound to its inputs: text as typed, turned into
/// an <see cref="IrcNetwork"/> by <see cref="ToEdit"/>.
/// </summary>
public sealed class IrcNetworkDraft
{
    /// <summary>The id of the network edited, or null for a new one.</summary>
    public string? Id { get; init; }

    /// <summary>The name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="IrcTransportKinds.WebSocket"/> or <see cref="IrcTransportKinds.Relay"/>.</summary>
    public string Transport { get; set; } = IrcTransportKinds.WebSocket;

    /// <summary>The WebSocket address, as typed.</summary>
    public string WebSocketUrl { get; set; } = string.Empty;

    /// <summary>The server host for the relay.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The port for the relay, as typed.</summary>
    public string Port { get; set; } = "6697";

    /// <summary>Whether the relay opens TLS.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>The nickname; empty for the OS user name.</summary>
    public string Nick { get; set; } = string.Empty;

    /// <summary>The SASL account; empty for none.</summary>
    public string SaslAccount { get; set; } = string.Empty;

    /// <summary>A new SASL password; empty keeps the one in the keyring.</summary>
    public string SaslPassword { get; set; } = string.Empty;

    /// <summary>Whether the keyring already holds a password for this network.</summary>
    public bool HasSavedPassword { get; init; }

    /// <summary>The automatic channels, separated by spaces or commas.</summary>
    public string AutoJoin { get; set; } = string.Empty;

    /// <summary>A draft for a new network: WebSocket, TLS port 6697.</summary>
    /// <returns>The empty draft.</returns>
    public static IrcNetworkDraft New() => new();

    /// <summary>A draft of an existing network.</summary>
    /// <param name="network">The network.</param>
    /// <param name="hasSavedPassword">Whether its SASL password is in the keyring.</param>
    /// <returns>The draft.</returns>
    public static IrcNetworkDraft From(IrcNetwork network, bool hasSavedPassword)
    {
        ArgumentNullException.ThrowIfNull(network);
        return new IrcNetworkDraft
        {
            Id = network.Id,
            Name = network.Name,
            Transport = network.TransportKind,
            WebSocketUrl = network.WebSocketUrl?.OriginalString ?? string.Empty,
            Host = network.Host,
            Port = network.Port > 0 ? network.Port.ToString(CultureInfo.InvariantCulture) : string.Empty,
            UseTls = network.UseTls,
            Nick = network.Nick ?? string.Empty,
            SaslAccount = network.SaslAccount ?? string.Empty,
            HasSavedPassword = hasSavedPassword,
            AutoJoin = string.Join(' ', network.AutoJoin),
        };
    }

    /// <summary>Turns the fields into the network to save; <see cref="IrcNetworkList.Validate"/> checks it.</summary>
    /// <param name="others">The networks already there (a new network gets an id none of them has).</param>
    /// <returns>The edit.</returns>
    public IrcNetworkEdit ToEdit(IEnumerable<IrcNetwork> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var name = Name.Trim();
        var port = int.TryParse(Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        Uri? url = Uri.TryCreate(WebSocketUrl.Trim(), UriKind.Absolute, out var uri) ? uri : null;
        var account = SaslAccount.Trim();
        var network = new IrcNetwork(Id ?? IrcNetworkList.NewId(name, others), name, Transport, Host.Trim(), port)
        {
            UseTls = UseTls,
            WebSocketUrl = url,
            Nick = Nick.Trim() is { Length: > 0 } nick ? nick : null,
            SaslAccount = account.Length > 0 ? account : null,
            AutoJoin = IrcNetworkList.ParseChannels(AutoJoin),
        };
        var password = account.Length > 0 && SaslPassword.Length > 0 ? SaslPassword : null;
        return new IrcNetworkEdit(network, password, ForgetPassword: account.Length == 0 && HasSavedPassword);
    }
}
