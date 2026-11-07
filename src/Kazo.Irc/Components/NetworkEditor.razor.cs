using System.Globalization;
using Kazo.Irc.Chat;
using Kazo.Irc.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Kazo.Irc.Components;

/// <summary>
/// The network editor (decision 13): the list of the networks of <c>networks.json</c>, and the
/// form of one network. It checks a network with <see cref="IrcNetworkList.Validate"/> before
/// giving it to <see cref="OnSave"/>; the window writes the file and the keyring.
/// </summary>
public sealed partial class NetworkEditor
{
    private IrcNetworkDraft? _draft;
    private IReadOnlyList<IrcNetworkProblem> _problems = [];

    /// <summary>Gets or sets the networks of <c>networks.json</c>.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<IrcNetwork> Networks { get; set; } = [];

    /// <summary>Gets or sets the translated text of a key of <c>Resources/Strings.resx</c>.</summary>
    [Parameter, EditorRequired]
    public Func<string, string> Text { get; set; } = key => key;

    /// <summary>Gets or sets the nickname used when a network has none (the OS user name, decision 12).</summary>
    [Parameter]
    public string DefaultNick { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the keyring holds a SASL password for a network.</summary>
    [Parameter]
    public Func<IrcNetwork, Task<bool>> HasSavedPassword { get; set; } = _ => Task.FromResult(false);

    /// <summary>Gets or sets what saving a network does; it returns false when it could not be saved.</summary>
    [Parameter]
    public Func<IrcNetworkEdit, Task<bool>> OnSave { get; set; } = _ => Task.FromResult(true);

    /// <summary>Gets or sets what deleting a network does.</summary>
    [Parameter]
    public EventCallback<IrcNetwork> OnDelete { get; set; }

    /// <summary>Gets or sets what closing the editor does.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    private string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), arguments);

    /// <summary>How the network is reached, as the list shows it.</summary>
    private string Route(IrcNetwork network) => network.TransportKind == IrcTransportKinds.WebSocket
        ? Format("RouteWebSocket", network.WebSocketUrl?.Host ?? string.Empty)
        : Format(network.UseTls ? "RouteRelayTls" : "RouteRelay", network.Host, network.Port);

    private void Add()
    {
        _draft = IrcNetworkDraft.New();
        _problems = [];
    }

    private async Task EditAsync(IrcNetwork network)
    {
        var saved = network.SaslAccount is not null && await HasSavedPassword(network);
        _draft = IrcNetworkDraft.From(network, saved);
        _problems = [];
    }

    private void Cancel()
    {
        _draft = null;
        _problems = [];
    }

    private async Task SaveAsync()
    {
        if (_draft is null)
        {
            return;
        }

        var others = Networks.Where(network => network.Id != _draft.Id).ToList();
        var edit = _draft.ToEdit(Networks);
        _problems = IrcNetworkList.Validate(edit.Network, others);
        if (_problems.Count == 0 && await OnSave(edit))
        {
            _draft = null;
        }
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    /// <summary>Escape leaves the form, then the editor.</summary>
    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Escape")
        {
            return;
        }

        if (_draft is not null)
        {
            Cancel();
        }
        else
        {
            await CloseAsync();
        }
    }
}
