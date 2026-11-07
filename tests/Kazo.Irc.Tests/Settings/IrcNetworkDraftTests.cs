using Kazo.Irc.Chat;
using Kazo.Irc.Settings;

namespace Kazo.Irc.Tests.Settings;

/// <summary>The fields of the network editor and what they become (decisions 12 and 13).</summary>
public sealed class IrcNetworkDraftTests
{
    [Fact]
    public void A_new_network_is_a_WebSocket_with_TLS_on_6697()
    {
        var draft = IrcNetworkDraft.New();
        Assert.Null(draft.Id);
        Assert.Equal(IrcTransportKinds.WebSocket, draft.Transport);
        Assert.Equal("6697", draft.Port);
        Assert.True(draft.UseTls);
    }

    [Fact]
    public void The_fields_become_a_network_with_a_new_id()
    {
        var draft = new IrcNetworkDraft
        {
            Name = "  Kazo Net ",
            Transport = IrcTransportKinds.Relay,
            Host = " irc.kazo.ca ",
            Port = " 6667 ",
            UseTls = false,
            Nick = " ",
            AutoJoin = "#kazo, #matrix",
        };

        var edit = draft.ToEdit(IrcNetworkList.Proposed);

        Assert.Equal(("kazo-net", "Kazo Net", "irc.kazo.ca", 6667), (edit.Network.Id, edit.Network.Name, edit.Network.Host, edit.Network.Port));
        Assert.False(edit.Network.UseTls);
        Assert.Null(edit.Network.Nick);
        Assert.Equal(["#kazo", "#matrix"], edit.Network.AutoJoin);
        Assert.Null(edit.NewPassword);
        Assert.False(edit.ForgetPassword);
    }

    [Fact]
    public void A_port_that_is_not_a_number_is_refused_by_the_check()
    {
        var draft = new IrcNetworkDraft { Name = "X", Transport = IrcTransportKinds.Relay, Host = "irc.x.org", Port = "66a" };
        var edit = draft.ToEdit([]);
        Assert.Equal([IrcNetworkProblem.PortInvalid], IrcNetworkList.Validate(edit.Network, []));
    }

    [Fact]
    public void An_existing_network_comes_back_the_same()
    {
        var unreal = IrcNetworkList.Proposed[2] with { Nick = "neo", SaslAccount = "neo", AutoJoin = ["#unreal-support"] };

        var draft = IrcNetworkDraft.From(unreal, hasSavedPassword: true);
        var edit = draft.ToEdit(IrcNetworkList.Proposed);

        Assert.Equal("wss://irc.unrealircd.org/", draft.WebSocketUrl);
        Assert.Equal("#unreal-support", draft.AutoJoin);
        Assert.Equal(unreal.Id, edit.Network.Id);
        Assert.Equal(unreal.WebSocketUrl, edit.Network.WebSocketUrl);
        Assert.Equal("neo", edit.Network.SaslAccount);
        Assert.Equal(["#unreal-support"], edit.Network.AutoJoin);
        Assert.Null(edit.NewPassword);
        Assert.False(edit.ForgetPassword);
    }

    [Fact]
    public void A_password_goes_to_the_keyring_only_with_an_account()
    {
        var withAccount = new IrcNetworkDraft { Name = "X", SaslAccount = "neo", SaslPassword = "zion" }.ToEdit([]);
        var withoutAccount = new IrcNetworkDraft { Name = "X", SaslPassword = "zion" }.ToEdit([]);

        Assert.Equal("zion", withAccount.NewPassword);
        Assert.Null(withoutAccount.NewPassword);
    }

    [Fact]
    public void Removing_the_account_forgets_the_saved_password()
    {
        var network = IrcNetworkList.Proposed[0] with { SaslAccount = "neo" };
        var draft = IrcNetworkDraft.From(network, hasSavedPassword: true);
        draft.SaslAccount = "";

        var edit = draft.ToEdit(IrcNetworkList.Proposed);

        Assert.Null(edit.Network.SaslAccount);
        Assert.True(edit.ForgetPassword);
    }
}
