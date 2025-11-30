// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Terminology;
using Ignixa.DataLayer.SqlEntityFramework;
using Medino;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Services;

/// <summary>
/// Bootstrap service that triggers terminology imports for existing packages on startup.
/// Scans all tenants for packages with pending terminology resources and creates orchestrations.
/// Runs once after package preload completes.
/// </summary>
public class TerminologyImportBootstrapService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TerminologyImportBootstrapService> _logger;

    public TerminologyImportBootstrapService(
        IServiceProvider serviceProvider,
        ILogger<TerminologyImportBootstrapService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait a bit for package preload to complete
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            _logger.LogInformation("Starting terminology import bootstrap scan...");

            using var scope = _serviceProvider.CreateScope();
            var repositoryFactory = scope.ServiceProvider.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Scan tenant 1 only (system partition doesn't have terminology resources)
            var tenantId = 1;

            using var context = await repositoryFactory.GetDbContextAsync(tenantId, stoppingToken);

            // Group pending terminology resources by package
            var packageGroups = await context.PackageResources
                .Where(pr => pr.IsActive)
                .Where(pr => pr.ResourceType == "CodeSystem" || pr.ResourceType == "ValueSet" || pr.ResourceType == "ConceptMap")
                .Where(pr => pr.TerminologyImportStatus == null || pr.TerminologyImportStatus == "Pending" || pr.TerminologyImportStatus == "Failed")
                .GroupBy(pr => new { pr.PackageId, pr.PackageVersion })
                .Select(g => new
                {
                    g.Key.PackageId,
                    g.Key.PackageVersion,
                    ResourceIds = g.Select(pr => pr.PackageResourceId).ToList()
                })
                .ToListAsync(stoppingToken);

            if (packageGroups.Count == 0)
            {
                _logger.LogInformation("No pending terminology imports found");
                return;
            }

            _logger.LogInformation(
                "Found {PackageCount} package(s) with {ResourceCount} total pending terminology resources",
                packageGroups.Count,
                packageGroups.Sum(g => g.ResourceIds.Count));

            // Trigger import for each package
            foreach (var package in packageGroups)
            {
                try
                {
                    _logger.LogInformation(
                        "Triggering terminology import for {PackageId}@{PackageVersion} ({Count} resources)",
                        package.PackageId,
                        package.PackageVersion,
                        package.ResourceIds.Count);

                    var importEvent = new TerminologyImportTriggeredEvent(
                        TenantId: tenantId,
                        PackageId: package.PackageId,
                        PackageVersion: package.PackageVersion,
                        PackageResourceIds: package.ResourceIds);

                    await mediator.PublishAsync(importEvent, stoppingToken);

                    _logger.LogInformation(
                        "Triggered orchestration for {PackageId}@{PackageVersion}",
                        package.PackageId,
                        package.PackageVersion);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to trigger terminology import for {PackageId}@{PackageVersion}: {Message}",
                        package.PackageId,
                        package.PackageVersion,
                        ex.Message);
                }
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
}
