// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Core;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Autofac module for background job repository registration. The <c>BackgroundJobs:Repository</c> setting
/// selects the implementation:
/// <list type="bullet">
/// <item><description><c>InMemory</c> (default) — development and testing; job state does not survive a
/// process restart.</description></item>
/// <item><description><c>SqlServer</c> — persistent storage in <c>dbo.BackgroundJobs</c>.</description></item>
/// </list>
/// <para>
/// Until Phase F this setting was documented but never read: the in-memory repository was registered
/// unconditionally as <see cref="IBackgroundJobRepository{T}"/> and the SQL one only <c>AsSelf()</c>, so
/// nothing could resolve it. The default is deliberately unchanged, so a deployment that does not set the
/// key keeps exactly the behaviour it had.
/// </para>
/// </summary>
public class BackgroundJobsModule(IConfiguration configuration) : Module
{
    // Jobs carry their owning tenant in dbo.BackgroundJobs.TenantId and are listed across tenants, so the
    // table lives in the shared database rather than any one tenant's. Tenant 1 is where the rest of the
    // global state (conformance, packages) already lives.
    private const int SharedJobsTenantId = 1;

    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var repository = configuration["BackgroundJobs:Repository"];

        // Validate: non-empty, unrecognized values are configuration typos and should fail fast.
        if (!string.IsNullOrEmpty(repository) && !string.Equals(repository, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unrecognized BackgroundJobs:Repository configuration value '{repository}'. " +
                $"Accepted values: 'SqlServer' (case-insensitive), or empty/absent for the InMemory default.");
        }

        if (string.Equals(repository, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            builder.RegisterGeneric(typeof(SqlServerBackgroundJobRepository<>))
                .As(typeof(IBackgroundJobRepository<>))
                .WithParameter("connectionTenantId", SharedJobsTenantId)
                .SingleInstance()
                .OnActivating((args) =>
                {
                    var logger = args.Context.Resolve<ILogger<BackgroundJobsModule>>();
                    logger.LogInformation("Using SqlServer background job repository");
                });

            return;
        }

        builder.RegisterGeneric(typeof(Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs.InMemoryBackgroundJobRepository<>))
            .As(typeof(IBackgroundJobRepository<>))
            .SingleInstance()
            .OnActivating((args) =>
            {
                var logger = args.Context.Resolve<ILogger<BackgroundJobsModule>>();
                logger.LogInformation("Using InMemory background job repository (default)");
            });
    }
}
