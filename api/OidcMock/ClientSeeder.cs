using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OidcMock;

/// <summary>
/// Registers the app as public PKCE clients, so local development needs no client secret anywhere: <c>template</c> for BCC Signon and
/// <c>template-entra</c> for the Entra sign-in (ADR 0031). Each client is checked on its own, so a mock that already knows the first
/// still gets the second.
/// </summary>
public sealed class ClientSeeder(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var urls = configuration.GetSection("Client:Urls").Get<string[]>() ?? [];

        // The Signon client may ask for the second factor with the mfa scope; Entra has no such scope, so its client may not.
        await EnsureAsync(applications, configuration["Client:ClientId"] ?? "template", urls, "signin-oidc", "signout-callback-oidc", withMfaScope: true, cancellationToken);
        await EnsureAsync(applications, configuration["Client:EntraClientId"] ?? Persona.EntraClientId, urls, "signin-entra", "signout-callback-entra", withMfaScope: false, cancellationToken);
    }

    private static async Task EnsureAsync(IOpenIddictApplicationManager applications, string clientId, string[] urls, string callback, string signedOutCallback,
        bool withMfaScope, CancellationToken cancellationToken)
    {
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null) return;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            DisplayName = clientId,
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile, Permissions.Scopes.Email,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        if (withMfaScope) descriptor.Permissions.Add(Permissions.Prefixes.Scope + "mfa");
        foreach (var url in urls)
        {
            descriptor.RedirectUris.Add(new Uri($"{url.TrimEnd('/')}/{callback}"));
            descriptor.PostLogoutRedirectUris.Add(new Uri($"{url.TrimEnd('/')}/{signedOutCallback}"));
        }

        await applications.CreateAsync(descriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
