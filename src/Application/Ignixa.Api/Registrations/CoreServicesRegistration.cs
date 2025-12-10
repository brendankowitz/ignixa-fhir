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
}
