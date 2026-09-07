using System.Diagnostics;
using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "A fresh clone carries no local database or editor state" — the committed local database and
/// editor user-preference file are gone, and the ignore rules reject either being committed
/// again.
/// </summary>
public class RepositoryHygieneTests
{
    private static async Task<string> RunGitAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdOut;
    }

    [Fact]
    public async Task TrackedFiles_ContainNoLocalDatabaseAndNoEditorUserState()
    {
        var tracked = await RunGitAsync("ls-files");

        tracked.Should().NotContain(".db", "no local database artifact may be tracked")
            .And.NotContain(".DotSettings.user", "no contributor's personal editor preferences may be tracked");
    }

    [Theory]
    [InlineData("HBD.HealthZ.UI/Db/healthz.db")]
    [InlineData("HBD.Healthz.UI.sln.DotSettings.user")]
    public async Task IgnoreRules_RejectArtifactsOfThatKindBeingCommittedAgain(string candidatePath)
    {
        var (exitCode, _) = await RunGitCheckIgnore(candidatePath);

        // git check-ignore exits 0 when the path IS ignored.
        exitCode.Should().Be(0, $"{candidatePath} must be rejected by .gitignore so it can't be re-committed");
    }

    private static async Task<(int ExitCode, string StdOut)> RunGitCheckIgnore(string path)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("check-ignore");
        psi.ArgumentList.Add(path);

        using var process = Process.Start(psi)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdOut);
    }
}
