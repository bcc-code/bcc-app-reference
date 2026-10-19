using BccCode.Template.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OidcMock;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Cookie.Name = "oidc-mock";
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    o.UseInMemoryDatabase("openiddict-mock");
    o.UseOpenIddict();
});

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetEndSessionEndpointUris("/connect/endsession");
        o.AllowAuthorizationCodeFlow();
        o.RegisterScopes("openid", "profile", "email");
        o.DisableAccessTokenEncryption();
        // Keys live in memory: tokens from a previous run are rejected, which is right for a mock.
        o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        o.UseAspNetCore()
            // CI runners do not trust the ASP.NET dev certificate, so the API may reach the mock over plain HTTP.
            .DisableTransportSecurityRequirement()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();
    });

builder.Services.AddHostedService<ClientSeeder>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

app.MapOidcEndpoints();
app.MapDefaultEndpoints();
app.Run();
