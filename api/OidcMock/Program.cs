using BccCode.Template.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OidcMock;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Every instance of the mock serves on localhost, so parallel local instances (isolated worktree runs) would share one session
// cookie and sign each other out. The AppHost passes a suffix per instance; the base name keeps other apps' mocks apart too.
var cookieSuffix = builder.Configuration["Session:CookieSuffix"];
var cookieName = string.IsNullOrWhiteSpace(cookieSuffix) ? "Template.OidcMock" : $"Template.OidcMock.{cookieSuffix}";

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Cookie.Name = cookieName;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    o.UseInMemoryDatabase("openiddict-mock");
    o.UseOpenIddict();
});

// The AppHost and the previews pass the app's database; without it (the mock run alone) the keys stay in memory.
var keyConnectionString = builder.Configuration.GetConnectionString("template");
(SecurityKey Signing, SecurityKey Encryption)? persistentKeys = null;
if (keyConnectionString is not null)
    persistentKeys = await PersistentKeys.LoadOrCreateAsync(keyConnectionString, builder.Configuration["OidcMock:Schema"] ?? "oidcmock");

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetEndSessionEndpointUris("/connect/endsession");
        o.AllowAuthorizationCodeFlow();
        o.RegisterScopes("openid", "profile", "email", "mfa");
        o.DisableAccessTokenEncryption();
        if (persistentKeys is { } keys)
        {
            // Persistent keys, so a restart (Cloud Run scales the mock to zero) does not invalidate the JWKS the API has cached.
            o.AddSigningKey(keys.Signing).AddEncryptionKey(keys.Encryption);
        }
        else
        {
            // Run alone, the mock keeps its keys in memory: tokens from a previous run are rejected, which is right for a mock.
            o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
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
