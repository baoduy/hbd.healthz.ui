namespace HBD.HealthZ.UI.Tests;

internal static class RepoPaths
{
    /// <summary>Walks up from the test assembly's output directory to the repo root (the
    /// directory containing the .sln), so these tests work regardless of build configuration
    /// or working directory the test runner was launched from.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string AppProject => Path.Combine(RepoRoot, "HBD.HealthZ.UI", "HBD.HealthZ.UI.csproj");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (.sln) above " + AppContext.BaseDirectory);
    }
}
