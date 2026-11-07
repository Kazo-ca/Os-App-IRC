using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Model;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Protocol.Client;

/// <summary>
/// An IRC client for one network: it opens a transport, registers (CAP, SASL, NICK, USER), keeps
/// the model of channels and users up to date from what the server sends, answers PING, sends what
/// the user types, and reconnects with a growing delay when the connection is lost.
/// </summary>
/// <remarks>
/// <para>
/// Every line from the server is handled on the client's read loop, one at a time, and the events
/// are raised from it. In Blazor WebAssembly that loop runs on the UI thread; elsewhere a UI must
/// marshal the events itself.
/// </para>
/// <para>
/// Life: <see cref="Start"/> starts the loop; <see cref="QuitAsync"/> sends <c>QUIT</c> and ends
/// it (no reconnection); <see cref="DisposeAsync"/> ends it without <c>QUIT</c>.
/// See doc/en/architecture.md, "Protocol".
/// </para>
/// </remarks>
public sealed partial class IrcClient : IAsyncDisposable
{
    private readonly Func<IIrcTransport> _transportFactory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, IrcChannel> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IrcUser> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _queries = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetime;
    private Task? _run;
    private IIrcTransport? _transport;
    private volatile bool _quitRequested;
    private volatile bool _stopReconnecting;
    private bool _autoJoinDone;

    /// <summary>Creates a client; nothing is sent before <see cref="Start"/>.</summary>
    /// <param name="options">How to register on the network.</param>
    /// <param name="transportFactory">Gives a new transport at every connection attempt.</param>
    /// <param name="timeProvider">The clock of the reconnection delays and the PING timer (tests give a fake one).</param>
    public IrcClient(IrcClientOptions options, Func<IIrcTransport> transportFactory, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Nick);
        ArgumentNullException.ThrowIfNull(transportFactory);
        Options = options;
        _transportFactory = transportFactory;
        _time = timeProvider ?? TimeProvider.System;
        CurrentNick = options.Nick;
        _wantedNick = options.Nick;
    }

    /// <summary>The state changed.</summary>
    public event EventHandler<IrcStateChange>? StateChanged;

    /// <summary>The server accepted the registration (<c>001</c>).</summary>
    public event EventHandler? Registered;

    /// <summary>A line was received and read (raised before the model is updated by it).</summary>
    public event EventHandler<IrcMessage>? MessageReceived;

    /// <summary>A line was sent.</summary>
    public event EventHandler<string>? LineSent;

    /// <summary>A message to show in a channel or a private conversation (the client's own included).</summary>
    public event EventHandler<IrcChatMessage>? ChatMessage;

    /// <summary>A join, part, quit, nickname, topic, mode or away event to show in a conversation.</summary>
    public event EventHandler<IrcActivity>? Activity;

    /// <summary>A line for the server tab.</summary>
    public event EventHandler<IrcServerText>? ServerText;

    /// <summary>An error from the server or from the connection.</summary>
    public event EventHandler<IrcClientError>? Error;

    /// <summary>The answer to a WHOIS is complete.</summary>
    public event EventHandler<IrcWhoisInfo>? WhoisReceived;

    /// <summary>The SASL login ended.</summary>
    public event EventHandler<IrcSaslResult>? SaslCompleted;

    /// <summary>The client waits before trying again.</summary>
    public event EventHandler<IrcReconnectInfo>? Reconnecting;

    /// <summary>A private conversation was opened (<c>/query</c>, or a private message received).</summary>
    public event EventHandler<string>? QueryOpened;

    /// <summary>How the client registers.</summary>
    public IrcClientOptions Options { get; }

    /// <summary>Where the client is in the life of its connection.</summary>
    public IrcClientState State { get; private set; } = IrcClientState.Disconnected;

    /// <summary>The nickname the server knows the client by (the wanted one until <c>001</c>).</summary>
    public string CurrentNick { get; private set; }

    /// <summary>True when the server marked the client away.</summary>
    public bool IsAway { get; private set; }

    /// <summary>The services account the client is logged in as, or null.</summary>
    public string? Account { get; private set; }

    /// <summary>What the server supports (ISUPPORT).</summary>
    public IrcServerSupport ServerSupport { get; } = new();

    /// <summary>The server's name, from <c>001</c>'s source.</summary>
    public string? ServerName { get; private set; }

    /// <summary>The channels the client is in, or wants to be in again after a reconnection.</summary>
    public IReadOnlyList<IrcChannel> Channels => _channels.Values.ToList();

    /// <summary>The users the client knows (in its channels or private conversations).</summary>
    public IReadOnlyList<IrcUser> Users => _users.Values.ToList();

    /// <summary>The nicknames of the open private conversations.</summary>
    public IReadOnlyList<string> Queries => _queries.Values.ToList();

    /// <summary>The loop of the client: completes when the client stops for good.</summary>
    public Task Completion => _run ?? Task.CompletedTask;

    /// <summary>The channel with this name, or null.</summary>
    public IrcChannel? GetChannel(string name) => _channels.GetValueOrDefault(ServerSupport.Fold(name));

    /// <summary>The user with this nickname, or null.</summary>
    public IrcUser? GetUser(string nick) => _users.GetValueOrDefault(ServerSupport.Fold(nick));

    /// <summary>True when <paramref name="nick"/> is the client's own nickname.</summary>
    public bool IsMe(string? nick) => nick is not null && ServerSupport.Fold(nick) == ServerSupport.Fold(CurrentNick);

    /// <summary>Starts connecting; the client then reconnects by itself until <see cref="QuitAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">The client is already running.</exception>
    public void Start()
    {
        if (_run is { IsCompleted: false })
        {
            throw new InvalidOperationException("The IRC client is already running.");
        }

        _quitRequested = false;
        _stopReconnecting = false;
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        _run = RunAsync(_lifetime.Token);
    }

    /// <summary>
    /// Sends <c>QUIT</c>, waits for the server to close the connection (up to
    /// <see cref="IrcClientOptions.QuitTimeout"/>) and stops the client: no reconnection.
    /// </summary>
    public async Task QuitAsync(string? message = null, CancellationToken cancellationToken = default)
    {
        _quitRequested = true;
        var run = _run;
        if (run is null || run.IsCompleted)
        {
            return;
        }

        if (_transport is not null)
        {
            try
            {
                await SendAsync(new IrcMessage("QUIT", message ?? Options.DefaultQuitMessage), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // The connection is already gone: there is nobody to say goodbye to.
            }
        }

        using var timeout = new CancellationTokenSource();
        var delay = Task.Delay(Options.QuitTimeout, _time, timeout.Token);
        if (await Task.WhenAny(run, delay).ConfigureAwait(false) != run)
        {
            _lifetime?.Cancel();
        }

        await timeout.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    /// <summary>Stops the client at once, without <c>QUIT</c>.</summary>
    public async ValueTask DisposeAsync()
    {
        _quitRequested = true;
        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }

        if (_run is not null)
        {
            await _run.ConfigureAwait(false);
        }

        _lifetime?.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>Sends one message as it is (checked by <see cref="IrcMessage.Format"/>).</summary>
    /// <exception cref="InvalidOperationException">The client is not connected.</exception>
    /// <exception cref="IrcProtocolException">The message cannot be written.</exception>
    public async Task SendAsync(IrcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var line = message.Format();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transport = _transport ?? throw new InvalidOperationException("The IRC client is not connected.");
            await transport.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        LineSent?.Invoke(this, line);
    }

    private async Task RunAsync(CancellationToken lifetime)
    {
        var attempt = 0;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var registered = await RunSessionAsync(lifetime).ConfigureAwait(false);
                if (_quitRequested || _stopReconnecting || !Options.Reconnect.Enabled)
                {
                    break;
                }

                attempt = registered ? 1 : attempt + 1;
                if (Options.Reconnect.MaxAttempts is { } max && attempt > max)
                {
                    break;
                }

                var delay = Options.Reconnect.GetDelay(attempt);
                SetState(IrcClientState.Reconnecting);
                Reconnecting?.Invoke(this, new IrcReconnectInfo(attempt, delay));
                await Task.Delay(delay, _time, lifetime).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Stopped by QuitAsync's timeout or by DisposeAsync.
        }
        finally
        {
            SetState(IrcClientState.Disconnected);
        }
    }

    /// <summary>One connection, from the transport's opening to its end. Returns true when it registered.</summary>
    private async Task<bool> RunSessionAsync(CancellationToken lifetime)
    {
        var transport = _transportFactory();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        BeginSession();
        SetState(IrcClientState.Connecting);
        try
        {
            await transport.ConnectAsync(session.Token).ConfigureAwait(false);
            _transport = transport;
            SetState(IrcClientState.Registering);
            StartKeepAlive(session);
            await BeginRegistrationAsync(session.Token).ConfigureAwait(false);
            await ReadLoopAsync(transport, session.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            if (_pingTimedOut)
            {
                Error?.Invoke(this, new IrcClientError("PING_TIMEOUT", "The server stopped answering."));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error?.Invoke(this, new IrcClientError("TRANSPORT", ex.Message, ex));
        }
        finally
        {
            StopKeepAlive();
            await _writeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _transport = null;
            _writeLock.Release();
            await transport.DisposeAsync().ConfigureAwait(false);
            EndSession();
        }

        return _registered;
    }

    private async Task ReadLoopAsync(IIrcTransport transport, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await transport.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            NoteReceived();
            if (!IrcMessage.TryParse(line, out var message) || message is null)
            {
                continue;
            }

            MessageReceived?.Invoke(this, message);
            await HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Forgets what belongs to one connection before a new one.</summary>
    private void BeginSession()
    {
        _registered = false;
        _pingTimedOut = false;
        ResetRegistration();
    }

    /// <summary>The connection ended: nobody is in any channel any more, but the client remembers which ones to join again.</summary>
    private void EndSession()
    {
        foreach (var channel in _channels.Values)
        {
            channel.IsJoined = false;
            channel.ClearUsers();
            channel.ClearModes();
        }

        var keep = _queries.Keys.ToHashSet();
        foreach (var key in _users.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _users.Remove(key);
        }

        IsAway = false;
        Account = null;
    }

    private void SetState(IrcClientState state)
    {
        if (State == state)
        {
            return;
        }

        var previous = State;
        State = state;
        StateChanged?.Invoke(this, new IrcStateChange(previous, state));
    }

    private DateTimeOffset Now => _time.GetUtcNow();
}
