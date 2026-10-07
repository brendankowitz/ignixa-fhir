// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.EventStore;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers event-sourced conformance management services including the event store,
/// conformance state projection, and package activation pipeline.
/// </summary>
public static class ConformanceServicesRegistration
{
    /// <summary>
    /// Adds conformance services to the service collection.
    /// </summary>
    public static IServiceCollection AddConformanceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the ConformanceState initializer as a hosted service (runs once at startup)
        services.AddHostedService<ConformanceStateInitializerService>();

        // Register the ConformanceState sync service for multi-instance scenarios (polls periodically)
        services.AddHostedService<ConformanceStateSyncService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    // Conformance and package state is global, not per-tenant, and has always lived in tenant 1's
    // database (see GlobalPackageTenantId in DataLayerRegistration).
    private const int GlobalConformanceTenantId = 1;

    /// <summary>
    /// Registers conformance services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterConformanceServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Event store implementation (SQL-based).
        // Tenant 1 mirrors what the EF implementation already resolved to: its DbContext came from
        // PackageRepositoryDbContextFactory, which was constructed against tenant 1's connection string
        // because conformance and package state is global rather than per-tenant. Phase F Task 1 moved the
        // implementation to raw ADO.NET; it did not change which database the store reads and writes.
        builder.Register<ISourceEventStore>(c => new SqlServerSourceEventStore(
                c.Resolve<ISqlExecutionService>(),
                GlobalConformanceTenantId,
                c.Resolve<ILogger<SqlServerSourceEventStore>>()))
            .SingleInstance();

        // ConformanceState (singleton, in-memory projection)
        builder.RegisterType<ConformanceState>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConformanceCacheRefresher>()
            .AsSelf()
            .As<IConformanceCacheRefresher>()
            .SingleInstance();

        builder.RegisterType<ConformanceRefreshPublisher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConformanceDefinitionsSynchronizer>()
            .As<IConformanceDefinitionsSynchronizer>()
            .SingleInstance();

        builder.Register(c => new ConformanceLease(
                c.Resolve<IOptions<ConformanceTransitionOptions>>(),
                TimeProvider.System,
                c.Resolve<ILogger<ConformanceLease>>()))
            .As<IConformanceLease>()
            .SingleInstance();

        builder.RegisterType<NullReindexTrigger>()
            .As<IReindexTrigger>()
            .SingleInstance();

        builder.RegisterType<ReindexJobLock>()
            .As<IReindexJobLock>()
            .SingleInstance();

        builder.RegisterType<SearchParameterTransitionCommitter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SearchParameterTransitionReconciler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DurableSearchParameterTransitionScheduler>()
            .As<ISearchParameterTransitionScheduler>()
            .SingleInstance();

        // PackageActivationPipeline
        builder.RegisterType<PackageActivationPipeline>()
            .AsSelf()
            .InstancePerDependency();

        return builder;
    }
}
