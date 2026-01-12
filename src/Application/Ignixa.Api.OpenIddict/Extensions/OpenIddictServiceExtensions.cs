using Ignixa.Api.OpenIddict.Configuration;
using Ignixa.Api.OpenIddict.Data;
using Ignixa.Api.OpenIddict.Services;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.OpenIddict.Extensions;

/// <summary>
/// Extension methods for registering OpenIddict services.
/// </summary>
public static class OpenIddictServiceExtensions
{
    /// <summary>
    /// Adds the embedded OpenIddict server to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="schemaProviders">
    /// Optional FHIR schema providers for version-aware scope generation.
    /// If not provided, uses common scopes only.
    /// </param>
    public static IServiceCollection AddIgnixaOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<IFhirSchemaProvider>? schemaProviders = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration
        services.Configure<OpenIddictServerOptions>(
            configuration.GetSection(OpenIddictServerOptions.SectionName));

        var options = configuration
            .GetSection(OpenIddictServerOptions.SectionName)
            .Get<OpenIddictServerOptions>();

        if (options?.Enabled is not true)
        {
            return services;
        }

        // Register services
        services.AddSingleton<DevelopmentUserService>();
        services.AddScoped<OpenIddictDataSeeder>();

        // Configure database context
        services.AddDbContext<OpenIddictDbContext>(dbOptions =>
        {
            if (options.UseInMemoryStorage)
            {
                dbOptions.UseInMemoryDatabase("OpenIddict");
            }
            else
            {
                if (string.IsNullOrEmpty(options.ConnectionString))
                {
                    throw new InvalidOperationException(
                        "ConnectionString must be provided when UseInMemoryStorage is false.");
                }

                dbOptions.UseSqlServer(options.ConnectionString);
            }

            dbOptions.UseOpenIddict();
        });

        // Configure OpenIddict
        services.AddOpenIddict()
            .AddCore(coreOptions =>
            {
                coreOptions.UseEntityFrameworkCore()
                    .UseDbContext<OpenIddictDbContext>();
            })
            .AddServer(serverOptions =>
            {
                // Enable OAuth 2.0 flows
                serverOptions.SetTokenEndpointUris("/connect/token")
                    .SetAuthorizationEndpointUris("/connect/authorize");

                serverOptions.AllowClientCredentialsFlow()
                    .AllowPasswordFlow()
                    .AllowRefreshTokenFlow()
                    .AllowAuthorizationCodeFlow();

                // Register signing and encryption credentials
                if (options.DisableAccessTokenEncryption)
                {
                    serverOptions.DisableAccessTokenEncryption();
                }

                serverOptions.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();

                // Configure issuer
                if (!string.IsNullOrEmpty(options.Issuer))
                {
                    serverOptions.SetIssuer(new Uri(options.Issuer));
                }

                // Register SMART on FHIR scopes (version-aware)
                var scopes = GenerateScopes(schemaProviders);
                serverOptions.RegisterScopes(scopes);

                // Development settings
                var aspNetCoreBuilder = serverOptions.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .EnableAuthorizationEndpointPassthrough();

                if (options.DisableHttpsRequirement)
                {
                    aspNetCoreBuilder.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(validationOptions =>
            {
                validationOptions.UseLocalServer();
                validationOptions.UseAspNetCore();
            });

        return services;
    }

    /// <summary>
    /// Ensures the OpenIddict database is created and seeded.
    /// </summary>
    public static async Task InitializeOpenIddictAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = serviceProvider.GetService<IOptions<OpenIddictServerOptions>>()?.Value;

        if (options?.Enabled is not true)
        {
            return;
        }

        // Ensure database is created
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        // Seed data
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<OpenIddictDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Generates SMART scopes based on available schema providers.
    /// </summary>
    private static string[] GenerateScopes(IEnumerable<IFhirSchemaProvider>? schemaProviders)
    {
        if (schemaProviders is null || !schemaProviders.Any())
        {
            // Fall back to common scopes when no schema providers available
            return SmartScopeGenerator.GenerateCommonScopes().ToArray();
        }

        // Generate scopes for all FHIR versions
        var resourceTypeSets = schemaProviders
            .Select(p => p.ResourceTypeNames.AsEnumerable())
            .ToArray();

        return SmartScopeGenerator.GenerateAllScopesForVersions(resourceTypeSets).ToArray();
    }
}
