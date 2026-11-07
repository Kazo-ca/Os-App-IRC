using Kazo.Irc.Layout;

namespace Kazo.Irc.Tests.Layout;

/// <summary>The side panels of the compact layout (one column on phones).</summary>
public sealed class IrcLayoutTests
{
    [Fact]
    public void No_panel_is_open_at_start()
    {
        IrcLayout layout = new();
        Assert.Equal(IrcPanel.None, layout.OpenPanel);
        Assert.False(layout.IsOpen(IrcPanel.Channels));
        Assert.False(layout.IsOpen(IrcPanel.Users));
        Assert.False(layout.IsOpen(IrcPanel.None));
    }

    [Fact]
    public void A_toolbar_button_opens_its_panel_and_a_second_press_closes_it()
    {
        IrcLayout layout = new();
        layout.Toggle(IrcPanel.Channels);
        Assert.True(layout.IsOpen(IrcPanel.Channels));
        layout.Toggle(IrcPanel.Channels);
        Assert.Equal(IrcPanel.None, layout.OpenPanel);
    }

    [Fact]
    public void Opening_one_panel_closes_the_other()
    {
        IrcLayout layout = new();
        layout.Toggle(IrcPanel.Channels);
        layout.Toggle(IrcPanel.Users);
        Assert.True(layout.IsOpen(IrcPanel.Users));
        Assert.False(layout.IsOpen(IrcPanel.Channels));
    }

    [Fact]
    public void Close_closes_the_open_panel_and_says_so_once()
    {
        IrcLayout layout = new();
        int changes = 0;
        layout.Changed += () => changes++;
        layout.Toggle(IrcPanel.Users);
        layout.Close();
        layout.Close();
        Assert.Equal(IrcPanel.None, layout.OpenPanel);
        Assert.Equal(2, changes);
    }
}
