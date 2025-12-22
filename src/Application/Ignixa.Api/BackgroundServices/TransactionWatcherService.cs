// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Extensions.Options;
using Ignixa.Api.Configuration;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Orchestrations;

namespace Ignixa.Api.BackgroundServices;

/// <summary>
/// Background service that starts the TransactionWatcher DurableTask orchestration.
/// The orchestration monitors for stalled transactions and automatically commits them
/// using durable timers based on configured ScanInterval.
///
/// This service:
/// 1. Creates or resumes the TransactionWatcher orchestration on startup
/// 2. Uses a singleton orchestration instance ID for the entire cluster
/// 3. The orchestration handles all timing via durable timers (survives restarts)
/// </summary>
public sealed class TransactionWatcherService : BackgroundService
{
    /// <summary>
    /// Singleton instance ID for the TransactionWatcher orchestration.
    /// Using a fixed ID ensures only one instance runs across the cluster.
    /// </summary>
    private const string OrchestrationInstanceId = "TransactionWatcher-Singleton";

    private readonly TaskHubClient _taskHubClient;
    private readonly TransactionWatcherOptions _options;
    private readonly ILogger<TransactionWatcherService> _logger;

    public TransactionWatcherService(
        TaskHubClient taskHubClient,
        IOptions<TransactionWatcherOptions> options,
        ILogger<TransactionWatcherService> logger)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for the DurableTask worker to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        if (!_options.Enabled)
        {
            _logger.LogInformation("Transaction watcher is disabled (TransactionWatcher:Enabled = false)");
            return;
        }

        _logger.LogInformation(
            "Transaction watcher starting (ScanInterval: {ScanInterval}, StallThreshold: {StallThreshold})",
            _options.ScanInterval,
            _options.StallThreshold);

        try
        {
            // Check if orchestration is already running
            var existingState = await _taskHubClient.GetOrchestrationStateAsync(OrchestrationInstanceId);

            if (existingState?.OrchestrationStatus is OrchestrationStatus.Running or OrchestrationStatus.Pending)
            {
                _logger.LogInformation(
                    "TransactionWatcher orchestration already running (Instance: {InstanceId}, Status: {Status})",
                    OrchestrationInstanceId,
                    existingState.OrchestrationStatus);
                return;
            }

            // Create input for the orchestration
            var input = new TransactionWatcherOrchestrationInput(
                ScanInterval: _options.ScanInterval,
                StallThreshold: _options.StallThreshold,
                Enabled: _options.Enabled);

            // Start the orchestration
            var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(TransactionWatcherOrchestration),
                OrchestrationInstanceId,
                input);

            _logger.LogInformation(
                "TransactionWatcher orchestration started (Instance: {InstanceId})",
                instance.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start TransactionWatcher orchestration");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transaction watcher service stopping");

        // Note: We don't terminate the orchestration on shutdown because:
        // 1. It's durable and will resume when the service restarts
        // 2. It uses durable timers that survive restarts
        // 3. Terminating would lose all state

        await base.StopAsync(cancellationToken);
    }
}

