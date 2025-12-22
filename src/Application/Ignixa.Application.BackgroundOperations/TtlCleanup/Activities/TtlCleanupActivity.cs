// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Activities;

/// <summary>
/// DurableTask activity that performs TTL cleanup for a single tenant.
/// Queries ResourceTtl table for expired entries (ExpiresAt &lt; now) and hard-deletes the resources.
/// Hard deletion removes all versions of the resource plus all search parameter indexes.
/// </summary>
public class TtlCleanupActivity(
    IFhirRepositoryFactory repositoryFactory,
    IAuditLogger auditLogger,
    ILogger<TtlCleanupActivity> logger)
    : AsyncTaskActivity<TtlCleanupActivityInput, TtlCleanupActivityOutput>
{
    private readonly IFhirRepositoryFactory _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    private readonly IAuditLogger _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    private readonly ILogger<TtlCleanupActivity> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task<TtlCleanupActivityOutput> ExecuteAsync(
        TaskContext context,
        TtlCleanupActivityInput input)
    {
        _logger.LogInformation(
            "Starting TTL cleanup activity: TenantId={TenantId}, BatchSize={BatchSize}",
            input.TenantId,
            input.BatchSize);

        int expiredCount = 0;
        int deletedCount = 0;
        int failedCount = 0;

        try
        {
            var repository = await _repositoryFactory.GetRepositoryAsync(input.TenantId, CancellationToken.None);

            // Query for expired resources using provider-agnostic interface
            var expiredResources = await repository.GetExpiredResourcesAsync(
                input.BatchSize,
                CancellationToken.None);

            expiredCount = expiredResources.Count;

            if (expiredCount == 0)
            {
                _logger.LogDebug("No expired resources found for tenant {TenantId}", input.TenantId);
                return new TtlCleanupActivityOutput(
                    TenantId: input.TenantId,
                    ExpiredCount: 0,
                    DeletedCount: 0,
                    FailedCount: 0,
                    ErrorMessage: null);
            }

            _logger.LogWarning(
                "Found {Count} expired resources for tenant {TenantId}",
                expiredCount,
                input.TenantId);

            // Hard-delete each expired resource (all versions + search indexes + TTL entry)
            foreach (var resource in expiredResources)
            {
                try
                {
                    _logger.LogInformation(
                        "Deleting expired resource {ResourceType}/{ResourceId} (expired at {ExpiresAt}) for tenant {TenantId}",
                        resource.ResourceType,
                        resource.ResourceId,
                        resource.ExpiresAt,
                        input.TenantId);

                    await repository.HardDeleteResourceAsync(
                        resource.ResourceTypeId,
                        resource.ResourceId,
                        CancellationToken.None);

                    deletedCount++;

                    _logger.LogInformation(
                        "Successfully deleted expired resource {ResourceType}/{ResourceId} for tenant {TenantId}",
                        resource.ResourceType,
                        resource.ResourceId,
                        input.TenantId);

                    // Audit log the successful deletion
                    _auditLogger.LogTtlDeletion(
                        input.TenantId,
                        resource.ResourceType,
                        resource.ResourceId,
                        resource.ExpiresAt,
                        success: true);
                }
                catch (Exception ex)
                {
                    failedCount++;

                    _logger.LogError(
                        ex,
                        "Failed to delete expired resource {ResourceType}/{ResourceId} for tenant {TenantId}",
                        resource.ResourceType,
                        resource.ResourceId,
                        input.TenantId);

                    // Audit log the failed deletion
                    _auditLogger.LogTtlDeletion(
                        input.TenantId,
                        resource.ResourceType,
                        resource.ResourceId,
                        resource.ExpiresAt,
                        success: false);
                }
            }

            _logger.LogInformation(
                "Completed TTL cleanup activity: TenantId={TenantId}, Expired={Expired}, Deleted={Deleted}, Failed={Failed}",
                input.TenantId,
                expiredCount,
                deletedCount,
                failedCount);

            return new TtlCleanupActivityOutput(
                TenantId: input.TenantId,
                ExpiredCount: expiredCount,
                DeletedCount: deletedCount,
                FailedCount: failedCount,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fatal error during TTL cleanup activity for tenant {TenantId}",
                input.TenantId);

            return new TtlCleanupActivityOutput(
                TenantId: input.TenantId,
                ExpiredCount: expiredCount,
                DeletedCount: deletedCount,
                FailedCount: failedCount,
                ErrorMessage: ex.Message);
        }
    }
}
