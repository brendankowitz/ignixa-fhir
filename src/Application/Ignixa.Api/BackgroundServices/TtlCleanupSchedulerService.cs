// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Api.Configuration;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Orchestrations;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.BackgroundServices;

/// <summary>
/// Background service that schedules TTL cleanup orchestrations on a configurable interval.
/// Uses DurableTask orchestrations for distributed, resilient cleanup operations.
/// </summary>
public sealed class TtlCleanupSchedulerService(
    TaskHubClient taskHubClient,
    IOptions<TtlCleanupOptions> options,
    ILogger<TtlCleanupSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("TTL cleanup scheduler is disabled");
            return;
        }

        logger.LogInformation("TTL cleanup scheduler starting (Interval: {Interval})", options.Value.ScanInterval);

        using var timer = new PeriodicTimer(options.Value.ScanInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScheduleCleanupOrchestrationAsync(stoppingToken);
        }
    }

    private async Task ScheduleCleanupOrchestrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var instanceId = $"ttl-cleanup-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var input = new TtlCleanupOrchestrationInput(options.Value.BatchSize);

            await taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(TtlCleanupOrchestration),
                instanceId,
                input);

            logger.LogInformation("Scheduled TTL cleanup orchestration: {InstanceId}", instanceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to schedule TTL cleanup orchestration");
        }
    }
}
