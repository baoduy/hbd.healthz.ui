using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Guards facts about the shipped configuration files themselves that the README and the
/// acceptance criteria assert but no other test reads directly — a silent edit to either file
/// would otherwise only surface as a README/behaviour mismatch someone notices by hand.
/// </summary>
public class ShippedConfigurationTests
{
    [Fact]
    public void AppSettings_DefaultStorageEngineIsSqlServer()
    {
        // "all five DbTypes engines selectable ... with SqlServer the default" — the shipped
        // appsettings.json is the thing that makes SqlServer the default; guard the fact itself.
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoPaths.RepoRoot, "HBD.HealthZ.UI", "appsettings.json"))
            .Build();

        config["HealthChecksUI:DbType"].Should().Be("SqlServer");
    }

    [Fact]
    public void DevelopmentAppSettings_RemainsUnchangedInTheRepository()
    {
        // Requester ruling: appsettings.Development.json stays exactly as it is — only its
        // exclusion from the publish output was ever in scope (covered separately).
        var path = Path.Combine(RepoPaths.RepoRoot, "HBD.HealthZ.UI", "appsettings.Development.json");
        var content = File.ReadAllText(path);

        content.Should().Contain("d430a78c-dd8c-4515-bb49-b35ba765359f")
            .And.Contain("deepsea-api.transwap.dev")
            .And.Contain("\"SignedOutCallbackPath \":", "the trailing-space key is frozen by requester ruling, not something to \"fix\"");
    }
}
