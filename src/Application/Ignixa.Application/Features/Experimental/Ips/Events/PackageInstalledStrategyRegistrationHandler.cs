// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Experimental.Ips.Api;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Ips.Events;

/// <summary>
/// Registers patient summary strategies when packages are installed.
/// Scans installed package for Composition StructureDefinitions and creates strategies automatically.
/// </summary>
public class PackageInstalledStrategyRegistrationHandler(
    IStructureDefinitionStrategyFactory strategyFactory,
    IIpsGenerationStrategyRegistry strategyRegistry,
    IPackageResourceRepository packageResourceRepository,
    ILogger<PackageInstalledStrategyRegistrationHandler> logger
) : INotificationHandler<PackageLoadedEvent>
{
    public async Task HandleAsync(
        PackageLoadedEvent notification,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Scanning package {PackageId}#{Version} for patient summary profiles",
            notification.PackageId,
            notification.PackageVersion);

        try
        {
            var structureDefinitions = await packageResourceRepository.ListPackageResourcesAsync(
                notification.PackageId,
                notification.PackageVersion,
                resourceType: "StructureDefinition",
                cancellationToken);

            if (structureDefinitions.Count == 0)
            {
                logger.LogDebug(
                    "No StructureDefinitions found in package {PackageId}#{Version}",
                    notification.PackageId,
                    notification.PackageVersion);
                return;
            }

            logger.LogDebug(
                "Found {Count} StructureDefinitions in package {PackageId}#{Version}",
                structureDefinitions.Count,
                notification.PackageId,
                notification.PackageVersion);

            var registeredCount = 0;

            foreach (var packageResource in structureDefinitions)
            {
                try
                {
                    var resourceNode = JsonSerializer.Deserialize<ResourceJsonNode>(packageResource.ResourceJson);

                    if (resourceNode is null)
                    {
                        logger.LogWarning(
                            "Failed to deserialize StructureDefinition {Canonical} from package {PackageId}#{Version}",
                            packageResource.Canonical,
                            notification.PackageId,
                            notification.PackageVersion);
                        continue;
                    }

                    var strategy = strategyFactory.CreateFromStructureDefinition(resourceNode, cancellationToken);

                    if (strategy is not null)
                    {
                        strategyRegistry.RegisterStrategy(strategy.BundleProfile, strategy);
                        registeredCount++;

                        logger.LogInformation(
                            "Registered patient summary strategy for profile {Profile}",
                            strategy.BundleProfile);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to process StructureDefinition {Canonical} from package {PackageId}#{Version}: {Message}",
                        packageResource.Canonical,
                        notification.PackageId,
                        notification.PackageVersion,
                        ex.Message);
                }
            }

            if (registeredCount > 0)
            {
                logger.LogInformation(
                    "Registered {Count} patient summary strategies from package {PackageId}#{Version}",
                    registeredCount,
                    notification.PackageId,
                    notification.PackageVersion);
            }
            else
            {
                logger.LogDebug(
                    "No patient summary profiles found in package {PackageId}#{Version}",
                    notification.PackageId,
                    notification.PackageVersion);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to scan package {PackageId}#{Version} for patient summary profiles: {Message}",
                notification.PackageId,
                notification.PackageVersion,
                ex.Message);
        }
    }
}
