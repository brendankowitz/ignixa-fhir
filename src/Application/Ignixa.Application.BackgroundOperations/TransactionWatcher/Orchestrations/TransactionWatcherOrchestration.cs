// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Activities;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Orchestrations;

/// <summary>
/// DurableTask orchestration for monitoring and committing stalled transactions.
/// Uses the "eternal orchestration" pattern - runs forever with periodic scan cycles.
///
/// Each cycle:
/// 1. Gets list of active tenants from ITenantConfigurationStore
/// 2. For each tenant (except system partition 0), schedules TransactionWatcherActivity in parallel
/// 3. Waits for all activities to complete
/// 4. Sleeps for ScanInterval using a durable timer
/// 5. Calls ContinueAsNew to restart with fresh state (truncates history)
///
/// Benefits of eternal orchestration pattern:
/// - Schedule survives application restarts (state is persisted)
/// - Durable timers are reliable (no missed cycles)
/// - History is truncated on each cycle (bounded storage)
/// - Single instance management (use fixed instance ID)
/// </summary>
public class TransactionWatcherOrchestration(ITenantConfigurationStore tenantConfigurationStore)
    : TaskOrchestration<TransactionWatcherOrchestrationOutput, TransactionWatcherOrchestrationInput>
{
    /// <summary>
    /// Well-known instance ID for the singleton transaction watcher orchestration.
    /// Using a fixed ID ensures only one instance runs at a time.
    /// </summary>
    public const string SingletonInstanceId = "transaction-watcher-eternal";

    private readonly ITenantConfigurationStore _tenantConfigurationStore = tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));

    public override async Task<TransactionWatcherOrchestrationOutput> RunTask(
        OrchestrationContext context,
        TransactionWatcherOrchestrationInput input)
    {
        var tenantResults = new List<TransactionWatcherActivityOutput>();
        int totalStalled = 0;
        int totalCommitted = 0;
        int totalFailed = 0;

        try
        {
            var tenants = await _tenantConfigurationStore.GetAllTenantsAsync(CancellationToken.None);

            var activeTenants = tenants
                .Where(t => t.TenantId != SystemConstants.SystemPartitionId && t.IsActive)
                .ToList();

            if (activeTenants.Count > 0)
            {
                var activityTasks = new List<Task<TransactionWatcherActivityOutput>>();

                foreach (var tenant in activeTenants)
                {
                    var activityInput = new TransactionWatcherActivityInput(
                        TenantId: tenant.TenantId,
                        StallThreshold: input.EffectiveStallThreshold);

                    var activityTask = context.ScheduleTask<TransactionWatcherActivityOutput>(
                        typeof(TransactionWatcherActivity),
                        activityInput);

                    activityTasks.Add(activityTask);
                }

                var completedActivities = await Task.WhenAll(activityTasks);

                foreach (var activityOutput in completedActivities)
                {
                    tenantResults.Add(activityOutput);
                    totalStalled += activityOutput.StalledCount;
                    totalCommitted += activityOutput.CommittedCount;
                    totalFailed += activityOutput.FailedCount;
                }
            }
        }
        catch (Exception ex)
        {
            return await ScheduleNextCycleAsync(
                context,
                input,
                new TransactionWatcherOrchestrationOutput(
                    Success: false,
                    TotalStalled: totalStalled,
                    TotalCommitted: totalCommitted,
                    TotalFailed: totalFailed,
                    TenantResults: tenantResults.AsReadOnly(),
                    ErrorMessage: $"Transaction watcher cycle failed: {ex.Message}"));
        }

        return await ScheduleNextCycleAsync(
            context,
            input,
            new TransactionWatcherOrchestrationOutput(
                Success: true,
                TotalStalled: totalStalled,
                TotalCommitted: totalCommitted,
                TotalFailed: totalFailed,
                TenantResults: tenantResults.AsReadOnly(),
                ErrorMessage: null));
    }

    /// <summary>
    /// Schedules the next scan cycle using a durable timer and ContinueAsNew.
    /// This is the core of the eternal orchestration pattern.
    /// </summary>
    private static async Task<TransactionWatcherOrchestrationOutput> ScheduleNextCycleAsync(
        OrchestrationContext context,
        TransactionWatcherOrchestrationInput input,
        TransactionWatcherOrchestrationOutput currentResult)
    {
        var nextCycleTime = context.CurrentUtcDateTime.Add(input.EffectiveScanInterval);
        await context.CreateTimer(nextCycleTime, true);

        context.ContinueAsNew(input);

        return currentResult;
    }
}
