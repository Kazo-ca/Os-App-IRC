#if IRC_DEMO
using Kazo.Irc.Demo;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Chat;

/// <summary>
/// The demonstration network (work item 4): <see cref="DemoIrcServer"/> in memory. Compiled only
/// with <c>IrcDemoNetwork</c> (Debug, and the CI's end-to-end build), never in the Store package.
/// </summary>
internal sealed class DemoNetwork : IIrcTransportFactory
{
    /// <summary>The transport kind of the demonstration network.</summary>
    public const string TransportKindName = "demo";

    /// <summary>The network offered in the list.</summary>
    public static IrcNetwork Network { get; } =
        new("demo", "Kazo Demo", TransportKindName, DemoIrcServer.ServerName, 0);

    /// <inheritdoc />
    public string Kind => TransportKindName;

    /// <inheritdoc />
    public IIrcTransport Create(IrcNetwork network) => new DemoIrcServer();
}
#endif
