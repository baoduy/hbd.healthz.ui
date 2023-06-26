using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddAuthorization(op=>
    {
        var policy= new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        op.DefaultPolicy = policy;
        
    })
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    // Add the possibility of acquiring a token to call a protected web API
    //.EnableTokenAcquisitionToCallDownstreamApi(initialScopes)

    // Enables controllers and pages to get GraphServiceClient by dependency injection
    // And use an in memory token cache
    //.AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
    //.AddInMemoryTokenCaches()
    ;
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, 
    options => {
 
        var redirectToIdpHandler = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            // Call what Microsoft.Identity.Web is doing
            await redirectToIdpHandler(context);

            // Override the redirect URI to be what you want
            context.ProtocolMessage.ResponseMode = OpenIdConnectResponseMode.FormPost;
            //context.ProtocolMessage.ResponseType = OpenIdConnectResponseType.CodeIdToken;
            context.ProtocolMessage.RedirectUri = builder.Configuration.GetValue<string>("AzureAd:RedirectUri");
            context.ProtocolMessage.PostLogoutRedirectUri = builder.Configuration.GetValue<string>("AzureAd:PostLogoutRedirectUri");
        };
    });

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = CookieSecurePolicy.Always;
    options.HttpOnly = HttpOnlyPolicy.Always;
});

//Health Check
builder.Services
    .AddHealthChecksUI()
    .AddInMemoryStorage();
    //.AddSqliteStorage($"Data Source=Db/healthz.db");


var app = builder.Build();

app
    .UseRouting()
    .UseAuthentication()
    .UseCookiePolicy()
    .UseAuthorization()
    .UseEndpoints(config => config.MapHealthChecksUI(op =>
    {
        op.UIPath = "/";
        op.ApiPath = "/api";
        op.PageTitle = "Application Health Monitoring";

    }).RequireAuthorization());

app.Run();