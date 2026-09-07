using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Covers the proxy trust boundary the spec calls out as this cycle's highest-value test:
/// a TLS-terminating proxy's forwarded protocol/host must be honoured for the sign-in redirect
/// only when it comes from a configured trusted proxy — never from an arbitrary caller.
/// </summary>
public class ForwardedHeadersTests
{
    private const string TrustedProxy = "203.0.113.10";
    private const string UntrustedCaller = "198.51.100.7";

    private static HealthzUiWebApplicationFactory MakeFactory(Action<Dictionary<string, string?>>? configure = null)
    {
        var factory = new HealthzUiWebApplicationFactory();
        factory.ConfigOverrides["ForwardedHeaders:KnownProxies:0"] = TrustedProxy;
        configure?.Invoke(factory.ConfigOverrides);
        return factory;
    }

    private static async Task<HttpContext> ChallengeAsync(
        HealthzUiWebApplicationFactory factory, string remoteIp, string forwardedProto, string forwardedHost)
    {
        // Force server + client to materialise before dispatching, same as CreateClient() does.
        _ = factory.Server;

        return await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/";
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        });
    }

    private static string RedirectUriQueryValue(HttpContext context)
    {
        context.Response.StatusCode.Should().BeInRange(300, 399, "an unauthenticated request must be challenged");
        var location = context.Response.Headers.Location.ToString();
        location.Should().NotBeNullOrEmpty();

        var uri = Uri.IsWellFormedUriString(location, UriKind.Absolute)
            ? new Uri(location)
            : new Uri("https://placeholder" + location);
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query["redirect_uri"].ToString();
    }

    [Fact]
    public async Task ForwardedProtoAndHost_FromTrustedProxy_IsHonouredInRedirectUri()
    {
        using var factory = MakeFactory();

        var context = await ChallengeAsync(factory, TrustedProxy, "https", "healthz.drunkcoding.net");

        var redirectUri = RedirectUriQueryValue(context);
        redirectUri.Should().StartWith("https://healthz.drunkcoding.net");
    }

    [Fact]
    public async Task ForwardedProtoAndHost_FromUntrustedCaller_IsIgnored()
    {
        using var factory = MakeFactory();

        var trusted = await ChallengeAsync(factory, TrustedProxy, "http", "localhost");
        var trustedRedirect = RedirectUriQueryValue(trusted);

        using var factory2 = MakeFactory();
        var forged = await ChallengeAsync(factory2, UntrustedCaller, "https", "evil.example");
        var forgedRedirect = RedirectUriQueryValue(forged);

        // A forged header from a non-trusted caller must change nothing about the derived
        // address: it must match what an ordinary direct request (http/localhost) produces,
        // not the attacker-claimed https://evil.example.
        forgedRedirect.Should().Be(trustedRedirect);
        forgedRedirect.Should().NotContain("evil.example");
    }

    [Fact]
    public async Task PinnedRedirectUri_OverridesForwardedHeaders_ForBackwardCompatibility()
    {
        // "An upgraded deployment that still pins absolute addresses keeps working."
        using var factory = MakeFactory(overrides =>
        {
            overrides["AzureAd:RedirectUri"] = "https://old-pinned.example/signin-oidc";
            overrides["AzureAd:PostLogoutRedirectUri"] = "https://old-pinned.example/signout-callback-oidc";
        });

        var context = await ChallengeAsync(factory, TrustedProxy, "https", "healthz.drunkcoding.net");

        var redirectUri = RedirectUriQueryValue(context);
        redirectUri.Should().Be("https://old-pinned.example/signin-oidc");
    }
}
