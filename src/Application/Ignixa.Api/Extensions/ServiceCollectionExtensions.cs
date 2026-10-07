// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Experimental.Infrastructure;
using Ignixa.Application.Features.SemanticSearch;
using Ignixa.DeId.Darts.Extensions;
using Ignixa.DeId.Extensions;
using Ignixa.Domain.Models;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Provides holistic extension methods for registering all Ignixa FHIR server services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Ignixa FHIR server services to the service collection.
    /// This is the main entry point for service registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIgnixaApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Core services (memory pooling, HTTP context, etc.)
        services.AddIgnixaCoreServices(configuration, environment);

        // Data layer services
        services.AddIgnixaDataLayerServices(configuration);

        // Search services configuration
        services.AddIgnixaSearchServices(configuration);

        // Semantic (vector) search: options, chunker, embedding generator. Registers nothing when
        // VectorSearch:Enabled is false. Validated and gated on the SQL Server data layer before any
        // service is registered, so a misconfigured or unsupported deployment fails at startup rather
        // than on the first write or query that needs an embedding.
        ValidateVectorSearchDataLayer(configuration);
        services.AddSemanticSearch(configuration);

        // Background services (hosted services, DurableTask)
        services.AddIgnixaBackgroundServices(configuration);

        // Package management HTTP clients
        services.AddIgnixaPackageManagementServices();

        // MCP server services
        services.AddIgnixaMcpServices(configuration);

        // Sidecar gRPC clients (if enabled)
        services.AddSidecarGrpcClients(configuration);

        // Sidecar logging provider (if enabled, must be after gRPC clients)
        services.AddSidecarLogging(configuration);

        services.AddExperimentalServices(configuration);

        // Conformance services (event store initializer)
        services.AddConformanceServices();

        // De-identification services
        services.AddFhirDeId();
        services.AddDartsDeId();

        return services;
    }

    /// <summary>
    /// Configures the Autofac container with all Ignixa FHIR server registrations.
    /// Call this from Host.ConfigureContainer&lt;ContainerBuilder&gt;().
    /// </summary>
    /// <param name="builder">The Autofac container builder.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environmentName">The environment name (e.g., "Development").</param>
    /// <returns>The container builder for chaining.</returns>
    public static ContainerBuilder RegisterIgnixaServices(
        this ContainerBuilder builder,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        // Core services (Autofac registrations)
        builder.RegisterCoreServices(configuration);

        // Data layer services
        builder.RegisterDataLayerServices(configuration, environmentName);

        // Search services
        builder.RegisterSearchServices(configuration);

        // Validation services
        builder.RegisterValidationServices();

        // Package management services
        builder.RegisterPackageManagementServices(configuration);

        // Application services (handlers, behaviors, etc.)
        builder.RegisterApplicationServices(configuration);

        // DurableTask activities and orchestrations
        builder.RegisterDurableTaskActivities();

        // Background job handlers
        builder.RegisterBackgroundJobHandlers();

        // Experimental services (MCP, Transform, Terminology)
        // Controlled by Experimental:Enabled and per-feature configuration
        builder.RegisterExperimentalServices(configuration);

        // Conformance services (event store, state, activation pipeline)
        builder.RegisterConformanceServices(configuration);

        return builder;
    }

    /// <summary>
    /// Fails startup when semantic search is enabled for a deployment that is not entirely on the SQL
    /// Server data layer. Vector persistence (schema version 5) only exists in
    /// <c>Ignixa.DataLayer.SqlServer</c>; a tenant on <c>FileSystem</c> storage with VectorSearch enabled
    /// would accept writes to a semantic parameter and silently never persist or query its vectors.
    /// </summary>
    /// <remarks>
    /// Reads "Tenants:Configurations" directly from <paramref name="configuration"/>, the same way
    /// <see cref="SearchServicesRegistration.ValidateTenantHostnames"/> does, rather than resolving
    /// <c>ITenantConfigurationStore</c>: this runs from <see cref="AddIgnixaApi"/>, during
    /// <c>IServiceCollection</c> registration, before any container exists to resolve a registered
    /// service from. Per-tenant storage type is also the only per-deployment signal available this
    /// early -- multi-tenant deployments with a mix of storage types are the reason this check is a
    /// cross-tenant scan rather than a single global flag.
    /// </remarks>
    /// <exception cref="OptionsValidationException">
    /// VectorSearch is enabled and at least one active tenant is not configured for SQL Server storage.
    /// </exception>
    internal static void ValidateVectorSearchDataLayer(IConfiguration configuration)
    {
        if (!configuration.GetValue("VectorSearch:Enabled", false))
        {
            return;
        }

        var tenants = configuration.GetSection("Tenants:Configurations").Get<List<TenantConfiguration>>() ?? [];
        var hasNonSqlTenant = tenants.Any(tenant => tenant.IsActive && tenant.Storage.Type is not ("SqlServer" or "SqlEntityFramework"));

        if (hasNonSqlTenant)
        {
            throw new OptionsValidationException(
                VectorSearchOptions.SectionName,
                typeof(VectorSearchOptions),
                ["VectorSearch requires the SQL Server data layer."]);
        }
    }
}
