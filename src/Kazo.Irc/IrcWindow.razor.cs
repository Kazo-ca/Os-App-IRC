using System.Globalization;
using Kazo.Irc.Chat;
using Kazo.Irc.Layout;
using Kazo.Irc.Logs;
using Kazo.Irc.Protocol.Client;
using Kazo.Irc.Settings;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Apps;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Diagnostics;
using Kazo.Os.AppSdk.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace Kazo.Irc;

/// <summary>
/// The IRC window (decision 8 of doc/en/plan.md): one window per user, holding every network and
/// channel; three columns when the window is wide, one column with two side panels when it is
/// narrow (decision 7). It follows the OS theme, with the console font as an option (decision 19).
/// Work item 4 connects it to the chat (<see cref="IrcChat"/>): the networks, the conversations,
/// the input line, what the user is told about mentions and private messages (decision 14) and the
/// logs (decision 15). Closing the window sends <c>QUIT</c> (decision 8). Work item 5 opens the
/// web addresses of the messages in the OS Browser (decision 17).
/// </summary>
public sealed partial class IrcWindow : IAsyncDisposable, IWindowCloseGuard
{
    private readonly IrcLayout _layout = new();
    private IrcSettings _settings = new();
    private IrcChat _chat = new(new IrcNetworkCatalog(), NoNotifier.Instance);
    private string? _status;
    private string? _shownTitle;
    private bool _closing;
    private IStringLocalizer<Strings>? _localizer;

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    private IReadOnlyList<IrcNetwork> AvailableNetworks =>
        [.. _chat.Catalog.Networks.Where(network => _chat.Sessions.All(session => session.Network.Id != network.Id))];

    private string InputPlaceholder => _chat.Selected switch
    {
        null => L["MessagePlaceholder"],
        { Kind: ConversationKind.Server } => L["CommandPlaceholder"],
        { } conversation => string.Format(CultureInfo.CurrentCulture, L["MessageTo"], conversation.Name),
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _chat.Changed -= OnChatChanged;
        await _chat.DisposeAsync();
    }

    /// <summary>
    /// The window closes (its close button, Alt+F4): every network gets a <c>QUIT</c> (decision 8),
    /// then the window may close.
    /// </summary>
    /// <param name="cancellationToken">Cancels the close.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken = default)
    {
        await QuitAllAsync();
        return true;
    }

    /// <inheritdoc />
    protected override Task OnAppStopRequestedAsync(KazoStopRequestedEventArgs e) => QuitAllAsync();

    /// <inheritdoc />
    protected override async Task OnAppInitializedAsync()
    {
        _settings = await IrcSettings.LoadAsync(KazoIrcFileSystem.Instance, AppContext.HomeDirectory);
        // The logs are written from the IRC client's read loop: the app context is entered for them.
        var logs = new IrcLogWriter(new AppScopedFileSystem(AppContext, KazoIrcFileSystem.Instance), AppContext.HomeDirectory, () => AppContext.TimeZone);
        logs.Failed += exception => _ = InvokeAsync(() =>
        {
            _status = string.Format(CultureInfo.CurrentCulture, L["LogsFailed"], exception.Message);
            StateHasChanged();
        });
        _chat = new IrcChat(IrcNetworks.Create(), new KazoIrcNotifier(AppContext), logs, Texts())
        {
            LogsEnabled = _settings.LogsEnabled,
        };
        _chat.Changed += OnChatChanged;
    }

    /// <inheritdoc />
    protected override void OnWindowActivated() => _chat.SetWindowActive(WindowState != KazoWindowState.Minimized);

    /// <inheritdoc />
    protected override void OnWindowDeactivated() => _chat.SetWindowActive(false);

    /// <inheritdoc />
    protected override void OnWindowStateChanged(KazoWindowState previous, KazoWindowState current) =>
        _chat.SetWindowActive(IsWindowActive && current != KazoWindowState.Minimized);

    private IrcChatTexts Texts() => new()
    {
        Connecting = L["Connecting"],
        Connected = L["Connected"],
        Reconnecting = L["Reconnecting"],
        Disconnected = L["Disconnected"],
        Whois = L["Whois"],
        WhoisUnknown = L["WhoisUnknown"],
        MentionTitle = L["MentionTitle"],
        PrivateTitle = L["PrivateTitle"],
    };

    /// <summary>The chat changed: render, and keep the window title's counter (decision 14).</summary>
    private void OnChatChanged() => _ = InvokeAsync(async () =>
    {
        StateHasChanged();
        var count = _chat.AttentionCount;
        var title = count > 0 ? $"({count}) {Manifest.Name}" : Manifest.Name;
        if (title != _shownTitle && !_closing)
        {
            _shownTitle = title;
            await SetWindowTitleAsync(title);
        }
    });

    private void Connect(IrcNetwork network)
    {
        _chat.Connect(network, IrcNick.FromUserName(AppContext.UserLoginName is { Length: > 0 } name ? name : AppContext.UserId));
        _layout.Close();
    }

    private void Select(Conversation conversation)
    {
        _chat.Select(conversation);
        _layout.Close();
    }

    private Task CloseConversationAsync(Conversation conversation) => _chat.CloseConversationAsync(conversation);

    private async Task OpenQueryAsync(string nick)
    {
        await _chat.SubmitAsync("/query " + nick);
        _layout.Close();
    }

    private async Task<bool> SubmitAsync(string text)
    {
        var problem = await _chat.SubmitAsync(text);
        _status = problem switch
        {
            ChatInputProblem.None or ChatInputProblem.Empty => null,
            ChatInputProblem.UnknownCommand => string.Format(CultureInfo.CurrentCulture, L["InputUnknownCommand"], text.Split(' ')[0]),
            ChatInputProblem.MissingArgument => L["InputMissingArgument"],
            ChatInputProblem.NoConversation => L["InputNoConversation"],
            ChatInputProblem.NotAChannel => L["InputNotAChannel"],
            ChatInputProblem.NotConnected => L["InputNotConnected"],
            _ => L["InputRefused"],
        };
        return problem == ChatInputProblem.None;
    }

    private string ActivityText(ChatLine line)
    {
        var withReason = line.Text.Length > 0;
        var format = line.Activity switch
        {
            IrcActivityKind.Join => L["ActivityJoin"],
            IrcActivityKind.Part => withReason ? L["ActivityPartReason"] : L["ActivityPart"],
            IrcActivityKind.Kick => withReason ? L["ActivityKickReason"] : L["ActivityKick"],
            IrcActivityKind.Quit => withReason ? L["ActivityQuitReason"] : L["ActivityQuit"],
            IrcActivityKind.Nick => L["ActivityNick"],
            IrcActivityKind.Topic => L["ActivityTopic"],
            IrcActivityKind.Mode => L["ActivityMode"],
            IrcActivityKind.Away => withReason ? L["ActivityAwayReason"] : L["ActivityAway"],
            IrcActivityKind.Back => L["ActivityBack"],
            _ => "{0} {1}",
        };
        return string.Format(CultureInfo.CurrentCulture, format, line.Nick, line.Text, line.TargetNick);
    }

    private string StateText(IrcClientState state) => state switch
    {
        IrcClientState.Connected => L["StateConnected"],
        IrcClientState.Connecting or IrcClientState.Registering => L["StateConnecting"],
        IrcClientState.Reconnecting => L["StateReconnecting"],
        _ => L["StateDisconnected"],
    };

    private async Task QuitAllAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            await _chat.QuitAllAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            // The connection was already gone: nothing more to tell the server.
        }
    }

    /// <summary>
    /// The console font button: switches the font and saves the choice. The font changes even when
    /// the file cannot be written; the window then says why the choice will not be kept.
    /// </summary>
    private Task ToggleMonospaceAsync()
    {
        _settings = _settings with { MonospaceFont = !_settings.MonospaceFont };
        return SaveSettingsAsync();
    }

    /// <summary>The logs button (decision 15): stops or restarts the logs, and saves the choice.</summary>
    private Task ToggleLogsAsync()
    {
        _settings = _settings with { LogsEnabled = !_settings.LogsEnabled };
        _chat.LogsEnabled = _settings.LogsEnabled;
        return SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settings.SaveAsync(KazoIrcFileSystem.Instance, AppContext.HomeDirectory);
            _status = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status = string.Format(CultureInfo.CurrentCulture, L["SettingsSaveFailed"], exception.Message);
        }
    }

    /// <summary>Escape closes the open side panel, as a phone's back gesture would.</summary>
    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _layout.Close();
        }
    }

    /// <summary>
    /// A web address of a message was clicked (decision 17): a new window of the Kazo OS Browser
    /// opens it (<c>apps.launch</c>). The Browser decides how to reach the site (simulated network,
    /// or the real web through the OS).
    /// </summary>
    /// <param name="link">The address, <c>http</c> or <c>https</c> (<see cref="Text.MessageLinks"/>).</param>
    private async Task OpenLinkAsync(Uri link)
    {
        try
        {
            // Entered explicitly: the click may come from a component the OS did not wrap.
            using IDisposable scope = KazoExecutionContext.Enter(AppContext);
            await KazoBrowser.OpenAsync(link.AbsoluteUri);
            _status = null;
        }
        catch (Exception exception) when (exception is KazoAppLaunchException or UnauthorizedAccessException or ArgumentException)
        {
            _status = string.Format(CultureInfo.CurrentCulture, L["LinkFailed"], link.AbsoluteUri, exception.Message);
        }
    }

    private void DismissStatus() => _status = null;

    /// <summary>The notifier of the chat before the window is initialized.</summary>
    private sealed class NoNotifier : IIrcNotifier
    {
        public static NoNotifier Instance { get; } = new();

        public void Notify(string title, string message)
        {
        }
    }
}
