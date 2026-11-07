using System.Net.Sockets;
using System.Security.Authentication;
using Kazo.Irc.Chat;
using Kazo.Irc.Protocol.Transport;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Net;

namespace Kazo.Irc.Net;

/// <summary>
/// Opens a network through the Kazo server's relay (decisions 3 and 4): Kazo OS's
/// <c>KazoTcpClient.ConnectAsync</c> with <c>KazoTcpConnectOptions { UseTls, TargetHost }</c>, the
/// relay opening TLS to the IRC server and checking its certificate (ADR 0235 §2). The relay
/// accepts the ports the app declares (<c>tcp/6697</c>, <c>tcp/6667</c>) unless the server's
/// <c>BlockedPorts</c> refuses them (ADR 0235 §3, ADR 0233).
/// </summary>
public sealed class KazoRelayTransportFactory : IIrcTransportFactory
{
    private readonly Func<IrcNetwork, CancellationToken, Task<Stream>> _connect;

    /// <summary>Creates the factory of the app.</summary>
    /// <param name="context">The app's context, entered for every connection (the client's loop runs outside the OS's flows).</param>
    public KazoRelayTransportFactory(IAppExecutionContext context)
        : this(ConnectWith(context))
    {
    }

    /// <summary>Creates a factory with another way to open the stream (tests).</summary>
    /// <param name="connect">Opens the stream to the network's host and port.</param>
    public KazoRelayTransportFactory(Func<IrcNetwork, CancellationToken, Task<Stream>> connect)
    {
        ArgumentNullException.ThrowIfNull(connect);
        _connect = connect;
    }

    /// <inheritdoc />
    public string Kind => IrcTransportKinds.Relay;

    /// <inheritdoc />
    public IIrcTransport Create(IrcNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        return new StreamIrcTransport(async cancellationToken =>
        {
            try
            {
                return await _connect(network, cancellationToken);
            }
            catch (Exception exception) when (Translate(exception) is { } failure)
            {
                throw failure;
            }
        });
    }

    /// <summary>
    /// What a failure of <c>KazoTcpClient.ConnectAsync</c> means for the user. The relay's refusals
    /// arrive as a real socket's errors (Kazo OS <c>server-network-relay.md</c>): « host not found »
    /// is also what happens with no server, with the relay off, or before the server has the app's
    /// grant; « refused » is also a port in the server's <c>BlockedPorts</c>.
    /// </summary>
    /// <param name="exception">The error.</param>
    /// <returns>The transport failure, or null for an error that is not the network's (a bug, a cancellation).</returns>
    public static IrcTransportException? Translate(Exception exception) => exception switch
    {
        IrcTransportException transport => transport,
        UnauthorizedAccessException => new(IrcTransportErrors.NoPermission, exception.Message, isPermanent: true, exception),
        AuthenticationException => new(IrcTransportErrors.CertificateRefused, exception.Message, isPermanent: true, exception),
        NotSupportedException => new(IrcTransportErrors.NotSupported, exception.Message, isPermanent: true, exception),
        SocketException { SocketErrorCode: SocketError.HostNotFound } =>
            new(IrcTransportErrors.NoServer, exception.Message, isPermanent: true, exception),
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
            new(IrcTransportErrors.Refused, exception.Message, isPermanent: false, exception),
        SocketException or IOException => new(IrcTransportErrors.Unreachable, exception.Message, isPermanent: false, exception),
        _ => null,
    };

    private static Func<IrcNetwork, CancellationToken, Task<Stream>> ConnectWith(IAppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return async (network, cancellationToken) =>
        {
            using IDisposable scope = KazoExecutionContext.Enter(context);
            var options = new KazoTcpConnectOptions { UseTls = network.UseTls, TargetHost = network.Host };
            return await KazoTcpClient.ConnectAsync(network.Host, network.Port, options, cancellationToken);
        };
    }
}
