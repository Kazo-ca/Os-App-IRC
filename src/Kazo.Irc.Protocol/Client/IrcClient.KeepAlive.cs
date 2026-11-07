using System.Globalization;
using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Client;

/// <summary>
/// Keep-alive: after <see cref="IrcClientOptions.PingInterval"/> of silence the client sends a
/// <c>PING</c>; after <see cref="IrcClientOptions.PingTimeout"/> more, the connection is held lost
/// and closed, and the reconnection starts. A browser WebSocket or a relayed stream that died
/// without a close frame is found this way.
/// </summary>
public sealed partial class IrcClient
{
    private readonly object _keepAliveGate = new();
    private ITimer? _keepAliveTimer;
    private CancellationTokenSource? _keepAliveSession;
    private long _lastReceived;
    private bool _pingSent;
    private bool _pingTimedOut;

    private void StartKeepAlive(CancellationTokenSource session)
    {
        lock (_keepAliveGate)
        {
            _keepAliveSession = session;
            _lastReceived = _time.GetTimestamp();
            _pingSent = false;
            var period = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, Math.Min(Options.PingInterval.Ticks, Options.PingTimeout.Ticks) / 2));
            _keepAliveTimer = _time.CreateTimer(_ => CheckKeepAlive(), null, period, period);
        }
    }

    private void StopKeepAlive()
    {
        lock (_keepAliveGate)
        {
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
            _keepAliveSession = null;
        }
    }

    private void NoteReceived()
    {
        lock (_keepAliveGate)
        {
            _lastReceived = _time.GetTimestamp();
            _pingSent = false;
        }
    }

    private void CheckKeepAlive()
    {
        bool sendPing;
        lock (_keepAliveGate)
        {
            if (_keepAliveSession is null)
            {
                return;
            }

            var silence = _time.GetElapsedTime(_lastReceived);
            if (_pingSent && silence >= Options.PingInterval + Options.PingTimeout)
            {
                _pingTimedOut = true;
                _keepAliveSession.Cancel();
                return;
            }

            sendPing = !_pingSent && silence >= Options.PingInterval;
            _pingSent |= sendPing;
        }

        if (sendPing)
        {
            _ = SendKeepAlivePingAsync();
        }
    }

    private async Task SendKeepAlivePingAsync()
    {
        try
        {
            var token = "kazo-" + _time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            await SendAsync(new IrcMessage("PING", token)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The connection is going away: the read loop reports it.
        }
    }
}
