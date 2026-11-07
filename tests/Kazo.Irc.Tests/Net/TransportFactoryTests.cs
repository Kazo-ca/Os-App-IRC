using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using Kazo.Irc.Chat;
using Kazo.Irc.Net;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Tests.Net;

/// <summary>The two real transports (work item 3, decisions 3 and 4) and the clear error of each failure.</summary>
public sealed class TransportFactoryTests
{
    private static readonly IrcNetwork Unreal = new("unrealircd", "UnrealIRCd", IrcTransportKinds.WebSocket, "irc.unrealircd.org", 6697)
    {
        WebSocketUrl = new Uri("wss://irc.unrealircd.org/"),
    };

    private static readonly IrcNetwork Libera = new("libera", "Libera.Chat", IrcTransportKinds.Relay, "irc.libera.chat", 6697);

    [Fact]
    public async Task The_WebSocket_factory_opens_the_networks_address()
    {
        Uri? opened = null;
        var factory = new KazoWebSocketTransportFactory((url, _) =>
        {
            opened = url;
            return Task.FromException<WebSocket>(new WebSocketException("refused"));
        });

        Assert.Equal(IrcTransportKinds.WebSocket, factory.Kind);
        await using var transport = factory.Create(Unreal);
        Assert.Null(opened);
        var failure = await Assert.ThrowsAsync<IrcTransportException>(() => transport.ConnectAsync(CancellationToken.None));

        Assert.Equal(new Uri("wss://irc.unrealircd.org/"), opened);
        Assert.Equal(IrcTransportErrors.Unreachable, failure.Code);
        Assert.False(failure.IsPermanent);
    }

    [Fact]
    public void A_WebSocket_network_needs_an_address()
    {
        var factory = new KazoWebSocketTransportFactory((_, _) => throw new InvalidOperationException());
        Assert.Throws<InvalidOperationException>(() => factory.Create(Unreal with { WebSocketUrl = null }));
    }

    [Fact]
    public void The_WebSocket_failures_are_translated()
    {
        AssertFailure(KazoWebSocketTransportFactory.Translate(new UnauthorizedAccessException("no")), IrcTransportErrors.NoPermission, permanent: true);
        AssertFailure(KazoWebSocketTransportFactory.Translate(new NotSupportedException("worker")), IrcTransportErrors.NotSupported, permanent: true);
        AssertFailure(
            KazoWebSocketTransportFactory.Translate(new WebSocketException("'irc.kazo.lan' is a simulated host.")),
            IrcTransportErrors.SimulatedHost,
            permanent: true);
        AssertFailure(KazoWebSocketTransportFactory.Translate(new WebSocketException("closed")), IrcTransportErrors.Unreachable, permanent: false);
        Assert.Null(KazoWebSocketTransportFactory.Translate(new OperationCanceledException()));
        Assert.Null(KazoWebSocketTransportFactory.Translate(new NullReferenceException()));
    }

    [Fact]
    public async Task The_relay_factory_opens_the_networks_host_and_port()
    {
        IrcNetwork? opened = null;
        var factory = new KazoRelayTransportFactory((network, _) =>
        {
            opened = network;
            return Task.FromException<Stream>(new SocketException((int)SocketError.HostNotFound));
        });

        Assert.Equal(IrcTransportKinds.Relay, factory.Kind);
        await using var transport = factory.Create(Libera);
        var failure = await Assert.ThrowsAsync<IrcTransportException>(() => transport.ConnectAsync(CancellationToken.None));

        Assert.Same(Libera, opened);
        Assert.Equal(IrcTransportErrors.NoServer, failure.Code);
        Assert.True(failure.IsPermanent);
    }

    [Fact]
    public async Task The_relay_stream_carries_the_IRC_lines()
    {
        using var stream = new MemoryStream("PING :kazo\r\n"u8.ToArray());
        var factory = new KazoRelayTransportFactory((_, _) => Task.FromResult<Stream>(stream));
        await using var transport = factory.Create(Libera);
        await transport.ConnectAsync(CancellationToken.None);

        Assert.Equal("PING :kazo", await transport.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public void The_relay_failures_are_translated()
    {
        AssertFailure(KazoRelayTransportFactory.Translate(new UnauthorizedAccessException("no")), IrcTransportErrors.NoPermission, permanent: true);
        AssertFailure(KazoRelayTransportFactory.Translate(new AuthenticationException("bad certificate")), IrcTransportErrors.CertificateRefused, permanent: true);
        AssertFailure(KazoRelayTransportFactory.Translate(new NotSupportedException("worker")), IrcTransportErrors.NotSupported, permanent: true);
        AssertFailure(KazoRelayTransportFactory.Translate(new SocketException((int)SocketError.HostNotFound)), IrcTransportErrors.NoServer, permanent: true);
        AssertFailure(KazoRelayTransportFactory.Translate(new SocketException((int)SocketError.ConnectionRefused)), IrcTransportErrors.Refused, permanent: false);
        AssertFailure(KazoRelayTransportFactory.Translate(new SocketException((int)SocketError.TimedOut)), IrcTransportErrors.Unreachable, permanent: false);
        AssertFailure(KazoRelayTransportFactory.Translate(new IOException("reset")), IrcTransportErrors.Unreachable, permanent: false);
        Assert.Null(KazoRelayTransportFactory.Translate(new OperationCanceledException()));
    }

    [Fact]
    public void A_failure_already_translated_is_kept()
    {
        var failure = new IrcTransportException(IrcTransportErrors.Lost, "lost", isPermanent: false);
        Assert.Same(failure, KazoRelayTransportFactory.Translate(failure));
        Assert.Same(failure, KazoWebSocketTransportFactory.Translate(failure));
    }

    private static void AssertFailure(IrcTransportException? failure, string code, bool permanent)
    {
        Assert.NotNull(failure);
        Assert.Equal(code, failure.Code);
        Assert.Equal(permanent, failure.IsPermanent);
        Assert.NotNull(failure.InnerException);
    }
}
