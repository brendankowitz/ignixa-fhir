// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Ignixa.Application.Events.Terminology;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Terminology.EventHandlers;

/// <summary>
/// Handles TerminologyImportTriggeredEvent by starting a DurableTask orchestration.
/// Creates a unique orchestration instance for each package to process terminology resources in parallel.
/// </summary>
public class TerminologyImportTriggeredHandler : INotificationHandler<TerminologyImportTriggeredEvent>
{
    private readonly TaskHubClient _taskHubClient;
    private readonly ILogger<TerminologyImportTriggeredHandler> _logger;

    public TerminologyImportTriggeredHandler(
        TaskHubClient taskHubClient,
        ILogger<TerminologyImportTriggeredHandler> logger)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(TerminologyImportTriggeredEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Create unique instance ID for this package
            // Format: terminology-import-{tenantId}-{packageId}-{packageVersion}
            // This ensures idempotency: re-triggering for the same package reuses the same orchestration
            var instanceId = $"terminology-import-{notification.TenantId}-{notification.PackageId}-{notification.PackageVersion}";

            var input = new TerminologyImportOrchestrationInput(
                TenantId: notification.TenantId,
                PackageId: notification.PackageId,
                PackageVersion: notification.PackageVersion,
                PackageResourceIds: notification.PackageResourceIds);

            _logger.LogInformation(
                "Starting TerminologyImportOrchestration {InstanceId} for {Count} resources from package {PackageId}@{PackageVersion}",
                instanceId,
                notification.PackageResourceIds.Count,
                notification.PackageId,
                notification.PackageVersion);

            var instance = await _taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(TerminologyImportOrchestration),
                instanceId,
                input);

            _logger.LogInformation(
                "Successfully created orchestration instance {InstanceId}",
                instance.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start TerminologyImportOrchestration for package {PackageId}@{PackageVersion}: {Message}",
                notification.PackageId,
                notification.PackageVersion,
                ex.Message);

            // Don't throw - allow event handling to complete even if orchestration fails to start
            // The package load should succeed, and terminology import can be retried manually
        }
    }
}
