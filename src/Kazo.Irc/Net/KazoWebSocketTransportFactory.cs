using System.Net.WebSockets;
using Kazo.Irc.Chat;
using Kazo.Irc.Protocol.Transport;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Net;

namespace Kazo.Irc.Net;

/// <summary>
/// Opens the networks that accept WebSocket (decision 3), straight from the browser: Kazo OS's
/// <c>KazoClientWebSocket</c> with the <c>text.ircv3.net</c> sub-protocol (ADR 0235 §1). It works
/// with no Kazo server; it needs <c>network.real.access</c>, like the relay (decision 11).
/// </summary>
public sealed class KazoWebSocketTransportFactory : IIrcTransportFactory
{
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connect;

    /// <summary>Creates the factory of the app.</summary>
    /// <param name="context">The app's context, entered for every connection (the client's loop runs outside the OS's flows).</param>
    public KazoWebSocketTransportFactory(IAppExecutionContext context)
        : this(ConnectWith(context))
    {
    }

    /// <summary>Creates a factory with another way to open the socket (tests).</summary>
    /// <param name="connect">Opens a WebSocket to the address, with the IRC sub-protocol.</param>
    public KazoWebSocketTransportFactory(Func<Uri, CancellationToken, Task<WebSocket>> connect)
    {
        ArgumentNullException.ThrowIfNull(connect);
        _connect = connect;
    }

    /// <inheritdoc />
    public string Kind => IrcTransportKinds.WebSocket;

    /// <inheritdoc />
    public IIrcTransport Create(IrcNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var url = network.WebSocketUrl
            ?? throw new InvalidOperationException($"The network '{network.Name}' has no WebSocket address.");
        return new WebSocketIrcTransport(async cancellationToken =>
        {
            try
            {
                return await _connect(url, cancellationToken);
            }
            catch (Exception exception) when (Translate(exception) is { } failure)
            {
                throw failure;
            }
        });
    }

    /// <summary>
    /// What a failure of <c>KazoClientWebSocket.ConnectAsync</c> means for the user. The browser
    /// does not say why a WebSocket failed (no HTTP code): a refusal, an unknown name and a page
    /// without WebSocket are all « unreachable », and are retried.
    /// </summary>
    /// <param name="exception">The error.</param>
    /// <returns>The transport failure, or null for an error that is not the network's (a bug, a cancellation).</returns>
    public static IrcTransportException? Translate(Exception exception) => exception switch
    {
        IrcTransportException transport => transport,
        UnauthorizedAccessException => new(IrcTransportErrors.NoPermission, exception.Message, isPermanent: true, exception),
        NotSupportedException => new(IrcTransportErrors.NotSupported, exception.Message, isPermanent: true, exception),
        WebSocketException when exception.Message.Contains("simulated host", StringComparison.OrdinalIgnoreCase) =>
            new(IrcTransportErrors.SimulatedHost, exception.Message, isPermanent: true, exception),
        WebSocketException => new(IrcTransportErrors.Unreachable, exception.Message, isPermanent: false, exception),
        _ => null,
    };

    private static Func<Uri, CancellationToken, Task<WebSocket>> ConnectWith(IAppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return async (url, cancellationToken) =>
        {
            using IDisposable scope = KazoExecutionContext.Enter(context);
            var socket = new KazoClientWebSocket();
            socket.Options.AddSubProtocol(WebSocketIrcTransport.TextSubProtocol);
            try
            {
                await socket.ConnectAsync(url, cancellationToken);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }
}
