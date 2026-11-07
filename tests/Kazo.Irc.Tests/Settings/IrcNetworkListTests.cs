using System.Text;
using Kazo.Irc.Chat;
using Kazo.Irc.Settings;

namespace Kazo.Irc.Tests.Settings;

/// <summary>The networks file <c>~/.config/ca.kazo.irc/networks.json</c> and its editor's checks (decision 13).</summary>
public sealed class IrcNetworkListTests
{
    private const string Home = "/home/neo";
    private const string NetworksPath = "/home/neo/.config/ca.kazo.irc/networks.json";

    [Fact]
    public void The_file_is_next_to_the_settings()
    {
        Assert.Equal(NetworksPath, IrcNetworkList.PathFor(Home));
        Assert.Equal(NetworksPath, IrcNetworkList.PathFor(Home + "/"));
    }

    [Fact]
    public async Task A_first_run_proposes_Libera_OFTC_and_a_WebSocket_network()
    {
        var networks = await IrcNetworkList.LoadAsync(new MemoryFileSystem(), Home);

        Assert.Equal(["Libera.Chat", "OFTC", "UnrealIRCd"], networks.Select(network => network.Name));
        Assert.True(networks[0].NeedsKazoServer);
        Assert.True(networks[1].NeedsKazoServer);
        Assert.False(networks[2].NeedsKazoServer);
        Assert.Equal(new Uri("wss://irc.unrealircd.org/"), networks[2].WebSocketUrl);
        Assert.All(networks, network => Assert.Empty(IrcNetworkList.Validate(network, networks)));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{}")]
    public async Task A_broken_file_gives_the_proposed_networks(string content)
    {
        MemoryFileSystem files = new();
        files.Files[NetworksPath] = Encoding.UTF8.GetBytes(content);
        Assert.Same(IrcNetworkList.Proposed, await IrcNetworkList.LoadAsync(files, Home));
    }

    [Fact]
    public async Task A_file_the_app_may_not_read_gives_the_proposed_networks()
    {
        MemoryFileSystem files = new() { ReadFailure = new UnauthorizedAccessException("denied") };
        Assert.Same(IrcNetworkList.Proposed, await IrcNetworkList.LoadAsync(files, Home));
    }

    [Fact]
    public async Task The_networks_are_kept_between_runs_without_any_password()
    {
        MemoryFileSystem files = new();
        IrcNetwork mine = new("mine", "Mine", IrcTransportKinds.Relay, "irc.example.org", 6667)
        {
            UseTls = false,
            Nick = "trinity",
            SaslAccount = "trinity",
            AutoJoin = ["#kazo", "#matrix"],
        };

        await IrcNetworkList.SaveAsync(files, Home, [IrcNetworkList.Proposed[2], mine]);
        var read = await IrcNetworkList.LoadAsync(files, Home);

        string json = files.Text(NetworksPath);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"transport\": \"relay\"", json);
        Assert.Equal(2, read.Count);
        Assert.Equal(new Uri("wss://irc.unrealircd.org/"), read[0].WebSocketUrl);
        IrcNetwork back = read[1];
        Assert.Equal(("mine", "Mine", IrcTransportKinds.Relay, "irc.example.org", 6667), (back.Id, back.Name, back.TransportKind, back.Host, back.Port));
        Assert.False(back.UseTls);
        Assert.Equal("trinity", back.Nick);
        Assert.Equal("trinity", back.SaslAccount);
        Assert.Equal(["#kazo", "#matrix"], back.AutoJoin);
    }

    [Fact]
    public async Task An_empty_list_stays_empty()
    {
        MemoryFileSystem files = new();
        await IrcNetworkList.SaveAsync(files, Home, []);
        Assert.Empty(await IrcNetworkList.LoadAsync(files, Home));
    }

    [Fact]
    public async Task Entries_the_app_cannot_use_are_skipped()
    {
        MemoryFileSystem files = new();
        files.Files[NetworksPath] = Encoding.UTF8.GetBytes("""
            { "networks": [
              { "id": "a", "name": "A", "transport": "relay", "host": "irc.a.org", "port": 6697 },
              { "name": "No id", "transport": "relay", "host": "irc.b.org" },
              { "id": "c", "name": "C", "transport": "carrier-pigeon" },
              { "id": "d", "name": "D", "transport": "demo" },
              { "id": "e", "name": "E", "transport": "websocket", "webSocketUrl": "wss://irc.e.org/", "later": true }
            ] }
            """);

        var networks = await IrcNetworkList.LoadAsync(files, Home);

        Assert.Equal(["a", "e"], networks.Select(network => network.Id));
        Assert.True(networks[0].UseTls);
    }

    [Fact]
    public void A_valid_network_has_no_problem()
    {
        IrcNetwork network = new("x", "X", IrcTransportKinds.Relay, "irc.x.org", 6697) { Nick = "neo", AutoJoin = ["#kazo", "&local"] };
        Assert.Empty(IrcNetworkList.Validate(network, IrcNetworkList.Proposed));
    }

    [Fact]
    public void The_editor_refuses_what_cannot_work()
    {
        static IReadOnlyList<IrcNetworkProblem> Check(IrcNetwork network) => IrcNetworkList.Validate(network, IrcNetworkList.Proposed);
        IrcNetwork relay = new("x", "X", IrcTransportKinds.Relay, "irc.x.org", 6697);
        IrcNetwork socket = new("y", "Y", IrcTransportKinds.WebSocket, string.Empty, 0) { WebSocketUrl = new Uri("wss://irc.y.org/") };

        Assert.Equal([IrcNetworkProblem.NameMissing], Check(relay with { Name = " " }));
        Assert.Equal([IrcNetworkProblem.NameTaken], Check(relay with { Name = "libera.chat " }));
        Assert.Equal([IrcNetworkProblem.HostMissing], Check(relay with { Host = "" }));
        Assert.Equal([IrcNetworkProblem.HostMissing], Check(relay with { Host = "irc.x.org:6697" }));
        Assert.Equal([IrcNetworkProblem.PortInvalid], Check(relay with { Port = 0 }));
        Assert.Equal([IrcNetworkProblem.PortNotDeclared], Check(relay with { Port = 7000 }));
        Assert.Equal([IrcNetworkProblem.NickInvalid], Check(relay with { Nick = "two words" }));
        Assert.Equal([IrcNetworkProblem.ChannelInvalid], Check(relay with { AutoJoin = ["kazo"] }));
        Assert.Empty(Check(socket));
        Assert.Equal([IrcNetworkProblem.WebSocketUrlInvalid], Check(socket with { WebSocketUrl = null }));
        Assert.Equal([IrcNetworkProblem.WebSocketUrlInvalid], Check(socket with { WebSocketUrl = new Uri("https://irc.y.org/") }));
    }

    [Fact]
    public void A_network_keeps_its_own_name_when_edited()
    {
        var libera = IrcNetworkList.Proposed[0];
        Assert.Empty(IrcNetworkList.Validate(libera with { Port = 6667 }, IrcNetworkList.Proposed));
    }

    [Fact]
    public void A_new_id_comes_from_the_name_and_is_unique()
    {
        Assert.Equal("my-network", IrcNetworkList.NewId("My Network!", []));
        Assert.Equal("network", IrcNetworkList.NewId("  ", []));
        Assert.Equal("libera-chat", IrcNetworkList.NewId("Libera.Chat", IrcNetworkList.Proposed));
        IrcNetwork taken = new("oftc", "x", IrcTransportKinds.Relay, "h", 6697);
        IrcNetwork taken2 = taken with { Id = "oftc-2" };
        Assert.Equal("oftc-3", IrcNetworkList.NewId("OFTC", [taken, taken2]));
    }

    [Fact]
    public void Channels_are_separated_by_spaces_or_commas()
    {
        Assert.Equal(["#kazo", "#matrix", "&local"], IrcNetworkList.ParseChannels(" #kazo,#matrix  &local #KAZO "));
        Assert.Empty(IrcNetworkList.ParseChannels(null));
    }
}
