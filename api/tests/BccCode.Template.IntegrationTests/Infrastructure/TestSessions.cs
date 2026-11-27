using System.Collections.Concurrent;
using System.Security.Claims;
using BccCode.Template.Api.Auth;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OidcMock;

namespace BccCode.Template.IntegrationTests.Infrastructure;

/// <summary>
/// A signed-in test user. <see cref="Mfa"/> is whether the sign-in had a second factor; an administrator without it is refused on
/// /api. A user is a person the test makes up (<see cref="New"/>, so tests that share a database do not see each other's notes) or
/// a persona of the OIDC mock (<see cref="Of"/>, whose uid is the one the seeder writes notes for).
/// </summary>
public sealed record TestUser(Guid PersonId, string Name, bool Mfa, params string[] Roles)
{
    /// <summary>The provider the session came from (<see cref="AuthProviders"/>); logout signs out of its scheme.</summary>
    public string Provider { get; init; } = AuthProviders.BccSignon;

    /// <summary>A session as written before the person id claim existed: it has the Signon <c>personUid</c> claim and neither <c>template:person</c> nor a provider.</summary>
    public bool Legacy { get; init; }

    public static TestUser NewLegacy()
    {
        return new TestUser(Guid.NewGuid(), "Legacy User", Mfa: true) { Legacy = true };
    }

    public static TestUser New(string name = "Test User", params string[] roles)
    {
        return new(Guid.NewGuid(), name, Mfa: true, roles);
    }

    public static TestUser NewAdmin()
    {
        return New("Test Admin", "admin");
    }

    public static TestUser NewAdminWithoutMfa()
    {
        return new(Guid.NewGuid(), "Test Admin", Mfa: false, "admin");
    }

    /// <summary>A person who signed in with Entra, so the session says so.</summary>
    public static TestUser NewEntra(string name = "Test Entra User", params string[] roles)
    {
        return new(Guid.NewGuid(), name, Mfa: true, roles) { Provider = AuthProviders.Entra };
    }

    public static TestUser Of(Persona persona)
    {
        return new(Guid.Parse(persona.PersonUid), persona.Name, persona.Mfa, persona.Roles);
    }

    /// <summary>The identity provider session id the test sign-in carries (<c>sid</c>), which the trace of a request is tagged with.</summary>
    internal static string SidOf(string personUid)
    {
        return $"test-sid-{personUid}";
    }

    /// <summary>Two users with the same person, name, second factor, roles and provider share a session on a host.</summary>
    internal string CacheKey => $"{PersonId}|{Name}|{string.Join(',', Roles)}|{Mfa}|{Provider}|{Legacy}";
}

/// <summary>
/// Signs a test person in the way the app does: a ticket in the server-side session store and the session cookie that points to
/// it. Nothing about the sign-in is faked, so the policies, the second-factor check and the session telemetry run as in production.
/// </summary>
public static class TestSessions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scenario, TestUser> Users = [];

    /// <summary>Remembers who <c>scenario.As(user)</c> signs in; <see cref="SessionInjector"/> reads it before the request runs.</summary>
    internal static void Sign(Scenario scenario, TestUser user)
    {
        Users.AddOrUpdate(scenario, user);
    }

    internal static bool TryGetUser(Scenario scenario, out TestUser? user)
    {
        return Users.TryGetValue(scenario, out user);
    }

    // The claim the cookie handler puts into the cookie when a session store is used.
    private const string SESSION_ID_CLAIM = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    /// <summary>Returns the value for a <c>Cookie</c> request header.</summary>
    public static async Task<string> CreateAsync(IAlbaHost host, TestUser user, string issuer = TestOidc.Issuer)
    {
        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var person = user.PersonId.ToString();
        var claims = new List<Claim>
        {
            new(AuthClaims.Name, user.Name),
            new(AuthClaims.Subject, person),
            new(AuthClaims.Issuer, issuer),
            new(AuthClaims.SessionId, TestUser.SidOf(person)),
        };
        if (user.Legacy) claims.Add(new Claim(AuthClaims.SignonPersonUid, person));
        else claims.AddRange([new Claim(AuthClaims.PersonId, person), new Claim(AuthClaims.Provider, user.Provider)]);
        claims.AddRange(user.Roles.Select(role => new Claim(AuthClaims.Role, role)));
        if (user.Mfa) claims.Add(new Claim(AuthClaims.Mfa, "true"));
        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc", AuthClaims.Name, AuthClaims.Role)),
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.Add(AuthConfiguration.SessionLifetime) },
            scheme);
        var key = await options.SessionStore!.StoreAsync(ticket);

        var cookieTicket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(SESSION_ID_CLAIM, key)], scheme)), new AuthenticationProperties(), scheme);
        return $"{AuthConfiguration.SessionCookieName}={options.TicketDataFormat.Protect(cookieTicket)}";
    }
}

/// <summary>
/// Gives the requests of one host the session cookie of the user that <c>scenario.As(user)</c> names, creating the session on the
/// first request of that user. Registered with <see cref="Install"/> on every host the tests start, because the scenario
/// configuration is synchronous and a session is made asynchronously.
/// </summary>
public sealed class SessionInjector
{
    private readonly ConcurrentDictionary<string, string> _cookies = new();

    public static SessionInjector Install(IAlbaHost host)
    {
        var injector = new SessionInjector();
        host.BeforeEachAsync(async scenario =>
        {
            if (!TestSessions.TryGetUser(scenario, out var user) || user is null) return;

            if (!injector._cookies.TryGetValue(user.CacheKey, out var cookie))
                injector._cookies[user.CacheKey] = cookie = await TestSessions.CreateAsync(host, user);
            scenario.WithRequestHeader("Cookie", cookie);
        });
        return injector;
    }

    /// <summary>Forgets the sessions, after the data of the database was deleted with them.</summary>
    public void Clear()
    {
        _cookies.Clear();
    }
}
