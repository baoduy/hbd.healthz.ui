using System.Diagnostics;
using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Automates the dependency-audit and publish-artifact acceptance scenarios directly against
/// the real `dotnet` CLI and the real project — these are the properties §3 requires to hold as
/// a property of the build, not a one-off manual check.
/// </summary>
public class SupplyChainAndPublishTests
{
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDotnetAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }

    [Fact]
    public async Task DependencyAudit_ReportsNoKnownVulnerablePackages()
    {
        // "A dependency audit of the dashboard reports no known vulnerabilities."
        var (exitCode, stdOut, stdErr) = await RunDotnetAsync(
            "list", RepoPaths.AppProject, "package", "--vulnerable", "--include-transitive");

        exitCode.Should().Be(0, because: stdErr);
        stdOut.Should().NotContain("has the following vulnerable packages",
            "no package, direct or transitive, may carry a known published advisory");
    }

    [Fact]
    public async Task Publish_ContainsNoDesignTimeToolingNoCompilerToolchainNoDevConfig()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "healthz-pubcheck-" + Guid.NewGuid());
        try
        {
            var (exitCode, _, stdErr) = await RunDotnetAsync(
                "publish", RepoPaths.AppProject, "-c", "Release", "-o", outputDir);
            exitCode.Should().Be(0, because: stdErr);

            var files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);

            files.Should().NotContain(f => Path.GetFileName(f).StartsWith("Microsoft.EntityFrameworkCore.Design", StringComparison.OrdinalIgnoreCase),
                "the design-time EF tooling must not ship in the published output");
            files.Should().NotContain(f => Path.GetFileName(f).StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase),
                "no C# compiler toolchain assembly may ship in the published output");
            files.Should().NotContain(f => Path.GetFileName(f).Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase),
                "development configuration must never leave the repository inside a release");

            // Every file, treated as raw bytes so a match can't be missed to a text-encoding
            // mismatch — mirrors "no file in the output names" from the acceptance criteria.
            foreach (var file in files)
            {
                var bytes = File.ReadAllText(file, System.Text.Encoding.Latin1);
                bytes.Should().NotContain("d430a78c-dd8c-4515-bb49-b35ba765359f",
                    $"{file} must not name the maintainer's identity tenant");
                bytes.Should().NotContain("deepsea-api.transwap.dev",
                    $"{file} must not name the internal DeepSea health endpoint");
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
