using Kazo.Irc.Layout;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Irc.Settings;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace Kazo.Irc;

/// <summary>
/// The IRC window (decision 8 of doc/en/plan.md): one window per user, which will hold every
/// network and channel. Work item 1 gives it its layout, empty: the networks and channels, the
/// messages with their input line, and the users of the channel; three columns when the window is
/// wide, one column with two side panels when it is narrow (decision 7). It follows the OS theme,
/// with the console font as an option saved in <c>~/.config/ca.kazo.irc/settings.json</c>
/// (decision 19).
/// </summary>
public sealed partial class IrcWindow
{
    private readonly IrcLayout _layout = new();
    private IrcSettings _settings = new();
    private string? _status;
    private IStringLocalizer<Strings>? _localizer;

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <inheritdoc />
    protected override async Task OnAppInitializedAsync()
    {
        _settings = await IrcSettings.LoadAsync(KazoIrcFileSystem.Instance, AppContext.HomeDirectory);
    }

    /// <summary>
    /// The console font button: switches the font and saves the choice. The font changes even when
    /// the file cannot be written; the window then says why the choice will not be kept.
    /// </summary>
    private async Task ToggleMonospaceAsync()
    {
        _settings = _settings with { MonospaceFont = !_settings.MonospaceFont };
        try
        {
            await _settings.SaveAsync(KazoIrcFileSystem.Instance, AppContext.HomeDirectory);
            _status = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status = string.Format(System.Globalization.CultureInfo.CurrentCulture, L["SettingsSaveFailed"], exception.Message);
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

    private void DismissStatus() => _status = null;
}
