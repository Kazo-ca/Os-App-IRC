namespace Kazo.Irc.Chat;

/// <summary>The networks the window offers.</summary>
internal static class IrcNetworks
{
    /// <summary>
    /// Builds the catalog. Work item 4 has the demonstration network only (when compiled in); work
    /// item 3 adds the real networks (decision 13) and their WebSocket and relay transports.
    /// </summary>
    /// <returns>The catalog.</returns>
    public static IrcNetworkCatalog Create()
    {
        var catalog = new IrcNetworkCatalog();
#if IRC_DEMO
        catalog.Add(DemoNetwork.Network, new DemoNetwork());
#endif
        return catalog;
    }
}
