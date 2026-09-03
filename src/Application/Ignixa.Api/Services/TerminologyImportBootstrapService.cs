// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Triggers terminology imports for packages already loaded, once, at startup. Covers the resources that
/// were stored before terminology auto-import was switched on, and anything a previous run left
/// non-terminal.
/// <para>
/// Reads through <see cref="IPackageResourceRepository"/> rather than a tenant-scoped <c>FhirDbContext</c>.
/// The EF version opened a context for tenant 1 and explained the choice as "the system partition doesn't
/// have terminology resources", which described the wrong thing: <c>dbo.PackageResource</c> is not
/// partitioned at all, so there was never more than one place to look.
/// </para>
/// </summary>
public class TerminologyImportBootstrapService : BackgroundService
{
    // The tenant stamped on the published event, for the import orchestration's request context. It does
    // not select which resources are found -- package content is global.
    private const int OrchestrationTenantId = 1;

    private static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TerminologyImportBootstrapService> _logger;
    private readonly TimeSpan _startupDelay;

    public TerminologyImportBootstrapService(
        IServiceProvider serviceProvider,
        ILogger<TerminologyImportBootstrapService> logger,
        TimeSpan? startupDelay = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _startupDelay = startupDelay ?? DefaultStartupDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Package preload runs as its own hosted service with no completion signal to wait on, so this
            // is a delay rather than a handshake. Anything it stores after the scan is covered by
            // PackageLoadedTerminologyImportHandler instead.
            if (_startupDelay > TimeSpan.Zero)
            {
                await Task.Delay(_startupDelay, stoppingToken);
            }

            _logger.LogInformation("Starting terminology import bootstrap scan...");

            using var scope = _serviceProvider.CreateScope();
            var packageResources = scope.ServiceProvider.GetRequiredService<IPackageResourceRepository>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var pending = await packageResources.ListPendingTerminologyImportsAsync(
                packageId: null, packageVersion: null, stoppingToken);

            if (pending.Count == 0)
            {
                _logger.LogInformation("No pending terminology imports found");
                return;
            }

            _logger.LogInformation(
                "Found {PackageCount} package(s) with {ResourceCount} total pending terminology resources",
                pending.Count,
                pending.Sum(p => p.PackageResourceIds.Count));

            foreach (var package in pending)
            {
                await TriggerAsync(mediator, package.PackageId, package.PackageVersion, package.PackageResourceIds, stoppingToken);
            }

            _logger.LogInformation("Terminology import bootstrap completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Terminology import bootstrap cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during terminology import bootstrap: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// One package's failure must not stop the others being offered, which is why this catches per package
    /// rather than around the loop.
    /// </summary>
    private async Task TriggerAsync(
        IMediator mediator,
        string packageId,
        string packageVersion,
        IReadOnlyList<long> packageResourceIds,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Triggering terminology import for {PackageId}@{PackageVersion} ({Count} resources)",
                packageId,
                packageVersion,
                packageResourceIds.Count);

            await mediator.PublishAsync(
                new TerminologyImportTriggeredEvent(
                    TenantId: OrchestrationTenantId,
                    PackageId: packageId,
                    PackageVersion: packageVersion,
                    PackageResourceIds: packageResourceIds),
                cancellationToken);

            _logger.LogInformation(
                "Triggered orchestration for {PackageId}@{PackageVersion}", packageId, packageVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to trigger terminology import for {PackageId}@{PackageVersion}: {Message}",
                packageId,
                packageVersion,
                ex.Message);
        }
    }
}
