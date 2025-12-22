// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Activities;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Orchestrations;

/// <summary>
/// DurableTask orchestration for monitoring and committing stalled transactions.
/// Runs as an eternal orchestration using durable timers based on configured ScanInterval.
/// Multi-tenant aware: Delegates to TransactionWatcherActivity to scan all active tenants.
///
/// Pattern: Monitor/Polling (see ADR-2510)
/// - Uses context.CreateTimer() for reliable durable timers that survive restarts
/// - Schedules TransactionWatcherActivity for the actual scanning work
/// - Continues running indefinitely until disabled or terminated via TaskHubClient.TerminateInstanceAsync()
/// </summary>
public class TransactionWatcherOrchestration : TaskOrchestration<TransactionWatcherOrchestrationOutput, TransactionWatcherOrchestrationInput>
{
    public override async Task<TransactionWatcherOrchestrationOutput> RunTask(
        OrchestrationContext context,
        TransactionWatcherOrchestrationInput input)
    {
        // If disabled, return immediately
        if (!input.Enabled)
        {
            return new TransactionWatcherOrchestrationOutput(
                TotalScans: 0,
                TotalCommitted: 0,
                TotalFailed: 0,
                StoppedReason: "Disabled via configuration (TransactionWatcher:Enabled = false)");
        }

        // Eternal orchestration loop - runs indefinitely until terminated
        // Note: Counters are reset on each replay, but the orchestration
        // continues from where it left off due to DurableTask's replay semantics
        while (true)
        {
            // Execute the scan activity
            var activityInput = new TransactionWatcherActivityInput(
                StallThreshold: input.StallThreshold);

            await context.ScheduleTask<TransactionWatcherActivityOutput>(
                typeof(TransactionWatcherActivity),
                activityInput);

            // Wait for the configured scan interval before next scan
            // Using durable timer - survives restarts and doesn't block workers
            var nextScanTime = context.CurrentUtcDateTime.Add(input.ScanInterval);
            await context.CreateTimer(nextScanTime, CancellationToken.None);
        }
    }
}
