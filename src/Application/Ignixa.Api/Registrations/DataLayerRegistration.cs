// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Microsoft.IO;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers data layer services including repositories, EF Core, blob storage,
/// and multi-tenancy infrastructure.
/// </summary>
public static class DataLayerRegistration
{
    /// <summary>
    /// Adds data layer services to the service collection.
    /// </summary>
    public static IServiceCollection AddIgnixaDataLayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // One SqlServer reference-data cache per tenant, shared by the write path and the package-load
        // search-parameter sync. Singleton because the identity of the instance is the point: a sync against
        // any other instance leaves the write path dropping index rows.
        services.AddSingleton<Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexCacheRegistry>();

        // SchemaDeployer (DacFx-based schema deployment for brand-new, empty tenant databases)
        services.AddIgnixaSqlServerSchemaDeployment(configuration);

        // Configure blob storage options
        services.Configure<Ignixa.DataLayer.BlobStorage.Infrastructure.LocalFileBlobStorageOptions>(
            configuration.GetSection("LocalFileBlobStorage"));

        return services;
    }

    /// <summary>
    /// Registers data layer services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterDataLayerServices(
        this ContainerBuilder builder,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        // In-memory resource location index
        builder.RegisterType<InMemoryResourceLocationIndex>()
            .As<IResourceLocationIndex>()
            .SingleInstance();

        // Multi-tenancy: Tenant configuration store
        builder.RegisterType<AppSettingsTenantConfigurationStore>()
            .As<ITenantConfigurationStore>()
            .SingleInstance();

        // Register named repository factories
        RegisterRepositoryFactories(builder, configuration, environmentName);

        // Register composite factories (route to appropriate provider based on tenant config)
        RegisterCompositeFactories(builder);

        // Register partition and query execution strategies
        RegisterStrategies(builder);

        // Audit logger: Conditional registration based on Sidecar.Enabled
        builder.Register<IAuditLogger>(c =>
        {
            var sidecarOptions = c.Resolve<Ignixa.Application.Infrastructure.SidecarOptions>();

            if (sidecarOptions.Enabled)
            {
                // Sidecar mode: Use gRPC client
                var client = c.Resolve<Ignixa.Sidecar.Audit.AuditService.AuditServiceClient>();
                var logger = c.Resolve<ILogger<Ignixa.Application.Infrastructure.SidecarAuditLogger>>();
                return new Ignixa.Application.Infrastructure.SidecarAuditLogger(client, logger);
            }
            else
            {
                // Local mode: Use structured logging
                var logger = c.Resolve<ILogger<AuditLogger>>();
                return new AuditLogger(logger);
            }
        })
        .SingleInstance();

        // Metrics service: Conditional registration based on Sidecar.Enabled
        builder.Register<Ignixa.Domain.Abstractions.IMetricsService>(c =>
        {
            var sidecarOptions = c.Resolve<Ignixa.Application.Infrastructure.SidecarOptions>();

            if (sidecarOptions.Enabled)
            {
                // Sidecar mode: Use gRPC client
                var client = c.Resolve<Ignixa.Sidecar.Metrics.MetricsService.MetricsServiceClient>();
                var logger = c.Resolve<ILogger<Ignixa.Application.Infrastructure.SidecarMetricsService>>();
                return new Ignixa.Application.Infrastructure.SidecarMetricsService(client, logger);
            }
            else
            {
                // Local mode: Use structured logging
                var logger = c.Resolve<ILogger<Ignixa.Application.Infrastructure.LocalMetricsService>>();
                return new Ignixa.Application.Infrastructure.LocalMetricsService(logger);
            }
        })
        .SingleInstance();

        // Register blob storage
        RegisterBlobStorage(builder, configuration);

        // Register export stream writer factory
        builder.RegisterType<CompositeExportStreamWriterFactory>()
            .As<IExportStreamWriterFactory>()
            .SingleInstance();

        // Register ViewDefinitionLoader for SQL-on-FHIR export
        builder.RegisterType<Ignixa.DataLayer.BlobStorage.ViewDefinitionLoader>()
            .AsSelf()
            .SingleInstance();

        // Register background job repository module
        builder.RegisterModule(new Infrastructure.BackgroundJobsModule(configuration));

        // Register package resource repository
        RegisterPackageRepository(builder);

        // ISystemRepository is deliberately not registered. The EF registration here could never resolve --
        // SqlSystemRepository needs a FhirDbContext and only IDbContextFactory<FhirDbContext> is registered
        // -- and nothing resolved it: its sole consumer, ImportTerminologyResourceActivity, constructs the
        // repository and the importer by hand from a tenant-scoped context. Replacing a dead registration
        // with an equally dead one would only look like wiring. Task 6 gives the terminology path its own
        // per-tenant construction.

        return builder;
    }

    private static void RegisterRepositoryFactories(
        ContainerBuilder builder,
        IConfiguration configuration,
        string environmentName)
    {
        // FileSystem-based factories
        builder.RegisterType<FileBasedFhirRepositoryFactory>()
            .Named<IFhirRepositoryFactory>("FileSystem")
            .SingleInstance();

        builder.RegisterType<FileBasedSearchServiceFactory>()
            .Named<ISearchServiceFactory>("FileSystem")
            .SingleInstance();

        // Per-tenant database initialization: schema deploy -> upgrade -> search-parameter catalog seed
        // -> reference-data preload, in that order, before any repository is handed out.
        builder.Register(c => new SqlServerTenantInitializer(
                c.Resolve<ISchemaDeployer>(),
                c.Resolve<Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexCacheRegistry>(),
                c.Resolve<ILoggerFactory>().CreateLogger<SqlServerTenantInitializer>()))
            .AsSelf()
            .SingleInstance();

        // The host's environment name, not ASPNETCORE_ENVIRONMENT off the process: a container that
        // supplies its environment through configuration has no such variable, and reading it there
        // silently disabled the Production credential guard.
        builder.Register(c => new ManagedIdentityConnectionStringValidator(
                environmentName,
                c.Resolve<ILoggerFactory>().CreateLogger<ManagedIdentityConnectionStringValidator>()))
            .AsSelf()
            .SingleInstance();

        // SQL Server factory (implements both interfaces). The "SqlEf" name is the DI key the composite
        // factories resolve by, not a statement about the implementation: it is kept so
        // CompositeRepositoryFactory/CompositeSearchServiceFactory -- which take the inner factory as a
        // plain interface -- need no change. Tenant storage types "SqlEntityFramework" and "SqlServer" are
        // a separate, unrelated vocabulary and both remain accepted.
        builder.Register(c => new SqlServerTenantServiceFactory(
                c.Resolve<ITenantConfigurationStore>(),
                c.Resolve<ILoggerFactory>(),
                c.Resolve<RecyclableMemoryStreamManager>(),
                c.Resolve<SqlServerTenantInitializer>(),
                c.Resolve<ManagedIdentityConnectionStringValidator>(),
                c.Resolve<ISqlExecutionService>()))
            .Named<IFhirRepositoryFactory>("SqlEf")
            .Named<ISearchServiceFactory>("SqlEf")
            .AsSelf()
            .SingleInstance();
    }

    private static void RegisterCompositeFactories(ContainerBuilder builder)
    {
        // Composite repository factory
        builder.Register<IFhirRepositoryFactory>(c =>
            new CompositeRepositoryFactory(
                c.Resolve<ITenantConfigurationStore>(),
                c.ResolveNamed<IFhirRepositoryFactory>("FileSystem"),
                c.ResolveNamed<IFhirRepositoryFactory>("SqlEf")))
            .SingleInstance();

        // Composite search service factory
        builder.Register<ISearchServiceFactory>(c =>
            new CompositeSearchServiceFactory(
                c.Resolve<ITenantConfigurationStore>(),
                c.ResolveNamed<ISearchServiceFactory>("FileSystem"),
                c.ResolveNamed<ISearchServiceFactory>("SqlEf")))
            .SingleInstance();
    }

    private static void RegisterStrategies(ContainerBuilder builder)
    {
        // Partition strategy (based on tenant mode)
        builder.Register<IPartitionStrategy>(c =>
        {
            var configStore = c.Resolve<ITenantConfigurationStore>();
            var loggerFactory = c.Resolve<ILoggerFactory>();

            return configStore.Mode switch
            {
                TenantMode.Isolated => new IsolatedModePartitionStrategy(
                    loggerFactory.CreateLogger<IsolatedModePartitionStrategy>()),
                TenantMode.Distributed => throw new NotSupportedException(
                    "Distributed mode is not yet implemented (Phase 20.2+). " +
                    "Set Tenants:Mode to 'Isolated' in appsettings.json."),
                _ => throw new InvalidOperationException(
                    $"Unknown TenantMode: {configStore.Mode}. Valid values: Isolated, Distributed")
            };
        }).As<IPartitionStrategy>().SingleInstance();

        // Query execution strategy (based on tenant mode)
        builder.Register<IQueryExecutionStrategy>(c =>
        {
            var configStore = c.Resolve<ITenantConfigurationStore>();
            var searchServiceFactory = c.Resolve<ISearchServiceFactory>();
            var loggerFactory = c.Resolve<ILoggerFactory>();

            return configStore.Mode switch
            {
                TenantMode.Isolated => new PassthroughExecutionStrategy(
                    searchServiceFactory,
                    loggerFactory.CreateLogger<PassthroughExecutionStrategy>()),
                TenantMode.Distributed => throw new NotSupportedException(
                    "Distributed mode is not yet implemented (Phase 20.2+). " +
                    "Set Tenants:Mode to 'Isolated' in appsettings.json."),
                _ => throw new InvalidOperationException(
                    $"Unknown TenantMode: {configStore.Mode}. Valid values: Isolated, Distributed")
            };
        }).As<IQueryExecutionStrategy>().SingleInstance();
    }

    private static void RegisterBlobStorage(ContainerBuilder builder, IConfiguration configuration)
    {
        builder.Register(c =>
        {
            var config = c.Resolve<IConfiguration>();
            var factory = new Ignixa.DataLayer.BlobStorage.Infrastructure.BlobClientFactory(
                config,
                c.Resolve<IComponentContext>().Resolve<IServiceProvider>(),
                c.Resolve<ILogger<Ignixa.DataLayer.BlobStorage.Infrastructure.BlobClientFactory>>());
            return factory.CreateClientAsync().GetAwaiter().GetResult();
        })
        .As<IBlobStorageClient>()
        .SingleInstance();
    }

    // Package and conformance content is global rather than per-tenant, and lives in tenant 1's database.
    private const int GlobalPackageTenantId = 1;

    private static void RegisterPackageRepository(ContainerBuilder builder)
    {
        // PackageRepositoryDbContextFactory is deliberately not registered. Its only two consumers -- the EF
        // SqlPackageResourceRepository and the EF SqlSourceEventStore -- were both replaced by raw-ADO.NET
        // SqlServer implementations (below, and in ConformanceServicesRegistration), leaving nothing in the
        // process that resolves IDbContextFactory<FhirDbContext>. Keeping the registration would only look
        // like wiring, the same reason ISystemRepository above is left unregistered.

        // SQL package resource repository (Phase F Task 5a: raw ADO.NET, no DbContext).
        // Tenant 1 for the same reason the removed EF DbContext factory used it: package content is global,
        // and dbo.PackageResource has no tenant column to scope it by.
        builder.Register<IPackageResourceRepository>(c =>
            new SqlServerPackageResourceRepository(
                c.Resolve<ISqlExecutionService>(),
                GlobalPackageTenantId,
                c.Resolve<ILogger<SqlServerPackageResourceRepository>>()))
            .InstancePerDependency();

        // Terminology importer factory. SystemPartitionId rather than GlobalPackageTenantId above: the
        // terminology tables are server-wide and live in the system partition's database, matching the
        // SqlServerTerminologyService registration in ValidationServicesRegistration. A factory rather than
        // a direct ITerminologyImporter registration because the importer needs a reference-data cache that
        // is produced asynchronously and must be the registry's instance -- see ITerminologyImporterFactory.
        builder.Register<ITerminologyImporterFactory>(c =>
            new SqlServerTerminologyImporterFactory(
                c.Resolve<ISqlExecutionService>(),
                c.Resolve<Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexCacheRegistry>(),
                SystemConstants.SystemPartitionId,
                c.Resolve<ILoggerFactory>()))
            .InstancePerDependency();
    }
}
