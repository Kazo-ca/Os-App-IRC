using Kazo.Irc.Net;
using Kazo.Os.AppSdk;

namespace Kazo.Irc.Chat;

/// <summary>The networks the window offers.</summary>
internal static class IrcNetworks
{
    /// <summary>
    /// Builds the catalog: the WebSocket and relay transports (work item 3, decision 3) for the
    /// networks of <c>networks.json</c>, and the demonstration network when it is compiled in.
    /// </summary>
    /// <param name="context">The app's context, entered for each connection.</param>
    /// <param name="networks">The networks of <c>networks.json</c> (or the proposed ones).</param>
    /// <returns>The catalog.</returns>
    public static IrcNetworkCatalog Create(IAppExecutionContext context, IEnumerable<IrcNetwork> networks)
    {
        var catalog = new IrcNetworkCatalog()
            .AddFactory(new KazoWebSocketTransportFactory(context))
            .AddFactory(new KazoRelayTransportFactory(context));
#if IRC_DEMO
        catalog.Add(DemoNetwork.Network, new DemoNetwork());
#endif
        catalog.SetEditableNetworks(networks);
        return catalog;
    }
}
