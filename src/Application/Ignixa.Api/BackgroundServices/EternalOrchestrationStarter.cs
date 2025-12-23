// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Api.Configuration;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Models;
using Ignixa.Application.BackgroundOperations.TtlCleanup.Orchestrations;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Orchestrations;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.BackgroundServices;

/// <summary>
/// Background service that ensures all eternal orchestrations are running.
/// Eternal orchestrations use durable timers and ContinueAsNew for periodic execution.
/// This service runs once at startup to ensure each orchestration exists.
/// </summary>
public sealed class EternalOrchestrationStarter(
    TaskHubClient taskHubClient,
    IOptions<TtlCleanupOptions> ttlCleanupOptions,
    IOptions<TransactionWatcherOptions> transactionWatcherOptions,
    ILogger<EternalOrchestrationStarter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for DurableTask infrastructure to be ready
        logger.LogInformation("EternalOrchestrationStarter waiting for DurableTask infrastructure...");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        logger.LogInformation("EternalOrchestrationStarter starting eternal orchestrations...");

        // Start all eternal orchestrations
        await EnsureOrchestrationAsync<TtlCleanupOrchestration>(
            TtlCleanupOrchestration.SingletonInstanceId,
            new TtlCleanupOrchestrationInput(
                ttlCleanupOptions.Value.BatchSize,
                ttlCleanupOptions.Value.ScanInterval),
            ttlCleanupOptions.Value.Enabled,
            stoppingToken);

        await EnsureOrchestrationAsync<TransactionWatcherOrchestration>(
            TransactionWatcherOrchestration.SingletonInstanceId,
            new TransactionWatcherOrchestrationInput(
                transactionWatcherOptions.Value.StallThreshold,
                transactionWatcherOptions.Value.ScanInterval),
            transactionWatcherOptions.Value.Enabled,
            stoppingToken);

        logger.LogInformation("EternalOrchestrationStarter completed startup");

        // Future eternal orchestrations go here:
        // await EnsureOrchestrationAsync<ReindexOrchestration>(...);
        // await EnsureOrchestrationAsync<AuditCleanupOrchestration>(...);
    }

    /// <summary>
    /// Statuses that indicate an orchestration is already active and should not be recreated.
    /// Used for atomic deduplication when creating orchestration instances.
    /// </summary>
    private static readonly OrchestrationStatus[] ActiveStatuses =
    [
        OrchestrationStatus.Running,
        OrchestrationStatus.Pending,
        OrchestrationStatus.ContinuedAsNew
    ];

    private async Task EnsureOrchestrationAsync<T>(
        string instanceId,
        object input,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var orchestrationName = typeof(T).Name;

        if (!enabled)
        {
            logger.LogInformation("{Orchestration} is disabled", orchestrationName);
            return;
        }

        try
        {
            // Use atomic deduplication: CreateOrchestrationInstanceAsync with dedupeStatuses
            // prevents race conditions between check-and-create operations.
            // If an instance exists in Running/Pending/ContinuedAsNew state, this throws
            // InvalidOperationException instead of creating a duplicate.
            logger.LogDebug("Ensuring orchestration instance for {Orchestration}...", orchestrationName);
            await taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(T),
                instanceId,
                input,
                dedupeStatuses: ActiveStatuses);

            logger.LogInformation(
                "Started eternal orchestration {Orchestration} (InstanceId: {InstanceId})",
                orchestrationName,
                instanceId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Orchestration already running - this is expected and not an error
            logger.LogInformation(
                "{Orchestration} already running (InstanceId: {InstanceId})",
                orchestrationName,
                instanceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start eternal orchestration {Orchestration} (InstanceId: {InstanceId})", orchestrationName, instanceId);
        }
    }
}
