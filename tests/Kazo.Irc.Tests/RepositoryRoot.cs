namespace Kazo.Irc.Tests;

/// <summary>Finds the repository's files from the test output folder.</summary>
internal static class RepositoryRoot
{
    /// <summary>Gets the repository root: the folder that holds <c>Kazo.Irc.slnx</c>.</summary>
    public static string Path { get; } = Find();

    /// <summary>Gets a file of the app project <c>src/Kazo.Irc</c>.</summary>
    /// <param name="parts">The path inside the project.</param>
    public static string App(params string[] parts) =>
        System.IO.Path.Combine([Path, "src", "Kazo.Irc", .. parts]);

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Kazo.Irc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Kazo.Irc.slnx not found above the test output.");
    }
}
