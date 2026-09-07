using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "Responses carry browser-protection headers." The header middleware in Program.cs is
/// registered first specifically so it still fires on a challenge/redirect response, before
/// authentication runs — verified here on the unauthenticated path since that is the response
/// every unauthenticated visitor actually receives.
/// </summary>
public class SecurityHeadersTests : IClassFixture<HealthzUiWebApplicationFactory>
{
    private readonly HealthzUiWebApplicationFactory _factory;

    public SecurityHeadersTests(HealthzUiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Response_CarriesStandardBrowserProtectionHeaders()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).Should().BeTrue();
        contentTypeOptions.Should().Contain("nosniff");

        response.Headers.TryGetValues("X-Frame-Options", out var frameOptions).Should().BeTrue();
        frameOptions.Should().Contain("DENY");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        var cspValue = csp!.Single();
        cspValue.Should().Contain("frame-ancestors 'none'");
        cspValue.Should().Contain("default-src 'self'");
    }

    [Fact]
    public async Task Response_DoesNotAdvertiseServerHeader()
    {
        // builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false) — no "Server:" banner.
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        response.Headers.Server.Should().BeEmpty();
    }
}
