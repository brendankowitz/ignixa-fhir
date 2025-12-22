// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.DataLayer.SqlEntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Activities;

/// <summary>
/// DurableTask activity that performs TTL cleanup for a single tenant.
/// Queries ResourceTtl table for expired entries (ExpiresAt &lt; now) and hard-deletes the resources.
/// Hard deletion removes all versions of the resource plus all search parameter indexes.
/// </summary>
public class TtlCleanupActivity(
    SqlEntityFrameworkRepositoryFactory repositoryFactory,
    ILogger<TtlCleanupActivity> logger)
    : AsyncTaskActivity<TtlCleanupActivityInput, TtlCleanupActivityOutput>
{
    private readonly SqlEntityFrameworkRepositoryFactory _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
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
            using var dbContext = await _repositoryFactory.GetDbContextAsync(input.TenantId, CancellationToken.None);

            var now = DateTimeOffset.UtcNow;

            // Query ResourceTtl table for expired entries
            // Join with Resource to ensure we only process current (non-deleted, non-history) resources
            var expiredResources = await (from ttl in dbContext.ResourceTtls
                                          join r in dbContext.Resources on new { ttl.ResourceTypeId, ttl.ResourceId } equals new { r.ResourceTypeId, r.ResourceId }
                                          where ttl.ExpiresAt < now
                                              && !r.IsHistory
                                              && !r.IsDeleted
                                          select new
                                          {
                                              ttl.ResourceTypeId,
                                              ttl.ResourceId,
                                              ttl.ExpiresAt
                                          })
                .Take(input.BatchSize)
                .ToListAsync(CancellationToken.None);

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
                        resource.ResourceTypeId,
                        resource.ResourceId,
                        resource.ExpiresAt,
                        input.TenantId);

                    await HardDeleteResourceAsync(
                        dbContext,
                        resource.ResourceTypeId,
                        resource.ResourceId,
                        CancellationToken.None);

                    deletedCount++;

                    _logger.LogInformation(
                        "Successfully deleted expired resource {ResourceType}/{ResourceId} for tenant {TenantId}",
                        resource.ResourceTypeId,
                        resource.ResourceId,
                        input.TenantId);
                }
                catch (Exception ex)
                {
                    failedCount++;

                    _logger.LogError(
                        ex,
                        "Failed to delete expired resource {ResourceType}/{ResourceId} for tenant {TenantId}",
                        resource.ResourceTypeId,
                        resource.ResourceId,
                        input.TenantId);
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

    /// <summary>
    /// Hard deletes a resource and all its history versions and search indexes, and the TTL entry.
    /// Uses ExecuteSqlInterpolatedAsync for efficient bulk deletion.
    /// Based on TtlCleanupService.HardDeleteResourceAsync but also deletes from ResourceTtl table.
    /// </summary>
    private async Task HardDeleteResourceAsync(
        FhirDbContext dbContext,
        short resourceTypeId,
        string resourceId,
        CancellationToken cancellationToken)
    {
        // Delete all search parameter indexes for all versions + resource versions + TTL entry
        // Use temp table approach for efficient deletion
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"-- Create temp table to hold surrogate IDs
              DECLARE @SurrogateIds TABLE (ResourceSurrogateId BIGINT PRIMARY KEY);

              -- Find all surrogate IDs for this resource
              INSERT INTO @SurrogateIds (ResourceSurrogateId)
              SELECT ResourceSurrogateId
              FROM dbo.Resource
              WHERE ResourceTypeId = {resourceTypeId} AND ResourceId = {resourceId};

              -- Delete all search parameter indexes
              DELETE FROM dbo.ReferenceSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenText WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.StringSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.UriSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.NumberSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.QuantitySearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.DateTimeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.ReferenceTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenTokenCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenDateTimeCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenQuantityCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenStringCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);
              DELETE FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @SurrogateIds);

              -- Delete all resource versions (current + history)
              DELETE FROM dbo.Resource WHERE ResourceTypeId = {resourceTypeId} AND ResourceId = {resourceId};

              -- Delete TTL entry (after successfully deleting resource)
              DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = {resourceTypeId} AND ResourceId = {resourceId};",
            cancellationToken);
    }
}
