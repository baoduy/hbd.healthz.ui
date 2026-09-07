using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "An unexpected failure discloses nothing internal" — production's UseExceptionHandler must
/// swallow the real exception and return a generic response with no stack trace, file path, or
/// dependency version.
/// </summary>
public class UnhandledFailureTests
{
    [Fact]
    public async Task UnhandledException_ReturnsGenericResponse_WithNoInternalDetail()
    {
        using var factory = new HealthzUiWebApplicationFactory
        {
            AuthenticateAsSignedIn = true,
            MapThrowingTestEndpoint = true,
        };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/__throw");

        ((int)response.StatusCode).Should().Be(500);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("An unexpected error occurred.");
        body.Should().NotContain("InvalidOperationException")
            .And.NotContain("boom")
            .And.NotContain("HBD.HealthZ.UI")
            .And.NotContain("System.")
            .And.NotContain(" at "); // stack-trace frame marker
    }
}
