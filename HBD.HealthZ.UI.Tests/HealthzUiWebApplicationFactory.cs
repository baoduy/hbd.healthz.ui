using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HBD.HealthZ.UI.Tests;

/// <summary>
/// Boots the real <c>Program.cs</c> pipeline in-process. Supplies a static OIDC discovery
/// document so the OpenIdConnect handler never makes the network call it would otherwise make
/// to Entra ID's metadata endpoint on first challenge — the sandbox this runs in has no network
/// access, and none of the scenarios under test need a real identity provider, only the
/// dashboard's own redirect-URI derivation.
/// </summary>
public class HealthzUiWebApplicationFactory : WebApplicationFactory<Program>
{
    public Dictionary<string, string?> ConfigOverrides { get; } = new();

    /// <summary>
    /// When true, requests authenticate as a fake signed-in operator instead of being
    /// challenged — for tests that need to reach past <c>RequireAuthenticatedUser()</c>.
    /// </summary>
    public bool AuthenticateAsSignedIn { get; init; }

    /// <summary>
    /// When true, appends a route that always throws — mapped after the app's own pipeline so
    /// it still runs behind Program.cs's own <c>UseExceptionHandler</c>, to prove that
    /// middleware behaves the same for a failure the test controls as it does for a real one.
    /// </summary>
    public bool MapThrowingTestEndpoint { get; init; }

    /// <summary>Captures every log message the host emits via its real logging pipeline, so
    /// tests can assert on Program.cs's own startup warnings without scraping Console.</summary>
    public List<(LogLevel Level, string Message)> CapturedLogs { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureLogging(lb => lb.AddProvider(new CapturingLoggerProvider(CapturedLogs)));

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["HealthChecksUI:DbType"] = "Memory",
            });
            config.AddInMemoryCollection(ConfigOverrides);
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                // Pre-seed a static discovery document so the handler skips the metadata fetch
                // it would otherwise make to Entra ID (no network in this sandbox). Setting
                // ConfigurationManager directly — rather than just Options.Configuration —
                // because the framework's own PostConfigure (registered earlier by
                // AddOpenIdConnect) already ran and would otherwise win a race on which one
                // takes effect.
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0",
                    AuthorizationEndpoint = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/oauth2/v2.0/authorize",
                    TokenEndpoint = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/oauth2/v2.0/token",
                    JwksUri = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/discovery/v2.0/keys",
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            if (AuthenticateAsSignedIn)
            {
                services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, null);
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = FakeAuthHandler.SchemeName;
                    o.DefaultScheme = FakeAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FakeAuthHandler.SchemeName;
                });
            }

            if (MapThrowingTestEndpoint)
                services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter());
        });
    }

    /// <summary>
    /// Appends "/__throw" after the app's real pipeline (built by <c>next</c>) rather than
    /// before it, so the request still passes through Program.cs's own exception-handler
    /// middleware — which wraps everything registered after it — on its way back out.
    /// </summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__throw", inner => inner.Run(_ => throw new InvalidOperationException(
                "boom — simulated unhandled failure, must never reach the client")));
        };
    }

    private sealed class CapturingLoggerProvider(List<(LogLevel Level, string Message)> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                    sink.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
