// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.SqlOnFhir.packages;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers package management services including NPM package loading,
/// embedded packages, and implementation guide providers.
/// </summary>
public static class PackageManagementRegistration
{
    /// <summary>
    /// Adds package management HTTP clients to the service collection.
    /// </summary>
    public static IServiceCollection AddIgnixaPackageManagementServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Resilient HTTP message handler
        services.AddTransient<ResilientHttpMessageHandler>();

        // NPM package loader HTTP client
        services.AddHttpClient<NpmPackageLoader>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                sp.GetRequiredService<ResilientHttpMessageHandler>());

        // NPM package search service HTTP client
        services.AddHttpClient<NpmPackageSearchService>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                sp.GetRequiredService<ResilientHttpMessageHandler>());

        return services;
    }

    /// <summary>
    /// Registers package management services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterPackageManagementServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        // Embedded packages
        builder.RegisterType<SqlOnFhirEmbeddedPackage>()
            .As<IEmbeddedPackage>()
            .SingleInstance();

        // Composite package loader (embedded -> npm)
        builder.Register<IPackageLoader>(c =>
        {
            var loggerFactory = c.Resolve<ILoggerFactory>();
            var httpClient = c.Resolve<HttpClient>();
            var config = c.Resolve<IConfiguration>();

            // Create embedded loader
            var embeddedPackages = c.Resolve<IEnumerable<IEmbeddedPackage>>();
            var embeddedLoader = new EmbeddedPackageLoader(
                embeddedPackages,
                loggerFactory.CreateLogger<EmbeddedPackageLoader>());

            // Configure NPM options
            var npmOptions = new NpmPackageLoaderOptions
            {
                RegistryUrl = config.GetValue<string>(
                    "PackageManagement:NpmRegistry:RegistryUrl",
                    "https://packages.fhir.org"),
                EnableRetryPolicies = config.GetValue<bool>(
                    "PackageManagement:NpmRegistry:EnableRetryPolicies",
                    true)
            };

            var npmLoader = new NpmPackageLoader(
                httpClient,
                cacheManager: null,
                options: npmOptions,
                loggerFactory.CreateLogger<NpmPackageLoader>());

            return new CompositePackageLoader(
                loggerFactory.CreateLogger<CompositePackageLoader>(),
                embeddedLoader,
                npmLoader);
        })
        .As<IPackageLoader>()
        .InstancePerDependency();

        // Package extractor
        builder.RegisterType<PackageExtractor>()
            .As<IPackageExtractor>()
            .InstancePerDependency();

        // Package resource importer
        builder.RegisterType<PackageResourceImporter>()
            .As<IPackageResourceImporter>()
            .InstancePerDependency();

        // Implementation guide provider
        builder.Register<IImplementationGuideProvider>(c =>
            new ImplementationGuideProvider(
                c.Resolve<IPackageLoader>(),
                c.Resolve<IPackageExtractor>(),
                c.Resolve<IPackageResourceImporter>(),
                c.Resolve<Ignixa.Domain.Abstractions.IPackageResourceRepository>(),
                c.Resolve<ILogger<ImplementationGuideProvider>>()))
            .SingleInstance();

        // NPM package search service
        builder.Register<INpmPackageSearchService>(c =>
        {
            var config = c.Resolve<IConfiguration>();
            var httpClient = c.Resolve<HttpClient>();
            var loggerFactory = c.Resolve<ILoggerFactory>();

            var npmOptions = new NpmPackageLoaderOptions
            {
                RegistryUrl = config.GetValue<string>(
                    "PackageManagement:NpmRegistry:RegistryUrl",
                    "https://packages.fhir.org"),
                EnableRetryPolicies = config.GetValue<bool>(
                    "PackageManagement:NpmRegistry:EnableRetryPolicies",
                    true)
            };

            return new NpmPackageSearchService(
                httpClient,
                npmOptions,
                loggerFactory.CreateLogger<NpmPackageSearchService>());
        })
        .As<INpmPackageSearchService>()
        .SingleInstance();

        return builder;
    }
}
