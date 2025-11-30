// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Events.Package;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Terminology;
using Medino;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Events;

/// <summary>
/// Handles PackageLoadedEvent by triggering terminology import for CodeSystem/ValueSet/ConceptMap resources.
/// Queries PackageResources table for terminology resources and publishes TerminologyImportTriggeredEvent.
/// </summary>
public class PackageLoadedTerminologyImportHandler : INotificationHandler<PackageLoadedEvent>
{
    private readonly SqlEntityFrameworkRepositoryFactory _repositoryFactory;
    private readonly IMediator _mediator;
    private readonly ILogger<PackageLoadedTerminologyImportHandler> _logger;

    public PackageLoadedTerminologyImportHandler(
        SqlEntityFrameworkRepositoryFactory repositoryFactory,
        IMediator mediator,
        ILogger<PackageLoadedTerminologyImportHandler> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing PackageLoadedEvent for terminology import: {PackageId}@{PackageVersion}",
                notification.PackageId,
                notification.PackageVersion);

            // Get tenant-specific DbContext from factory
            using var context = await _repositoryFactory.GetDbContextAsync(notification.TenantId, cancellationToken);

            // Query PackageResources for terminology resources that need importing
            var terminologyResourceIds = await context.PackageResources
                .Where(pr => pr.PackageId == notification.PackageId)
                .Where(pr => pr.PackageVersion == notification.PackageVersion)
                .Where(pr => pr.IsActive)
                .Where(pr => pr.ResourceType == "CodeSystem" || pr.ResourceType == "ValueSet" || pr.ResourceType == "ConceptMap")
                .Where(pr => pr.TerminologyImportStatus == null ||
                            pr.TerminologyImportStatus == "Pending" ||
                            pr.TerminologyImportStatus == "Failed")
                .Select(pr => pr.PackageResourceId)
                .ToListAsync(cancellationToken);

            if (terminologyResourceIds.Count == 0)
            {
                _logger.LogInformation(
                    "No terminology resources found to import for {PackageId}@{PackageVersion}",
                    notification.PackageId,
                    notification.PackageVersion);
                return;
            }

            _logger.LogInformation(
                "Found {Count} terminology resources to import for {PackageId}@{PackageVersion}",
                terminologyResourceIds.Count,
                notification.PackageId,
                notification.PackageVersion);

            // Publish TerminologyImportTriggeredEvent
            var importEvent = new TerminologyImportTriggeredEvent(
                TenantId: notification.TenantId,
                PackageId: notification.PackageId,
                PackageVersion: notification.PackageVersion,
                PackageResourceIds: terminologyResourceIds);

            await _mediator.PublishAsync(importEvent, cancellationToken);

            _logger.LogInformation(
                "Published TerminologyImportTriggeredEvent for {Count} resources",
                terminologyResourceIds.Count);
        }
        catch (Exception ex)
        {
            // Log error but don't throw - allow package load to succeed even if terminology trigger fails
            _logger.LogError(
                ex,
                "Failed to trigger terminology import for {PackageId}@{PackageVersion}: {Message}",
                notification.PackageId,
                notification.PackageVersion,
                ex.Message);
        }
    }
}
