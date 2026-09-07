using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Covers Program.cs's own config-to-options wiring for the proxy trust list (lines 100-121):
/// an empty trust list is reported at Warning, and a malformed entry fails the app at startup
/// rather than silently trusting nobody or crashing obscurely later.
/// </summary>
public class ForwardedHeadersConfigWiringTests
{
    [Fact]
    public async Task NoTrustedProxyConfigured_LogsStartupWarning()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        // Default test config has no ForwardedHeaders:KnownProxies / KnownNetworks entries.

        using var client = factory.CreateClient();
        await client.GetAsync("/"); // force host startup

        factory.CapturedLogs.Should().Contain(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("KnownProxies") &&
            l.Message.Contains("both empty"));
    }

    [Fact]
    public async Task TrustedProxyConfigured_DoesNotLogTheEmptyTrustListWarning()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["ForwardedHeaders:KnownProxies:0"] = "203.0.113.10";

        using var client = factory.CreateClient();
        await client.GetAsync("/");

        factory.CapturedLogs.Should().NotContain(l => l.Message.Contains("both empty"));
    }

    [Fact]
    public async Task TrustedNetworkConfigured_DoesNotLogTheEmptyTrustListWarning()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["ForwardedHeaders:KnownNetworks:0"] = "203.0.113.0/24";

        using var client = factory.CreateClient();
        await client.GetAsync("/");

        factory.CapturedLogs.Should().NotContain(l => l.Message.Contains("both empty"));
    }

    [Fact]
    public void MalformedKnownProxyEntry_FailsStartupInsteadOfSilentlyTrustingNobody()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["ForwardedHeaders:KnownProxies:0"] = "not-an-ip-address";

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>("a malformed trusted-proxy entry must fail the app at startup, not be ignored");
    }

    [Fact]
    public async Task DefaultAllowedHosts_LogsNarrowingWarning()
    {
        using var factory = new HealthzUiWebApplicationFactory();
        // appsettings.json ships AllowedHosts: "*".

        using var client = factory.CreateClient();
        await client.GetAsync("/");

        factory.CapturedLogs.Should().Contain(l =>
            l.Level == LogLevel.Warning && l.Message.Contains("AllowedHosts"));
    }
}
