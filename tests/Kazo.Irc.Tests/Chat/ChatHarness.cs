using Kazo.Irc.Chat;
using Kazo.Irc.Demo;
using Kazo.Irc.Logs;
using Kazo.Irc.Protocol.Transport;
using Kazo.Irc.Tests.Settings;
using Microsoft.Extensions.Time.Testing;

namespace Kazo.Irc.Tests.Chat;

/// <summary>An <see cref="IrcChat"/> on the demonstration server, a fake clock, memory files and a recording notifier.</summary>
internal sealed class ChatHarness : IAsyncDisposable
{
    public static readonly IrcNetwork Demo = new("demo", "Kazo Demo", "demo", DemoIrcServer.ServerName, 0);

    public ChatHarness(bool withLogs = true)
    {
        var catalog = new IrcNetworkCatalog().Add(Demo, new Factory(this));
        Logs = withLogs ? new IrcLogWriter(Files, "/home/neo", () => TimeZoneInfo.Utc) : null;
        Chat = new IrcChat(catalog, Notifier, Logs, timeProvider: Time);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 7, 14, 5, 9, TimeSpan.Zero));

    public MemoryFileSystem Files { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    public IrcLogWriter? Logs { get; }

    public IrcChat Chat { get; }

    public List<DemoIrcServer> Servers { get; } = [];

    public IrcSession Session => Chat.Sessions[0];

    /// <summary>Connects to the demonstration network as neo and waits for the registration.</summary>
    public async Task<IrcSession> ConnectAsync()
    {
        var session = Chat.Connect(Demo, "neo");
        await WaitUntilAsync(() => session.Client.State == Protocol.Client.IrcClientState.Connected);
        return session;
    }

    /// <summary>Joins #kazo by typing it, and waits until the channel and its users are there.</summary>
    public async Task<Conversation> JoinKazoAsync()
    {
        Assert.Equal(ChatInputProblem.None, await Chat.SubmitAsync("/join #kazo"));
        await WaitUntilAsync(() => Chat.Selected?.Name == "#kazo" && Chat.Selected.Users.Count == 4);
        return Chat.Selected!;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // A list changed while it was read by the condition: try again.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync() => await Chat.DisposeAsync();

    private sealed class Factory(ChatHarness harness) : IIrcTransportFactory
    {
        public string Kind => "demo";

        public IIrcTransport Create(IrcNetwork network)
        {
            var server = new DemoIrcServer(harness.Time);
            lock (harness.Servers)
            {
                harness.Servers.Add(server);
            }

            return server;
        }
    }
}

/// <summary>Records the notifications instead of showing them.</summary>
internal sealed class RecordingNotifier : IIrcNotifier
{
    private readonly List<(string Title, string Message)> _posted = [];

    public IReadOnlyList<(string Title, string Message)> Posted
    {
        get
        {
            lock (_posted)
            {
                return _posted.ToArray();
            }
        }
    }

    public void Notify(string title, string message)
    {
        lock (_posted)
        {
            _posted.Add((title, message));
        }
    }
}
