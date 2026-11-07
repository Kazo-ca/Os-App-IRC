using System.Text.Json;
using Kazo.Os.App.Abstractions;

namespace Kazo.Irc.Tests;

/// <summary>The app's manifest class, the source of the generated manifest.json (decisions 7, 8 and 20).</summary>
public sealed class ManifestTests
{
    private readonly IrcApp _app = new();

    [Fact]
    public void The_app_is_the_IRC_window_app_of_pub_kazo_in_communication()
    {
        Assert.Equal("ca.kazo.irc", _app.Id);
        Assert.Equal("IRC", _app.Name);
        Assert.Equal("pub.kazo", _app.PublisherId);
        Assert.Equal("communication", _app.Category);
        Assert.Equal("1.0.0", _app.Version);
        Assert.Equal(AppKind.Window, _app.Kind);
        Assert.Equal(typeof(IrcWindow), _app.EntryPoint);
    }

    [Fact]
    public void The_same_window_runs_on_the_desktop_and_on_phones_once_per_user()
    {
        Assert.Equal(typeof(IrcWindow), _app.MobileEntryPoint);
        Assert.True(_app.SingleInstancePerUser);

        using JsonDocument manifest = ReadManifest();
        string[] platforms = [.. manifest.RootElement.GetProperty("platform").GetProperty("supported").EnumerateArray().Select(platform => platform.GetString()!)];
        Assert.Equal(["desktop", "mobile"], platforms);
        Assert.True(manifest.RootElement.GetProperty("singleInstancePerUser").GetBoolean());
    }

    [Fact]
    public void Work_item_1_asks_only_for_its_own_folder_and_nothing_dangerous()
    {
        Assert.Equal(
            [AppCapabilities.FileSystemPrivateRead, AppCapabilities.FileSystemPrivateWrite],
            _app.Capabilities);
        Assert.DoesNotContain(_app.Capabilities, AppCapabilities.IsDangerous);
    }

    [Fact]
    public void The_window_opens_wide_enough_for_three_columns_and_shrinks_to_a_phone()
    {
        WindowOptions window = Assert.IsType<WindowOptions>(_app.Window);
        Assert.True(window.Resizable);
        Assert.True(window.DefaultWidth > IrcApp.CompactWidth, "the window opens in the three-column layout");
        Assert.True(window.MinWidth < IrcApp.CompactWidth, "a narrow window reaches the one-column layout");
        Assert.InRange(window.MinWidth, 320, 400);
    }

    [Fact]
    public void The_generated_manifest_and_its_French_texts_match_the_class()
    {
        using JsonDocument manifest = ReadManifest();
        JsonElement root = manifest.RootElement;
        Assert.Equal(_app.Id, root.GetProperty("id").GetString());
        Assert.Equal(_app.Version, root.GetProperty("version").GetString());
        Assert.Equal("communication", root.GetProperty("presentation").GetProperty("category").GetString());

        using JsonDocument french = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.App("localization", "fr.json")));
        Assert.Equal("IRC", french.RootElement.GetProperty("name").GetString());
        Assert.Equal("Discuter sur de vrais réseaux IRC", french.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void The_app_icon_is_the_illustrated_svg_of_wwwroot()
    {
        Assert.Equal(["icons/app.svg"], _app.Icons);
        string svg = File.ReadAllText(RepositoryRoot.App("wwwroot", "icons", "app.svg"));
        // Kazo OS ADR 0232: the common dark tile, a self-contained drawing (no script, no external file).
        Assert.Contains("viewBox=\"0 0 32 32\"", svg);
        Assert.Contains("#26303a", svg);
        Assert.Contains("#0a0e12", svg);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"http", svg, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllText(RepositoryRoot.App("manifest.json")));
}
