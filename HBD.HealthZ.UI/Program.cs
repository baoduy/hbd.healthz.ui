using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthorization(op=>
    {
        var policy= new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        op.DefaultPolicy = policy;
        
    })
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAdB2C"))
    // Add the possibility of acquiring a token to call a protected web API
    //.EnableTokenAcquisitionToCallDownstreamApi(initialScopes)

    // Enables controllers and pages to get GraphServiceClient by dependency injection
    // And use an in memory token cache
    //.AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
    //.AddInMemoryTokenCaches()
    ;

//Health Check
builder.Services
    .AddHealthChecksUI()
    .AddInMemoryStorage();
    //.AddSqliteStorage($"Data Source=Db/healthz.db");


var app = builder.Build();

app
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization()
    .UseEndpoints(config => config.MapHealthChecksUI(op =>
    {
        op.UIPath = "/";
        op.ApiPath = "/api";
        op.PageTitle = "Application Health Monitoring";

    }).RequireAuthorization());

app.Run();