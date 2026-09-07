using System.Net;
using HBD.HealthZ.UI.Configs;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Add services to the container.
builder.Services
    .AddAuthorization(op =>
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        op.DefaultPolicy = policy;
    })
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(op =>
    {
        builder.Configuration.GetSection("AzureAd").Bind(op);

    })
    // Add the possibility of acquiring a token to call a protected web API
    //.EnableTokenAcquisitionToCallDownstreamApi(initialScopes)

    // Enables controllers and pages to get GraphServiceClient by dependency injection
    // And use an in memory token cache
    //.AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
    //.AddInMemoryTokenCaches()
    ;

builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
    options =>
    {
        var redirectToIdpHandler = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            // Call what Microsoft.Identity.Web is doing
            await redirectToIdpHandler(context);
            
            var redirectUrl = builder.Configuration.GetValue<string>("AzureAd:RedirectUri");
            if (!string.IsNullOrEmpty(redirectUrl))
                context.ProtocolMessage.RedirectUri = redirectUrl;

            var postLogoutRedirectUri = builder.Configuration.GetValue<string>("AzureAd:PostLogoutRedirectUri");
            if (!string.IsNullOrEmpty(postLogoutRedirectUri))
                context.ProtocolMessage.PostLogoutRedirectUri = postLogoutRedirectUri;
        };
    });

//Health Check
builder.AddHealthzUiCofig();

var app = builder.Build();

// Every response gets the standard browser-protection headers — registered first so the
// OnStarting hook still fires even when the exception handler or an auth challenge below
// short-circuits the rest of the pipeline.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        // script-src allows only the two same-origin bundles HealthChecksUI serves, plus a
        // hash for its fixed inline bootstrap-config <script> block — the library gives no
        // nonce hook, so 'unsafe-inline' would otherwise be the only way to let it run.
        // Recompute this hash (dotnet run + view-source) if the HealthChecksUI package
        // version, UIPath/ApiPath, or its Webhooks/aside-menu defaults ever change what that
        // block renders — the hash is over those exact bytes.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'sha256-29KvUQtBdGhEjD36wVjowCcbYSzQFYz/12G+Q3SwFRE='; " +
            "style-src 'self'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";
        return Task.CompletedTask;
    });
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("An unexpected error occurred.");
    }));
}

var forwardedHeadersSection = app.Configuration.GetSection("ForwardedHeaders");
var knownProxies = forwardedHeadersSection.GetSection("KnownProxies").Get<string[]>() ?? [];
var knownNetworks = forwardedHeadersSection.GetSection("KnownNetworks").Get<string[]>() ?? [];

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    ForwardLimit = forwardedHeadersSection.GetValue<int?>("ForwardLimit") ?? 1
};
// Add to (never clear) the framework's own loopback defaults — an unrestricted trust list
// is an open-redirect / phishing primitive, so a malformed entry below fails the app at
// startup (IPAddress.Parse / IPNetwork.Parse throw) rather than silently trusting nobody.
foreach (var proxy in knownProxies)
    forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse(proxy));
foreach (var network in knownNetworks)
    forwardedHeadersOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

if (knownProxies.Length == 0 && knownNetworks.Length == 0)
    app.Logger.LogWarning(
        "ForwardedHeaders:KnownProxies and ForwardedHeaders:KnownNetworks are both empty; " +
        "X-Forwarded-* headers from the ingress will be ignored until a trusted proxy is configured.");

app.UseForwardedHeaders(forwardedHeadersOptions);

var allowedHosts = app.Configuration["AllowedHosts"];
if (string.IsNullOrEmpty(allowedHosts) || allowedHosts == "*")
    app.Logger.LogWarning(
        "AllowedHosts is '{AllowedHosts}'; narrow it to this deployment's own hostnames.",
        allowedHosts);

app
    .UseRouting()
    .UseAuthentication()
    .UseCookiePolicy()
    .UseAuthorization()
    .UseEndpoints(config =>
    {
        config.MapHealthChecksUI(op =>
        {
            op.UIPath = "/";
            op.ApiPath = "/api";
            op.PageTitle = "Application Health Monitoring";
        }).RequireAuthorization();
    });

app.Run();