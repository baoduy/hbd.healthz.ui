using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "An unauthenticated visitor is shown no health data" — the dashboard requires sign-in
/// unconditionally, on every route, and leaks no health result to a visitor who has not.
/// </summary>
public class UnauthenticatedAccessTests : IClassFixture<HealthzUiWebApplicationFactory>
{
    private readonly HealthzUiWebApplicationFactory _factory;

    public UnauthenticatedAccessTests(HealthzUiWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/api")]
    public async Task UnauthenticatedRequest_IsChallenged_AndDisclosesNoHealthData(string path)
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        ((int)response.StatusCode).Should().BeInRange(300, 399, "an unauthenticated visitor must be sent to sign in, never served the dashboard");
        response.Headers.Location.Should().NotBeNull();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("HealthChecksUI", "no dashboard/health content may be served before sign-in");
    }
}
