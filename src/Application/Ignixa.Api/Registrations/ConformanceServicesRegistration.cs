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
using Microsoft.Extensions.Logging;

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

        return services;
    }

    // Conformance and package state is global, not per-tenant, and has always lived in tenant 1's
    // database (see PackageRepositoryDbContextFactory's registration in DataLayerRegistration).
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
        // PackageRepositoryDbContextFactory, which is registered against tenant 1's connection string
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

        // PackageActivationPipeline
        builder.RegisterType<PackageActivationPipeline>()
            .AsSelf()
            .InstancePerDependency();

        return builder;
    }
}
