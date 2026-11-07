using Kazo.Irc.Chat;
using Kazo.Irc.Demo;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Protocol.Transport;
using Microsoft.Extensions.Time.Testing;

namespace Kazo.Irc.Tests.Chat;

/// <summary>The chat on the real networks (work item 3): clear failures, the « no Kazo server » mark, a new attempt.</summary>
public sealed class RealNetworkChatTests
{
    private static readonly IrcNetwork Libera = new("libera", "Libera.Chat", IrcTransportKinds.Relay, "irc.libera.chat", 6697);

    [Fact]
    public async Task No_Kazo_server_is_said_clearly_marks_the_relay_and_is_not_retried()
    {
        var factory = new ScriptedFactory(IrcTransportKinds.Relay, new IrcTransportException(IrcTransportErrors.NoServer, "Host not found", isPermanent: true));
        await using var chat = NewChat(factory);
        var changes = 0;
        chat.Changed += () => changes++;

        var session = chat.Connect(Libera, "neo");
        await ChatHarness.WaitUntilAsync(() => IrcChat.IsStopped(session));

        Assert.True(chat.RelayUnavailable);
        Assert.True(changes > 0);
        Assert.Equal(1, factory.Created);
        Assert.Contains(session.Server.Lines, line => line is { Kind: ChatLineKind.Error }
            && line.Text.StartsWith("Libera.Chat needs the Kazo server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stopped_network_is_opened_again_in_a_new_session()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 14, 0, 0, TimeSpan.Zero));
        var factory = new ScriptedFactory(
            IrcTransportKinds.Relay,
            new IrcTransportException(IrcTransportErrors.NoServer, "Host not found", isPermanent: true),
            () => new DemoIrcServer(time));
        await using var chat = NewChat(factory, time);
        var first = chat.Connect(Libera, "neo");
        await ChatHarness.WaitUntilAsync(() => IrcChat.IsStopped(first));

        var second = chat.Connect(Libera, "neo");

        Assert.NotSame(first, second);
        Assert.Same(second, Assert.Single(chat.Sessions));
        Assert.False(chat.RelayUnavailable);
        await ChatHarness.WaitUntilAsync(() => second.Client.State == IrcClientState.Connected);
        Assert.Same(second, chat.Connect(Libera, "neo"));
    }

    [Fact]
    public async Task A_failure_that_can_pass_is_said_and_retried()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 14, 0, 0, TimeSpan.Zero));
        var factory = new ScriptedFactory(
            IrcTransportKinds.Relay,
            new IrcTransportException(IrcTransportErrors.Refused, "Connection refused", isPermanent: false),
            () => new DemoIrcServer(time));
        await using var chat = NewChat(factory, time);

        var session = chat.Connect(Libera, "neo");
        await ChatHarness.WaitUntilAsync(() => session.Client.State == IrcClientState.Reconnecting);
        time.Advance(TimeSpan.FromMinutes(1));
        await ChatHarness.WaitUntilAsync(() => session.Client.State == IrcClientState.Connected);

        Assert.False(chat.RelayUnavailable);
        Assert.Contains(session.Server.Lines, line => line.Text.StartsWith("Libera.Chat refused the connection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_users_and_topic_sent_after_the_join_redraw_the_window()
    {
        await using var harness = new ChatHarness();
        await harness.ConnectAsync();
        var changes = new List<int>();
        harness.Chat.Changed += () =>
        {
            lock (changes)
            {
                changes.Add(harness.Chat.Selected?.Users.Count ?? 0);
            }
        };

        await harness.JoinKazoAsync();

        // The demonstration server sends NAMES after the JOIN, as a real network does: the last
        // redraw sees the four users, not only the user who joined.
        await ChatHarness.WaitUntilAsync(() =>
        {
            lock (changes)
            {
                return changes.Contains(4);
            }
        });
    }

    [Fact]
    public async Task The_SASL_account_goes_to_the_client()
    {
        var factory = new ScriptedFactory(IrcTransportKinds.Relay, new IrcTransportException(IrcTransportErrors.NoServer, "x", isPermanent: true));
        await using var chat = NewChat(factory);

        var session = chat.Connect(Libera, "neo", new IrcSaslCredentials("neo", "zion"));

        Assert.Equal("neo", session.Client.Options.Sasl?.Account);
    }

    [Fact]
    public async Task A_code_with_no_sentence_shows_the_technical_message()
    {
        var factory = new ScriptedFactory(IrcTransportKinds.Relay, new IrcTransportException("ODD", "something odd", isPermanent: true));
        await using var chat = NewChat(factory);

        var session = chat.Connect(Libera, "neo");
        await ChatHarness.WaitUntilAsync(() => IrcChat.IsStopped(session));

        Assert.Contains(session.Server.Lines, line => line is { Kind: ChatLineKind.Error, Text: "something odd" });
        Assert.False(chat.RelayUnavailable);
    }

    private static IrcChat NewChat(ScriptedFactory factory, TimeProvider? time = null)
    {
        var catalog = new IrcNetworkCatalog().AddFactory(factory);
        catalog.SetEditableNetworks([Libera]);
        return new IrcChat(catalog, new RecordingNotifier(), timeProvider: time ?? new FakeTimeProvider());
    }

    /// <summary>A factory whose first transport fails to connect, and whose next ones come from a function.</summary>
    private sealed class ScriptedFactory(string kind, IrcTransportException firstFailure, Func<IIrcTransport>? next = null) : IIrcTransportFactory
    {
        public int Created { get; private set; }

        public string Kind => kind;

        public IIrcTransport Create(IrcNetwork network)
        {
            Created++;
            return Created == 1 || next is null
                ? new StreamIrcTransport(_ => Task.FromException<Stream>(firstFailure))
                : next();
        }
    }
}
