using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "The CSP blocks resources the dashboard actually loads" (PR #4 review F1): HealthChecksUI's
/// own bundle injects <c>&lt;style&gt;</c> elements at runtime via webpack's style-loader
/// (Material Icons <c>@font-face</c>, the timeline layout, …) rather than shipping them in the
/// extracted stylesheet. Those blocks aren't hashable — the font's <c>blob:</c> URL is minted
/// fresh every page load — so <c>style-src</c> carries <c>'unsafe-inline'</c> instead. This
/// proves both halves of that decision stay true together: the bundle still performs runtime
/// style injection (the reason the exemption exists), and the policy the app actually serves
/// still allows it. If either drifts — the bundle stops injecting styles, or style-src reverts
/// to a strict 'self' — this fails, rather than silently blanking icons/layout in production.
/// </summary>
public class ContentSecurityPolicyStyleInjectionTests
{
    [Fact]
    public async Task StyleSrc_AllowsTheRuntimeStyleInjectionTheDashboardsOwnBundlePerforms()
    {
        using var factory = new HealthzUiWebApplicationFactory { AuthenticateAsSignedIn = true };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        response.Headers.TryGetValues("Content-Security-Policy", out var cspValues).Should().BeTrue();
        var csp = cspValues!.Single();

        var bundle = await client.GetStringAsync("/ui/resources/healthchecks-bundle.js");
        bundle.Should().Contain("document.createElement(\"style\")",
            "HealthChecksUI's bundle must still inject <style> elements at runtime for this test " +
            "(and the CSP comment's justification) to mean anything — if the package stops doing " +
            "this, style-src can safely drop 'unsafe-inline' instead");

        csp.Should().Contain("style-src 'self' 'unsafe-inline'",
            "the dashboard's bundle injects <style> elements the browser cannot verify against a " +
            "static hash (the Material Icons font src is a fresh blob: URL every page load); " +
            "without 'unsafe-inline' the browser refuses them and Material Icons/timeline layout break");
    }
}
