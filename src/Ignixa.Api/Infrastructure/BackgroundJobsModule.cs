// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Autofac module for background job repository registration.
/// Registers both in-memory (development) and SQL Server (production) implementations.
/// Configuration setting "BackgroundJobs:Repository" determines which is used:
/// - "InMemory" (default): Development/testing with in-memory storage
/// - "SqlServer": Production with SQL Server persistent storage
/// </summary>
public class BackgroundJobsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Default: Register in-memory repository for development
        builder.RegisterGeneric(typeof(Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs.InMemoryBackgroundJobRepository<>))
            .As(typeof(IBackgroundJobRepository<>))
            .SingleInstance();

        // Optional: Also register SQL Server repository (can be used if configured)
        builder.RegisterGeneric(typeof(Ignixa.DataLayer.SqlEntityFramework.Features.BackgroundJobs.SqlBackgroundJobRepository<>))
            .AsSelf()
            .SingleInstance();
    }
}
