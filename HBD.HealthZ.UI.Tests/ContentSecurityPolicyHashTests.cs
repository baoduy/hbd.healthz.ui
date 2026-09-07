using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// "The CSP `sha256-` hash pin is an untested assertion" per the verification brief: proves the
/// pinned hash in Program.cs's Content-Security-Policy header actually matches the bytes of an
/// inline script the dashboard's own HTML renders — a stale hash would silently blank the
/// dashboard while every other test stays green.
/// </summary>
public class ContentSecurityPolicyHashTests
{
    [Fact]
    public async Task PinnedScriptHash_MatchesAnInlineScriptTheDashboardActuallyRenders()
    {
        using var factory = new HealthzUiWebApplicationFactory { AuthenticateAsSignedIn = true };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        response.Headers.TryGetValues("Content-Security-Policy", out var cspValues).Should().BeTrue();
        var csp = cspValues!.Single();

        var pinnedHash = Regex.Match(csp, @"sha256-([A-Za-z0-9+/=]+)").Groups[1].Value;
        pinnedHash.Should().NotBeNullOrEmpty("Program.cs pins a sha256- hash for HealthChecksUI's inline bootstrap script");

        var html = await response.Content.ReadAsStringAsync();
        var inlineScripts = Regex.Matches(html, @"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        inlineScripts.Should().NotBeEmpty("the dashboard's own HTML must render at least one inline script for the pin to protect");

        var actualHashes = inlineScripts
            .Select(script => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script))))
            .ToList();

        actualHashes.Should().Contain(pinnedHash,
            "the pinned hash must match the exact bytes of the inline script the dashboard renders today, " +
            "or the CSP silently blocks it and blanks the dashboard");
    }
}
