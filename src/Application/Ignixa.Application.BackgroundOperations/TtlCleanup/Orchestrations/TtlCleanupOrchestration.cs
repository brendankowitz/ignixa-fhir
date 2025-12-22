// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Activities;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Orchestrations;

/// <summary>
/// DurableTask orchestration for TTL (Time-To-Live) cleanup operations.
/// Coordinates cleanup across all active tenants:
/// 1. Gets list of active tenants from ITenantConfigurationStore
/// 2. For each tenant (except system partition 0), schedules TtlCleanupActivity
/// 3. Waits for all activities to complete in parallel
/// 4. Aggregates and returns results
///
/// This design achieves efficient multi-tenant cleanup by:
/// - Parallel execution across tenants (activities run simultaneously)
/// - Per-tenant batching (BatchSize limits resources processed per run)
/// - Idempotent operations (safe to retry failed activities)
/// </summary>
public class TtlCleanupOrchestration(ITenantConfigurationStore tenantConfigurationStore)
    : TaskOrchestration<TtlCleanupOrchestrationOutput, TtlCleanupOrchestrationInput>
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));

    public override async Task<TtlCleanupOrchestrationOutput> RunTask(
        OrchestrationContext context,
        TtlCleanupOrchestrationInput input)
    {
        var tenantResults = new List<TtlCleanupActivityOutput>();
        int totalExpired = 0;
        int totalDeleted = 0;
        int totalFailed = 0;

        try
        {
            // Phase 1: Get all active tenants
            var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(CancellationToken.None);

            // Filter out system partition (Partition 0) and inactive tenants
            var activeTenants = tenants
                .Where(t => t.TenantId != SystemConstants.SystemPartitionId && t.IsActive)
                .ToList();

            if (activeTenants.Count == 0)
            {
                return new TtlCleanupOrchestrationOutput(
                    Success: true,
                    TotalExpired: 0,
                    TotalDeleted: 0,
                    TotalFailed: 0,
                    TenantResults: Array.Empty<TtlCleanupActivityOutput>(),
                    ErrorMessage: null);
            }

            // Phase 2: Schedule cleanup activity for each active tenant
            var activityTasks = new List<Task<TtlCleanupActivityOutput>>();

            foreach (var tenant in activeTenants)
            {
                var activityInput = new TtlCleanupActivityInput(
                    TenantId: tenant.TenantId,
                    BatchSize: input.BatchSize);

                var activityTask = context.ScheduleTask<TtlCleanupActivityOutput>(
                    typeof(TtlCleanupActivity),
                    activityInput);

                activityTasks.Add(activityTask);
            }

            // Phase 3: Wait for all activities to complete in parallel
            var completedActivities = await Task.WhenAll(activityTasks);

            // Phase 4: Aggregate results from all tenants
            foreach (var activityOutput in completedActivities)
            {
                tenantResults.Add(activityOutput);
                totalExpired += activityOutput.ExpiredCount;
                totalDeleted += activityOutput.DeletedCount;
                totalFailed += activityOutput.FailedCount;
            }

            return new TtlCleanupOrchestrationOutput(
                Success: true,
                TotalExpired: totalExpired,
                TotalDeleted: totalDeleted,
                TotalFailed: totalFailed,
                TenantResults: tenantResults.AsReadOnly(),
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new TtlCleanupOrchestrationOutput(
                Success: false,
                TotalExpired: totalExpired,
                TotalDeleted: totalDeleted,
                TotalFailed: totalFailed,
                TenantResults: tenantResults.AsReadOnly(),
                ErrorMessage: $"TTL cleanup orchestration failed: {ex.Message}");
        }
    }
}
