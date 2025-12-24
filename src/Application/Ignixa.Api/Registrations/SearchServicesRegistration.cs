// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Parsing;
using Ignixa.Specification;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers search-related services including search parameter parsing,
/// version-aware search options builders, and schema providers.
/// </summary>
public static class SearchServicesRegistration
{
    /// <summary>
    /// Adds search configuration options to the service collection.
    /// </summary>
    public static IServiceCollection AddIgnixaSearchServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure SearchParameter conflict resolution options
        services.Configure<SearchParameterResolutionOptions>(
            configuration.GetSection("SearchParameters:ConflictResolution"));

        return services;
    }

    /// <summary>
    /// Registers search services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterSearchServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Query parameter parser
        builder.RegisterType<QueryParameterParser>()
            .As<IQueryParameterParser>()
            .InstancePerDependency();

        // FhirVersionContext (version-specific providers, indexers, etc.)
        builder.Register<IFhirVersionContext>(c =>
        {
            var config = c.Resolve<IConfiguration>();
            var options = new SearchParameterResolutionOptions();
            config.GetSection("SearchParameters:ConflictResolution").Bind(options);

            return new FhirVersionContext(
                c.Resolve<ILoggerFactory>(),
                options,
                c.Resolve<IPackageResourceRepository>(),
                c.Resolve<IPackageResourceProvider>(),
                c.Resolve<ICompositeSchemaProviderRegistry>(),
                c.Resolve<ConformanceState>());
        }).SingleInstance();

        // SearchOptionsBuilderFactory
        builder.RegisterType<SearchOptionsBuilderFactory>()
            .As<ISearchOptionsBuilderFactory>()
            .SingleInstance();

        // Default ISearchOptionsBuilder (R4 for background operations)
        builder.Register<ISearchOptionsBuilder>(c =>
        {
            var factory = c.Resolve<ISearchOptionsBuilderFactory>();
            return factory.Create(FhirVersion.R4);
        }).SingleInstance();

        // FhirSchemaProvider resolver (backward compatibility)
        builder.Register<Func<FhirVersion, IFhirSchemaProvider>>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            return (FhirVersion version) => versionContext.GetSchemaProvider(version, tenantId: null);
        }).SingleInstance();

        // Search parameter definition managers
        RegisterSearchParameterDefinitionManagers(builder);

        // Composite schema provider registry
        builder.Register<ICompositeSchemaProviderRegistry>(c =>
            new CompositeSchemaProviderRegistry(
                c.Resolve<ILogger<CompositeSchemaProviderRegistry>>(),
                debounceDelay: TimeSpan.FromSeconds(1)))
            .SingleInstance();

        return builder;
    }

    private static void RegisterSearchParameterDefinitionManagers(ContainerBuilder builder)
    {
        // Default search parameter definition manager (R4)
        builder.Register<ISearchParameterDefinitionManager>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            return versionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);
        }).SingleInstance();

        // Searchable resolver
        builder.Register<ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver>(c =>
        {
            var manager = c.Resolve<ISearchParameterDefinitionManager>();
            return () => manager;
        }).SingleInstance();

        // Default compartment definition manager (R4)
        builder.Register<ICompartmentDefinitionManager>(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            return versionContext.GetCompartmentDefinitionManager(FhirVersion.R4);
        }).SingleInstance();
    }
}
