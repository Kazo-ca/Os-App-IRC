namespace Kazo.Irc.Layout;

/// <summary>
/// Which side panel the compact layout shows (the "Interface" row of doc/en/plan.md). In the wide
/// layout the three columns are always visible and the open panel does not matter; the CSS decides
/// which layout applies from the window's width (<see cref="IrcApp.CompactWidth"/>).
/// </summary>
/// <remarks>At most one panel is open: opening one closes the other, as in a phone's chat apps.</remarks>
public sealed class IrcLayout
{
    /// <summary>Gets the open panel.</summary>
    public IrcPanel OpenPanel { get; private set; } = IrcPanel.None;

    /// <summary>Raised when <see cref="OpenPanel"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Opens a panel, or closes it when it is the open one (its toolbar button).</summary>
    /// <param name="panel">The panel of the button pressed.</param>
    public void Toggle(IrcPanel panel) => Set(OpenPanel == panel ? IrcPanel.None : panel);

    /// <summary>Closes the open panel (the backdrop, Escape, a channel picked in the list).</summary>
    public void Close() => Set(IrcPanel.None);

    /// <summary>Tells whether a panel is the open one.</summary>
    /// <param name="panel">The panel.</param>
    /// <returns><see langword="true"/> when <paramref name="panel"/> is open.</returns>
    public bool IsOpen(IrcPanel panel) => panel != IrcPanel.None && OpenPanel == panel;

    private void Set(IrcPanel panel)
    {
        if (panel == OpenPanel)
        {
            return;
        }

        OpenPanel = panel;
        Changed?.Invoke();
    }
}
