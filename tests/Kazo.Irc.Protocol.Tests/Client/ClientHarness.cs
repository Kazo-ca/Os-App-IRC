using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Transport;
using Microsoft.Extensions.Time.Testing;

namespace Kazo.Irc.Protocol.Tests.Client;

/// <summary>An <see cref="IrcClient"/> on fake transports and a fake clock, with every event recorded.</summary>
internal sealed class ClientHarness : IAsyncDisposable
{
    private readonly List<FakeIrcTransport> _transports = [];
    private readonly object _gate = new();

    public ClientHarness(IrcClientOptions? options = null, Action<FakeIrcTransport, int>? prepareTransport = null)
    {
        Client = new IrcClient(options ?? new IrcClientOptions { Nick = "neo" }, () =>
        {
            var transport = new FakeIrcTransport();
            lock (_gate)
            {
                prepareTransport?.Invoke(transport, _transports.Count);
                _transports.Add(transport);
            }

            return transport;
        }, Time);

        Client.ChatMessage += (_, e) => Record(Chats, e);
        Client.Activity += (_, e) => Record(Activities, e);
        Client.ServerText += (_, e) => Record(ServerTexts, e);
        Client.Error += (_, e) => Record(Errors, e);
        Client.WhoisReceived += (_, e) => Record(Whois, e);
        Client.SaslCompleted += (_, e) => Record(Sasl, e);
        Client.Reconnecting += (_, e) => Record(Reconnects, e);
        Client.StateChanged += (_, e) => Record(States, e.Current);
        Client.QueryOpened += (_, e) => Record(OpenedQueries, e);
        Client.Registered += (_, _) => Record(RegisteredTimes, Time.GetUtcNow());
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 4, 0, 0, TimeSpan.Zero));

    public IrcClient Client { get; }

    public List<IrcChatMessage> Chats { get; } = [];
    public List<IrcActivity> Activities { get; } = [];
    public List<IrcServerText> ServerTexts { get; } = [];
    public List<IrcClientError> Errors { get; } = [];
    public List<IrcWhoisInfo> Whois { get; } = [];
    public List<IrcSaslResult> Sasl { get; } = [];
    public List<IrcReconnectInfo> Reconnects { get; } = [];
    public List<IrcClientState> States { get; } = [];
    public List<string> OpenedQueries { get; } = [];
    public List<DateTimeOffset> RegisteredTimes { get; } = [];

    public IReadOnlyList<FakeIrcTransport> Transports
    {
        get
        {
            lock (_gate)
            {
                return _transports.ToArray();
            }
        }
    }

    public FakeIrcTransport Transport => Transports[^1];

    /// <summary>Starts the client and waits for its USER line.</summary>
    public async Task StartAsync()
    {
        Client.Start();
        await WaitUntilAsync(() => Transports.Count > 0);
        await Transport.WaitForSentAsync("USER ");
    }

    /// <summary>Starts and registers with a server that offers no capability.</summary>
    public async Task RegisterAsync(string isupport = "PREFIX=(ov)@+ CHANTYPES=#&")
    {
        await StartAsync();
        await Transport.ServerSendAsync(":irc.kazo.ca CAP * LS :");
        await Transport.WaitForSentAsync("CAP END");
        await Transport.ServerSendAsync(
            ":irc.kazo.ca 001 neo :Welcome to Kazo",
            $":irc.kazo.ca 005 neo {isupport} :are supported by this server");
    }

    /// <summary>The client joins a channel with the given users.</summary>
    public Task JoinAsync(string channel, string names = "@neo") =>
        Transport.ServerSendAsync(
            $":neo!~neo@kazo.ca JOIN {channel}",
            $":irc.kazo.ca 353 neo = {channel} :{names}",
            $":irc.kazo.ca 366 neo {channel} :End of /NAMES list.");

    /// <summary>Waits (real time, 10 s at most) until <paramref name="condition"/> holds.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold within 10 seconds.");
            }

            await Task.Delay(5);
        }
    }

    public async ValueTask DisposeAsync() => await Client.DisposeAsync();

    private void Record<T>(List<T> list, T item)
    {
        lock (list)
        {
            list.Add(item);
        }
    }
}
