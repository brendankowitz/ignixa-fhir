// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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
                c.Resolve<IFhirBaseUriProvider>(),
                c.Resolve<IPackageResourceRepository>(),
                c.Resolve<IPackageResourceProvider>(),
                c.Resolve<ICompositeSchemaProviderRegistry>(),
                c.Resolve<ConformanceState>());
        }).SingleInstance();

        // The single authority for a tenant's service base URIs. "Fhir:BaseUri" is the deployment's public
        // FHIR root; when set it overrides the request origin entirely, which both pins the answer for
        // background indexing (reindex, $import -- no request to derive one from) and stops a forged Host
        // header from deciding whether a reference is stored as internal or external.
        builder.Register(c =>
        {
            var loggerFactory = c.Resolve<ILoggerFactory>();
            var configuredServiceRoot = ReadConfiguredServiceRoot(configuration, loggerFactory);
            return new FhirServiceBaseUriResolver(configuredServiceRoot);
        }).AsSelf().SingleInstance();

        // Eager by construction: RegisterBuildCallback runs at builder.Build(), before app.Run() accepts any
        // connection. A registration inside a lazy SingleInstance factory only runs on first resolution --
        // that would let a duplicate or malformed hostname boot "healthy" and fail on the first request
        // instead of at startup.
        builder.RegisterBuildCallback(container =>
            ValidateTenantHostnames(container.Resolve<IConfiguration>(), container.Resolve<ILoggerFactory>()));

        // Used to recognize an absolute reference that points back at this server so it reconciles with the
        // equivalent relative reference. Must be a singleton: the consumers (FhirVersionContext,
        // SearchOptionsBuilderFactory, the GraphQL type modules) are singletons, so a shorter lifetime
        // would only be honoured for whichever scope resolved them first. It still observes the calling
        // request because FhirRequestContextAccessor stores the context in a static AsyncLocal --
        // FhirRequestContextAccessorTests pins that, since a plausible "fix" to the static field would
        // silently strand this instance on an empty context.
        builder.Register<IFhirBaseUriProvider>(c =>
            new FhirRequestContextBaseUriProvider(
                c.Resolve<IFhirRequestContextAccessor>(),
                c.Resolve<FhirServiceBaseUriResolver>(),
                c.Resolve<ITenantConfigurationStore>()))
            .SingleInstance();

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

    /// <summary>
    /// Reads and validates "Fhir:BaseUri". An unusable or absent value is reported at startup rather than
    /// left to surface as references that reconcile on the request path and not on the reindex path.
    /// </summary>
    private static Uri? ReadConfiguredServiceRoot(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(FhirServiceBaseUriResolver).FullName!);
        var value = configuration["Fhir:BaseUri"];

        if (string.IsNullOrWhiteSpace(value))
        {
            logger.LogWarning(
                "Fhir:BaseUri is not configured. Absolute references that point back at this server will be "
                + "reconciled using the request's Host header, and background indexing ($reindex, $import) "
                + "cannot reconcile them at all. Set Fhir:BaseUri to this deployment's public FHIR root.");
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogError(
                "Fhir:BaseUri is not an absolute http(s) URL and will be ignored. Value: {ConfiguredBaseUri}",
                value);
            return null;
        }

        logger.LogInformation("Using configured FHIR service base {ConfiguredBaseUri}", FhirServiceBaseUri.Normalize(parsed));
        return parsed;
    }

    /// <summary>
    /// Validates tenant hostname configuration at startup. Runs eagerly via <c>RegisterBuildCallback</c>
    /// (see <see cref="RegisterSearchServices"/>), so every problem is logged and a duplicate configuration
    /// is refused before the server accepts its first request. Binds tenants directly from configuration --
    /// mirroring <c>AppSettingsTenantConfigurationStore.LoadTenants</c> -- because the build callback still
    /// runs during container construction and <see cref="Ignixa.Domain.Abstractions.ITenantConfigurationStore"/>
    /// (an Autofac-resolved singleton) is not a dependency this method should force-resolve. A duplicate
    /// hostname across tenants is fatal (the cross-tenant-confusion case): it throws here, which
    /// <c>RegisterBuildCallback</c> propagates out of <c>ContainerBuilder.Build()</c>, aborting startup. A
    /// malformed hostname is logged at Error but non-fatal: <c>AppSettingsTenantConfigurationStore</c>
    /// excludes it from the host index (it never routes), so one operator typo does not take every tenant
    /// down with it.
    /// </summary>
    internal static void ValidateTenantHostnames(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(TenantHostnameValidator).FullName!);
        var tenants = configuration.GetSection("Tenants:Configurations").Get<List<TenantConfiguration>>() ?? new List<TenantConfiguration>();
        var hostnameProblems = TenantHostnameValidator.Validate(tenants);

        foreach (var problem in hostnameProblems)
        {
            logger.LogError("Tenant hostname configuration problem: {Problem}", problem.Message);
        }

        if (hostnameProblems.Any(p => p.Kind == HostnameProblemKind.Duplicate))
        {
            throw new InvalidOperationException(
                "Duplicate tenant hostname configuration; refusing to start. See preceding log entries.");
        }
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
