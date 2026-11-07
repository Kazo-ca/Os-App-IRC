using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Chat;

/// <summary>A network the app can connect to.</summary>
/// <param name="Id">A stable identifier (the log folder uses <see cref="Name"/>).</param>
/// <param name="Name">The name shown, and the folder of its logs.</param>
/// <param name="TransportKind">Which <see cref="IIrcTransportFactory"/> opens it (<c>demo</c> here; <c>websocket</c> and <c>relay</c> come with work item 3).</param>
/// <param name="Host">The server's host name.</param>
/// <param name="Port">The server's port (0 for the demonstration network).</param>
public sealed record IrcNetwork(string Id, string Name, string TransportKind, string Host, int Port)
{
    /// <summary>The channels joined right after the first registration.</summary>
    public IReadOnlyList<string> AutoJoin { get; init; } = [];
}

/// <summary>
/// Opens a connection to a network. The client asks for a new transport at every connection
/// attempt. Work item 4 brings the demonstration network's factory only; work item 3 adds the
/// WebSocket (<c>KazoClientWebSocket</c>) and relay (<c>KazoTcpClient</c>) factories.
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
public sealed class IrcNetworkCatalog
{
    private readonly Dictionary<string, IIrcTransportFactory> _factories = new(StringComparer.Ordinal);
    private readonly List<IrcNetwork> _networks = [];

    /// <summary>The networks, in the order they were added.</summary>
    public IReadOnlyList<IrcNetwork> Networks => _networks;

    /// <summary>Adds a network and the factory that opens it.</summary>
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

        _factories[factory.Kind] = factory;
        _networks.Add(network);
        return this;
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
