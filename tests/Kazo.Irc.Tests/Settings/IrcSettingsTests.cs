using System.Text;
using Kazo.Irc.Settings;

namespace Kazo.Irc.Tests.Settings;

/// <summary>The settings file <c>~/.config/ca.kazo.irc/settings.json</c> (decision 19).</summary>
public sealed class IrcSettingsTests
{
    private const string Home = "/home/neo";
    private const string SettingsPath = "/home/neo/.config/ca.kazo.irc/settings.json";

    [Theory]
    [InlineData("/home/neo")]
    [InlineData("/home/neo/")]
    public void The_file_is_in_the_apps_own_config_folder(string home)
    {
        Assert.Equal(SettingsPath, IrcSettings.PathFor(home));
    }

    [Fact]
    public async Task A_first_run_has_no_file_and_gets_the_OS_font()
    {
        IrcSettings settings = await IrcSettings.LoadAsync(new MemoryFileSystem(), Home);
        Assert.False(settings.MonospaceFont);
    }

    [Fact]
    public async Task The_console_font_is_kept_between_runs()
    {
        MemoryFileSystem files = new();
        await new IrcSettings(MonospaceFont: true).SaveAsync(files, Home);

        Assert.Contains("\"monospaceFont\": true", Encoding.UTF8.GetString(files.Files[SettingsPath]));
        Assert.True((await IrcSettings.LoadAsync(files, Home)).MonospaceFont);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public async Task A_broken_file_gives_the_defaults(string content)
    {
        MemoryFileSystem files = new();
        files.Files[SettingsPath] = Encoding.UTF8.GetBytes(content);
        Assert.Equal(new IrcSettings(), await IrcSettings.LoadAsync(files, Home));
    }

    [Fact]
    public async Task A_file_the_app_may_not_read_gives_the_defaults()
    {
        MemoryFileSystem files = new() { ReadFailure = new UnauthorizedAccessException("denied") };
        Assert.Equal(new IrcSettings(), await IrcSettings.LoadAsync(files, Home));
    }

    [Fact]
    public async Task Settings_written_by_a_later_version_are_still_read()
    {
        MemoryFileSystem files = new();
        files.Files[SettingsPath] = Encoding.UTF8.GetBytes("{ \"monospaceFont\": true, \"logs\": false }");
        Assert.True((await IrcSettings.LoadAsync(files, Home)).MonospaceFont);
    }

    [Fact]
    public async Task A_failed_write_is_reported_to_the_caller()
    {
        MemoryFileSystem files = new() { WriteFailure = new IOException("disk full") };
        await Assert.ThrowsAsync<IOException>(() => new IrcSettings(true).SaveAsync(files, Home));
    }
}
