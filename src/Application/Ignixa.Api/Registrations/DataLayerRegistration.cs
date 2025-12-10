// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.DataLayer.InMemoryIndex;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Features.PackageManagement;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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

        // MultiTenantSearchIndexCache (per-tenant cache for search index reference data)
        services.AddSingleton<Ignixa.DataLayer.SqlEntityFramework.Indexing.MultiTenantSearchIndexCache>();

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

        // Register audit logger
        builder.RegisterType<AuditLogger>()
            .As<IAuditLogger>()
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
        builder.RegisterModule<Infrastructure.BackgroundJobsModule>();

        // Register package resource repository
        RegisterPackageRepository(builder);

        // Register SqlSystemRepository for system URL normalization
        builder.RegisterType<SqlSystemRepository>()
            .As<ISystemRepository>()
            .InstancePerDependency();

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

        // SQL Entity Framework factory (implements both interfaces)
        builder.Register(c => new SqlEntityFrameworkRepositoryFactory(
                c.Resolve<ITenantConfigurationStore>(),
                c.Resolve<ILoggerFactory>(),
                c.Resolve<RecyclableMemoryStreamManager>(),
                c.Resolve<Ignixa.DataLayer.SqlEntityFramework.Indexing.MultiTenantSearchIndexCache>(),
                environmentName))
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

    private static void RegisterPackageRepository(ContainerBuilder builder)
    {
        // Package repository DbContext factory
        builder.Register<PackageRepositoryDbContextFactory>(c =>
        {
            var tenantStore = c.Resolve<ITenantConfigurationStore>();
            var loggerFactory = c.Resolve<ILoggerFactory>();

            var tenantConfig = tenantStore.GetTenantConfigurationAsync(1, default).AsTask().GetAwaiter().GetResult();
            if (tenantConfig == null || string.IsNullOrEmpty(tenantConfig.Storage.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Tenant 1 connection string is required for global package resource repository");
            }

            return new PackageRepositoryDbContextFactory(tenantConfig.Storage.ConnectionString, loggerFactory);
        }).SingleInstance();

        // SQL package resource repository
        builder.Register<IPackageResourceRepository>(c =>
            new SqlPackageResourceRepository(
                c.Resolve<PackageRepositoryDbContextFactory>(),
                c.Resolve<ILogger<SqlPackageResourceRepository>>()))
            .InstancePerDependency();
    }
}
