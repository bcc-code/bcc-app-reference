using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace OidcMock;

/// <summary>Registers the app as a public PKCE client, so local development needs no client secret anywhere.</summary>
public sealed class ClientSeeder(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var clientId = configuration["Client:ClientId"] ?? "template";
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null) return;

        var urls = configuration.GetSection("Client:Urls").Get<string[]>() ?? [];
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            DisplayName = clientId,
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile, Permissions.Scopes.Email, Permissions.Prefixes.Scope + "mfa",
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var url in urls)
        {
            descriptor.RedirectUris.Add(new Uri($"{url.TrimEnd('/')}/signin-oidc"));
            descriptor.PostLogoutRedirectUris.Add(new Uri($"{url.TrimEnd('/')}/signout-callback-oidc"));
        }

        await applications.CreateAsync(descriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
