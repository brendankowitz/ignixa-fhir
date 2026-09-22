// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
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
    private readonly IPackageResourceRepository _packageResources;
    private readonly ILogger<TerminologyImportTriggeredHandler> _logger;

    public TerminologyImportTriggeredHandler(
        TaskHubClient taskHubClient,
        ILogger<TerminologyImportTriggeredHandler> logger,
        IPackageResourceRepository packageResources)
    {
        _taskHubClient = taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _packageResources = packageResources ?? throw new ArgumentNullException(nameof(packageResources));
    }

    public async Task HandleAsync(TerminologyImportTriggeredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ids = notification.PackageResourceIds.ToHashSet();
            if (ids.Count != notification.PackageResourceIds.Count)
            {
                throw new InvalidOperationException("Terminology import requests require distinct package resource IDs.");
            }
            var resources = await _packageResources.ListPackageResourcesAsync(
                notification.PackageId, notification.PackageVersion, cancellationToken: cancellationToken);
            var selected = resources.Where(resource => ids.Contains(resource.PackageResourceId)).ToArray();
            if (selected.Length != ids.Count)
            {
                throw new InvalidOperationException("Terminology import resources are missing from the active package version.");
            }
            var plan = TerminologyImportPlanner.Create(selected);

            // Create unique instance ID for this package
            // Format: terminology-import-{tenantId}-{packageId}-{packageVersion}
            // This ensures idempotency: re-triggering for the same package reuses the same orchestration
            var instanceId = $"terminology-import-{notification.TenantId}-{notification.PackageId}-{notification.PackageVersion}";

            var input = new TerminologyImportOrchestrationInput(
                TenantId: notification.TenantId,
                PackageId: notification.PackageId,
                PackageVersion: notification.PackageVersion,
                PackageResourceIds: notification.PackageResourceIds,
                DependencyPlan: plan);

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start TerminologyImportOrchestration for package {PackageId}@{PackageVersion}: {Message}",
                notification.PackageId,
                notification.PackageVersion,
                ex.Message);

            // Package-load and bootstrap callers own the recoverable boundary. They must see creation
            // failures rather than logging a published job that was never submitted.
            throw;
        }
    }
}
