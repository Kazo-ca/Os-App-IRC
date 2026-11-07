using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Chat;

/// <summary>The kinds of transport a network is opened with (<see cref="IrcNetwork.TransportKind"/>).</summary>
public static class IrcTransportKinds
{
    /// <summary>
    /// A WebSocket straight from the browser (<c>KazoClientWebSocket</c>, <c>text.ircv3.net</c>,
    /// ADR 0235 §1 of Kazo OS): works without the Kazo server, for the networks that accept it.
    /// </summary>
    public const string WebSocket = "websocket";

    /// <summary>
    /// A TCP connection through the Kazo server's relay (<c>KazoTcpClient</c>), TLS opened by the
    /// relay (ADR 0235 §2): every network, but only when a Kazo server is connected.
    /// </summary>
    public const string Relay = "relay";

    /// <summary>The demonstration network in memory (work item 4, never in the Store package).</summary>
    public const string Demo = "demo";
}

/// <summary>A network the app can connect to.</summary>
/// <param name="Id">A stable identifier (the key of its SASL password in the keyring; the logs use <see cref="Name"/>).</param>
/// <param name="Name">The name shown, and the folder of its logs.</param>
/// <param name="TransportKind">Which <see cref="IIrcTransportFactory"/> opens it (<see cref="IrcTransportKinds"/>).</param>
/// <param name="Host">The server's host name, for the relay (and the name its TLS certificate must carry).</param>
/// <param name="Port">The server's port for the relay (0 for the demonstration network).</param>
public sealed record IrcNetwork(string Id, string Name, string TransportKind, string Host, int Port)
{
    /// <summary>The channels joined right after the first registration.</summary>
    public IReadOnlyList<string> AutoJoin { get; init; } = [];

    /// <summary>Whether the relay opens TLS to <see cref="Host"/> (decision 4: on, port 6697, by default).</summary>
    public bool UseTls { get; init; } = true;

    /// <summary>The <c>wss://</c> (or <c>ws://</c>) address of the network's WebSocket, when it has one.</summary>
    public Uri? WebSocketUrl { get; init; }

    /// <summary>The nickname on this network; null for the OS user name (decision 12).</summary>
    public string? Nick { get; init; }

    /// <summary>The SASL PLAIN account; null for none. Its password is in the keyring, never here (decision 12).</summary>
    public string? SaslAccount { get; init; }

    /// <summary>Whether the network needs the Kazo server (decision 13: « serveur Kazo requis »).</summary>
    public bool NeedsKazoServer => TransportKind == IrcTransportKinds.Relay;
}

/// <summary>
/// Opens a connection to a network. The client asks for a new transport at every connection
/// attempt: the WebSocket factory (<c>KazoClientWebSocket</c>), the relay factory
/// (<c>KazoTcpClient</c>) and, in Debug and the end-to-end build, the demonstration network.
/// </summary>
public interface IIrcTransportFactory
{
    /// <summary>The <see cref="IrcNetwork.TransportKind"/> this factory opens.</summary>
    string Kind { get; }

    /// <summary>Creates a new, unopened transport to the network.</summary>
    /// <param name="network">The network.</param>
    /// <returns>The transport; the client opens it.</returns>
    IIrcTransport Create(IrcNetwork network);
}

/// <summary>The networks offered to the user and the transport factories that open them.</summary>
/// <remarks>
/// Two lists: the networks of <c>networks.json</c>, which the editor changes
/// (<see cref="SetEditableNetworks"/>), and the fixed ones added by code (the demonstration
/// network). <see cref="Networks"/> gives the fixed ones first.
/// </remarks>
public sealed class IrcNetworkCatalog
{
    private readonly Dictionary<string, IIrcTransportFactory> _factories = new(StringComparer.Ordinal);
    private readonly List<IrcNetwork> _fixed = [];
    private List<IrcNetwork> _editable = [];

    /// <summary>The networks: the fixed ones, then those of <c>networks.json</c>.</summary>
    public IReadOnlyList<IrcNetwork> Networks => [.. _fixed, .. _editable];

    /// <summary>The networks of <c>networks.json</c>, as the editor shows them.</summary>
    public IReadOnlyList<IrcNetwork> EditableNetworks => _editable;

    /// <summary>Adds a fixed network and the factory that opens it.</summary>
    /// <param name="network">The network.</param>
    /// <param name="factory">Its factory; it must open <see cref="IrcNetwork.TransportKind"/>.</param>
    /// <returns>This catalog.</returns>
    public IrcNetworkCatalog Add(IrcNetwork network, IIrcTransportFactory factory)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(factory);
        if (factory.Kind != network.TransportKind)
        {
            throw new ArgumentException($"The factory opens '{factory.Kind}', the network needs '{network.TransportKind}'.", nameof(factory));
        }

        AddFactory(factory);
        _fixed.Add(network);
        return this;
    }

    /// <summary>Adds a factory for the networks of its kind.</summary>
    /// <param name="factory">The factory.</param>
    /// <returns>This catalog.</returns>
    public IrcNetworkCatalog AddFactory(IIrcTransportFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[factory.Kind] = factory;
        return this;
    }

    /// <summary>Replaces the networks of <c>networks.json</c> (loaded, or saved by the editor).</summary>
    /// <param name="networks">The networks.</param>
    public void SetEditableNetworks(IEnumerable<IrcNetwork> networks)
    {
        ArgumentNullException.ThrowIfNull(networks);
        _editable = [.. networks];
    }

    /// <summary>Gives a function that creates a new transport to the network at each call.</summary>
    /// <param name="network">A network of this catalog.</param>
    /// <returns>The function the client calls at every connection attempt.</returns>
    /// <exception cref="InvalidOperationException">No factory opens this network.</exception>
    public Func<IIrcTransport> TransportsFor(IrcNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (!_factories.TryGetValue(network.TransportKind, out var factory))
        {
            throw new InvalidOperationException($"No transport opens '{network.TransportKind}' networks.");
        }

        return () => factory.Create(network);
    }
}
