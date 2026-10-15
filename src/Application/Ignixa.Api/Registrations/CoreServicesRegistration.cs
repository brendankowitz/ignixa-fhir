// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Authentication;
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
                options.KnownIPNetworks.Clear();
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

        var authConfig = configuration.GetSection("Authentication");

        // Use single OIDC configurator (works with all standard OIDC providers)
        var configurator = new Ignixa.Api.Authentication.Providers.OidcJwtProviderConfigurator();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Configure OIDC settings
                configurator.Configure(options, authConfig);

                // Common configuration (applies to all providers)
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
}
