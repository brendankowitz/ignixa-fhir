// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.EventStore;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Registers conformance services in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterConformanceServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Event store implementation (SQL-based)
        builder.RegisterType<SqlSourceEventStore>()
            .As<ISourceEventStore>()
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
