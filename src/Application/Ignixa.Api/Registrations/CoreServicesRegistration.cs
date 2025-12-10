// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Services;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IO;
using Microsoft.IdentityModel.Tokens;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers core services including RecyclableMemoryStreamManager, serialization,
/// HTTP context services, and host configuration options.
/// </summary>
public static class CoreServicesRegistration
{
    /// <summary>
    /// Adds core services to the service collection.
    /// </summary>
    public static IServiceCollection AddIgnixaCoreServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Startup timing diagnostics
        services.AddStartupTimingDiagnostics();

        // OpenAPI documentation
        services.AddOpenApi();

        // Memory cache for CapabilityStatement caching
        services.AddMemoryCache();

        // RecyclableMemoryStreamManager as singleton (memory pooling)
        services.AddSingleton<RecyclableMemoryStreamManager>();

        // HTTP context services
        services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
        services.AddHttpContextAccessor();

        // FHIR request context accessor (centralized request context pattern)
        services.AddScoped<IFhirRequestContextAccessor, FhirRequestContextAccessor>();

        // HTTP client factory for background operations
        services.AddHttpClient();

        // Configure ForwardedHeaders for Docker/container deployments
        ConfigureForwardedHeaders(services, configuration);

        // Configure Host Filtering
        ConfigureHostFiltering(services, configuration);

        // Configure BackgroundService resilience
        services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });

        // Configure authorization options from appsettings.json (Authorization section)
        services.Configure<Ignixa.Application.Features.Authorization.AuthorizationOptions>(
            configuration.GetSection(Ignixa.Application.Features.Authorization.AuthorizationOptions.SectionName));

        // JWT Bearer authentication
        ConfigureJwtAuthentication(services, configuration, environment);

        return services;
    }

    /// <summary>
    /// Registers core services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterCoreServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Application version info for CapabilityStatement
        builder.RegisterType<Ignixa.Application.Infrastructure.ApplicationVersionInfo>()
            .As<IApplicationVersionInfo>()
            .SingleInstance();

        // FHIRPath parser (shared across validation and PATCH operations)
        builder.RegisterType<FhirPathParser>()
            .AsSelf()
            .SingleInstance();

        return builder;
    }

    private static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedPrefix;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }
    }

    private static void ConfigureHostFiltering(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HostFilteringOptions>(options =>
        {
            var allowedHosts = configuration["AllowedHosts"]?.Split(";") ?? ["*"];
            foreach (var host in allowedHosts)
            {
                options.AllowedHosts.Add(host);
            }
        });
    }

    private static void ConfigureJwtAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Check if authorization is enabled
        var authEnabled = configuration.GetValue<bool>("Authorization:Enabled", true);
        if (!authEnabled)
        {
            return;
        }

        // In production, RequireAuthentication must be true
        var requireAuth = configuration.GetValue<bool>("Authorization:RequireAuthentication", true);
        if (environment.IsProduction() && !requireAuth)
        {
            throw new InvalidOperationException(
                "Authorization:RequireAuthentication must be true in production environments. " +
                "Disabling authentication in production is a security risk.");
        }

        // Configure JWT Bearer authentication with support for multiple providers
        var authConfig = configuration.GetSection("Authentication");
        var provider = authConfig["Provider"] ?? "JwtBearer";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Configure based on provider type
                ConfigureJwtBearerOptions(options, authConfig, provider, configuration);

                // Map claims using FHIR claim types
                // Use standard OpenID Connect "name" claim or fallback to "sub"
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType =
                    Ignixa.Application.Features.Authorization.FhirClaimTypes.Role;

                // Events for debugging and custom processing
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerHandler>>();
                        logger.LogWarning(
                            "Authentication failed: {Error}",
                            context.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerHandler>>();
                        logger.LogDebug(
                            "Token validated for user: {User}",
                            context.Principal?.Identity?.Name ?? "Unknown");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
    }

    private static void ConfigureJwtBearerOptions(
        JwtBearerOptions options,
        IConfigurationSection authConfig,
        string provider,
        IConfiguration configuration)
    {
        switch (provider)
        {
            case "Entra":
                ConfigureEntraAuthentication(options, authConfig);
                break;
            case "Okta":
                ConfigureOktaAuthentication(options, authConfig);
                break;
            case "OIDC":
                ConfigureOidcAuthentication(options, authConfig);
                break;
            case "OpenIddict":
                ConfigureOpenIddictAuthentication(options, authConfig);
                break;
            case "JwtBearer":
            default:
                ConfigureGenericJwtAuthentication(options, authConfig);
                break;
        }
    }

    private static void ConfigureEntraAuthentication(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var entraConfig = authConfig.GetSection("Entra");
        var instance = entraConfig["Instance"] ?? "https://login.microsoftonline.com/";
        var tenantId = entraConfig["TenantId"] ?? throw new InvalidOperationException("Entra:TenantId is required");
        var audience = entraConfig["Audience"] ?? throw new InvalidOperationException("Entra:Audience is required");

        options.Authority = $"{instance}{tenantId}/v2.0";
        options.Audience = audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }

    private static void ConfigureOktaAuthentication(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var oktaConfig = authConfig.GetSection("Okta");
        var domain = oktaConfig["Domain"] ?? throw new InvalidOperationException("Okta:Domain is required");
        var audience = oktaConfig["Audience"] ?? throw new InvalidOperationException("Okta:Audience is required");

        options.Authority = $"https://{domain}";
        options.Audience = audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }

    private static void ConfigureOidcAuthentication(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var oidcConfig = authConfig.GetSection("OIDC");
        var authority = oidcConfig["Authority"] ?? throw new InvalidOperationException("OIDC:Authority is required");
        var audience = oidcConfig["Audience"];

        options.Authority = authority;
        if (!string.IsNullOrEmpty(audience))
        {
            options.Audience = audience;
        }
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrEmpty(audience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }

    private static void ConfigureOpenIddictAuthentication(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var openIddictConfig = authConfig.GetSection("OpenIddict");
        var issuer = openIddictConfig["Issuer"] ?? throw new InvalidOperationException("OpenIddict:Issuer is required");
        var audience = openIddictConfig["Audience"];

        options.Authority = issuer;
        if (!string.IsNullOrEmpty(audience))
        {
            options.Audience = audience;
        }
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrEmpty(audience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }

    private static void ConfigureGenericJwtAuthentication(JwtBearerOptions options, IConfigurationSection authConfig)
    {
        var authority = authConfig["Authority"];
        var audience = authConfig["Audience"];
        var issuer = authConfig["Issuer"];

        if (!string.IsNullOrEmpty(authority))
        {
            options.Authority = authority;
        }

        if (!string.IsNullOrEmpty(audience))
        {
            options.Audience = audience;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrEmpty(issuer) || !string.IsNullOrEmpty(authority),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrEmpty(audience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    }
}
