using Ignixa.Api.OpenIddict.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Ignixa.Api.OpenIddict.Services;

/// <summary>
/// Seeds pre-configured client applications and scopes into OpenIddict.
/// </summary>
public sealed class OpenIddictDataSeeder(
    IServiceProvider serviceProvider,
    IOptions<OpenIddictServerOptions> options)
{
    private readonly OpenIddictServerOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await SeedScopesAsync(scopeManager, cancellationToken);
        await SeedClientApplicationsAsync(applicationManager, cancellationToken);
    }

    private static async Task SeedScopesAsync(
        IOpenIddictScopeManager scopeManager,
        CancellationToken cancellationToken)
    {
        // FHIR scopes based on SMART App Launch
        var fhirScopes = new[]
        {
            "fhirUser",
            "launch",
            "launch/patient",
            "offline_access",
            "online_access",
            "openid",
            "profile",
            // Patient-level scopes
            "patient/*.read",
            "patient/*.write",
            "patient/*.cruds",
            "patient/Patient.read",
            "patient/Observation.read",
            "patient/Condition.read",
            "patient/MedicationRequest.read",
            // User-level scopes
            "user/*.read",
            "user/*.write",
            "user/*.cruds",
            "user/Patient.read",
            "user/Observation.read",
            "user/Condition.read",
            "user/MedicationRequest.read",
            // System-level scopes
            "system/*.read",
            "system/*.write",
            "system/*.cruds",
            "system/Patient.read",
            "system/Observation.read",
            "system/Condition.read",
            "system/MedicationRequest.read"
        };

        foreach (var scopeName in fhirScopes)
        {
            if (await scopeManager.FindByNameAsync(scopeName, cancellationToken) is null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = scopeName,
                    DisplayName = $"FHIR {scopeName}",
                    Resources = { "fhir-api" }
                }, cancellationToken);
            }
        }
    }

    private async Task SeedClientApplicationsAsync(
        IOpenIddictApplicationManager applicationManager,
        CancellationToken cancellationToken)
    {
        foreach (var clientConfig in _options.ClientApplications)
        {
            var existingClient = await applicationManager.FindByClientIdAsync(
                clientConfig.ClientId,
                cancellationToken);

            if (existingClient is not null)
            {
                // Update existing client
                await applicationManager.UpdateAsync(
                    existingClient,
                    CreateDescriptor(clientConfig),
                    cancellationToken);
            }
            else
            {
                // Create new client
                await applicationManager.CreateAsync(
                    CreateDescriptor(clientConfig),
                    cancellationToken);
            }
        }
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(ClientApplicationOptions config)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = config.ClientId,
            DisplayName = config.DisplayName ?? config.ClientId,
            ClientType = config.IsPublicClient
                ? OpenIddictConstants.ClientTypes.Public
                : OpenIddictConstants.ClientTypes.Confidential
        };

        if (!string.IsNullOrEmpty(config.ClientSecret) && !config.IsPublicClient)
        {
            descriptor.ClientSecret = config.ClientSecret;
        }

        foreach (var redirectUri in config.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri));
        }

        foreach (var postLogoutUri in config.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutUri));
        }

        foreach (var grantType in config.GrantTypes)
        {
            descriptor.Permissions.Add($"{OpenIddictConstants.Permissions.Prefixes.GrantType}{grantType}");
        }

        // Add endpoint permissions based on grant types
        if (config.GrantTypes.Contains("authorization_code"))
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        }

        if (config.GrantTypes.Contains("client_credentials") || config.GrantTypes.Contains("password"))
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (config.GrantTypes.Contains("refresh_token"))
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        // Add scope permissions
        foreach (var scope in config.Scopes)
        {
            descriptor.Permissions.Add($"{OpenIddictConstants.Permissions.Prefixes.Scope}{scope}");
        }

        return descriptor;
    }
}
