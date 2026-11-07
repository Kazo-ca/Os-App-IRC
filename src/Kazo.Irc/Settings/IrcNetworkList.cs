using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kazo.Irc.Chat;

namespace Kazo.Irc.Settings;

/// <summary>What the network editor refuses (decision 13).</summary>
public enum IrcNetworkProblem
{
    /// <summary>No name.</summary>
    NameMissing,

    /// <summary>Another network has this name (the logs use it as a folder).</summary>
    NameTaken,

    /// <summary>The WebSocket address is not an absolute <c>wss://</c> or <c>ws://</c> address.</summary>
    WebSocketUrlInvalid,

    /// <summary>No server host for the Kazo server's relay.</summary>
    HostMissing,

    /// <summary>A port outside 1 to 65535.</summary>
    PortInvalid,

    /// <summary>A port the app does not declare: the relay would refuse it (ADR 0235 §3).</summary>
    PortNotDeclared,

    /// <summary>A nickname with a space or a character IRC does not allow.</summary>
    NickInvalid,

    /// <summary>An automatic channel that does not start with <c>#</c>, <c>&amp;</c>, <c>+</c> or <c>!</c>, or has a space or a comma.</summary>
    ChannelInvalid,
}

/// <summary>
/// The networks of the app, in <c>~/.config/ca.kazo.irc/networks.json</c> (the « Settings » row of
/// doc/en/plan.md, decision 13): a short list the user can change, with the proposed networks when
/// there is no file yet. The SASL password is never in this file: it is in the keyring
/// (<see cref="IIrcSecretStore"/>, decision 12).
/// </summary>
public static class IrcNetworkList
{
    /// <summary>The ports the app declares for the relay (<c>IrcApp.RealNetworkPorts</c>): TLS 6697 and plain 6667.</summary>
    public static IReadOnlyList<int> DeclaredPorts { get; } = [6697, 6667];

    /// <summary>
    /// The networks proposed on the first run (decision 13). Libera.Chat and OFTC have no public
    /// WebSocket (checked 2026-11-07: Libera's web gateway refuses other origins, OFTC's is
    /// Socket.IO): they need the Kazo server. UnrealIRCd's network accepts <c>text.ircv3.net</c>
    /// on <c>wss://irc.unrealircd.org/</c>: it works with no server.
    /// </summary>
    public static IReadOnlyList<IrcNetwork> Proposed { get; } =
    [
        new("libera", "Libera.Chat", IrcTransportKinds.Relay, "irc.libera.chat", 6697),
        new("oftc", "OFTC", IrcTransportKinds.Relay, "irc.oftc.net", 6697),
        new("unrealircd", "UnrealIRCd", IrcTransportKinds.WebSocket, "irc.unrealircd.org", 6697)
        {
            WebSocketUrl = new Uri("wss://irc.unrealircd.org/"),
        },
    ];

    /// <summary>Gets the networks file of one user.</summary>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    /// <returns><c>&lt;home&gt;/.config/ca.kazo.irc/networks.json</c>.</returns>
    public static string PathFor(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(homeDirectory);
        return homeDirectory.TrimEnd('/') + "/.config/ca.kazo.irc/networks.json";
    }

    /// <summary>
    /// Reads the networks. No file gives the proposed networks; an unreadable or broken file also
    /// gives them (and is replaced only when the user saves). An entry the app cannot use (no
    /// id or name, unknown transport) is skipped.
    /// </summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The networks.</returns>
    public static async Task<IReadOnlyList<IrcNetwork>> LoadAsync(IIrcFileSystem files, string homeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            byte[] bytes = await files.ReadAllBytesAsync(PathFor(homeDirectory), cancellationToken);
            var file = JsonSerializer.Deserialize(bytes, IrcNetworkListJsonContext.Default.IrcNetworkFile);
            return file?.Networks is { } entries ? [.. entries.Select(ToNetwork).OfType<IrcNetwork>()] : Proposed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Proposed;
        }
    }

    /// <summary>Writes the networks, creating <c>~/.config/ca.kazo.irc/</c> when needed.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="networks">The networks.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is written.</returns>
    public static Task SaveAsync(IIrcFileSystem files, string homeDirectory, IEnumerable<IrcNetwork> networks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(networks);
        var file = new IrcNetworkFile([.. networks.Select(ToEntry)]);
        string json = JsonSerializer.Serialize(file, IrcNetworkListJsonContext.Default.IrcNetworkFile);
        return files.WriteAllBytesAsync(PathFor(homeDirectory), Encoding.UTF8.GetBytes(json), cancellationToken);
    }

    /// <summary>Checks a network before the editor saves it.</summary>
    /// <param name="network">The network edited.</param>
    /// <param name="others">The networks already there (its name must not be another's).</param>
    /// <returns>What is wrong; empty when it can be saved.</returns>
    public static IReadOnlyList<IrcNetworkProblem> Validate(IrcNetwork network, IEnumerable<IrcNetwork> others)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(others);
        var problems = new List<IrcNetworkProblem>();
        if (string.IsNullOrWhiteSpace(network.Name))
        {
            problems.Add(IrcNetworkProblem.NameMissing);
        }
        else if (others.Any(other => other.Id != network.Id && string.Equals(other.Name.Trim(), network.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add(IrcNetworkProblem.NameTaken);
        }

        if (network.TransportKind == IrcTransportKinds.WebSocket
            && network.WebSocketUrl is not { IsAbsoluteUri: true, Scheme: "wss" or "ws", Host.Length: > 0 })
        {
            problems.Add(IrcNetworkProblem.WebSocketUrlInvalid);
        }

        if (network.TransportKind == IrcTransportKinds.Relay)
        {
            if (string.IsNullOrWhiteSpace(network.Host) || network.Host.Any(c => char.IsWhiteSpace(c) || c is '/' or ':' or '@'))
            {
                problems.Add(IrcNetworkProblem.HostMissing);
            }

            if (network.Port is < 1 or > 65535)
            {
                problems.Add(IrcNetworkProblem.PortInvalid);
            }
            else if (!DeclaredPorts.Contains(network.Port))
            {
                problems.Add(IrcNetworkProblem.PortNotDeclared);
            }
        }

        if (network.Nick is { Length: > 0 } nick && !IrcNick.IsValid(nick))
        {
            problems.Add(IrcNetworkProblem.NickInvalid);
        }

        if (network.AutoJoin.Any(channel => !IsChannelName(channel)))
        {
            problems.Add(IrcNetworkProblem.ChannelInvalid);
        }

        return problems;
    }

    /// <summary>Gives a new network an id from its name that no other network has.</summary>
    /// <param name="name">The network's name.</param>
    /// <param name="others">The networks already there.</param>
    /// <returns>The id (lower case letters, digits and dashes).</returns>
    public static string NewId(string? name, IEnumerable<IrcNetwork> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var slug = new string([.. (name ?? string.Empty).ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        if (slug.Length == 0)
        {
            slug = "network";
        }

        var taken = new HashSet<string>(others.Select(other => other.Id), StringComparer.Ordinal);
        var id = slug;
        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{slug}-{n}";
        }

        return id;
    }

    /// <summary>Reads the automatic channels typed in the editor: separated by spaces or commas.</summary>
    /// <param name="text">The text typed.</param>
    /// <returns>The channels, in order, without duplicates.</returns>
    public static IReadOnlyList<string> ParseChannels(string? text) =>
        [.. (text ?? string.Empty).Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool IsChannelName(string channel) =>
        channel.Length >= 2 && "#&+!".Contains(channel[0], StringComparison.Ordinal)
        && !channel.Any(c => c is ' ' or ',' || char.IsControl(c));

    private static IrcNetwork? ToNetwork(IrcNetworkEntry entry)
    {
        if (entry is not { Id.Length: > 0, Name.Length: > 0 }
            || entry.Transport is not (IrcTransportKinds.WebSocket or IrcTransportKinds.Relay))
        {
            return null;
        }

        Uri? url = Uri.TryCreate(entry.WebSocketUrl, UriKind.Absolute, out var parsed) ? parsed : null;
        return new IrcNetwork(entry.Id, entry.Name, entry.Transport, entry.Host ?? string.Empty, entry.Port ?? 6697)
        {
            UseTls = entry.Tls ?? true,
            WebSocketUrl = url,
            Nick = string.IsNullOrWhiteSpace(entry.Nick) ? null : entry.Nick,
            SaslAccount = string.IsNullOrWhiteSpace(entry.SaslAccount) ? null : entry.SaslAccount,
            AutoJoin = entry.AutoJoin ?? [],
        };
    }

    private static IrcNetworkEntry ToEntry(IrcNetwork network) => new(
        network.Id,
        network.Name,
        network.TransportKind,
        network.Host.Length > 0 ? network.Host : null,
        network.Port > 0 ? network.Port : null,
        network.UseTls,
        network.WebSocketUrl?.OriginalString,
        network.Nick,
        network.SaslAccount,
        [.. network.AutoJoin]);
}

/// <summary>The content of <c>networks.json</c>.</summary>
/// <param name="Networks">The networks, in the order shown.</param>
internal sealed record IrcNetworkFile(IReadOnlyList<IrcNetworkEntry>? Networks);

/// <summary>One network of <c>networks.json</c> (decision 13: address, port, transport, TLS, nickname, SASL account, channels).</summary>
internal sealed record IrcNetworkEntry(
    string? Id,
    string? Name,
    string? Transport,
    string? Host,
    int? Port,
    bool? Tls,
    string? WebSocketUrl,
    string? Nick,
    string? SaslAccount,
    IReadOnlyList<string>? AutoJoin);

/// <summary>Trim-safe JSON of <c>networks.json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IrcNetworkFile))]
internal sealed partial class IrcNetworkListJsonContext : JsonSerializerContext;
