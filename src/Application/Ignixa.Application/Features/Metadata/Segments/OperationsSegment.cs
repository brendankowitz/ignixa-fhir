// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// CapabilityStatement segment that exposes operations from registered IPackageFeature implementations.
/// Only adds operations that are declared by an IPackageFeature (conditional exposure).
/// Dynamically loads OperationDefinitions from the PackageResource table.
/// </summary>
public class OperationsSegment : ICapabilitySegment
{
    private readonly IEnumerable<IPackageFeature> _features;
    private readonly IPackageResourceRepository _packageResourceRepository;
    private readonly ILogger<OperationsSegment> _logger;

    public string SegmentKey => "operations";

    public int Priority => 35; // After static/resource segments, before tenant-specific

    public OperationsSegment(
        IEnumerable<IPackageFeature> features,
        IPackageResourceRepository packageResourceRepository,
        ILogger<OperationsSegment> logger)
    {
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _packageResourceRepository = packageResourceRepository ?? throw new ArgumentNullException(nameof(packageResourceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying operations segment for FHIR version {FhirVersion}", context.FhirVersion);

        var fhirVersionString = GetFhirVersionString(context.FhirVersion);

        // Collect all operations from registered features
        var systemOperations = new HashSet<string>();
        var resourceOperations = new Dictionary<string, HashSet<string>>();

        foreach (var feature in _features)
        {
            // Skip if feature doesn't support this FHIR version
            if (feature.SupportedFhirVersions != null &&
                !feature.SupportedFhirVersions.Contains(fhirVersionString, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Feature {PackageId} does not support FHIR version {FhirVersion}",
                    feature.PackageId,
                    fhirVersionString);
                continue;
            }

            // Collect system operations
            foreach (var op in feature.SystemOperations)
            {
                systemOperations.Add(op);
                _logger.LogDebug("Added system operation: {OperationName} from {PackageId}", op, feature.PackageId);
            }

            // Collect resource-level operations
            foreach (var (resourceType, operations) in feature.ResourceOperations)
            {
                if (!resourceOperations.ContainsKey(resourceType))
                {
                    resourceOperations[resourceType] = new HashSet<string>();
                }

                foreach (var op in operations)
                {
                    resourceOperations[resourceType].Add(op);
                    _logger.LogDebug(
                        "Added operation {OperationName} for resource {ResourceType} from {PackageId}",
                        op,
                        resourceType,
                        feature.PackageId);
                }
            }
        }

        if (systemOperations.Count == 0 && resourceOperations.Count == 0)
        {
            _logger.LogDebug("No operations to add (no registered features)");
            return;
        }

        // Load OperationDefinitions from PackageResource table
        var allOperationNames = systemOperations
            .Concat(resourceOperations.Values.SelectMany(x => x))
            .Distinct()
            .ToList();

        var operationDefs = await _packageResourceRepository.GetOperationDefinitionsAsync(
            allOperationNames,
            fhirVersionString,
            cancellationToken);

        var opDefsByName = operationDefs
            .GroupBy(x => x.ResourceId)
            .ToDictionary(g => g.Key, g => g.First()); // Take first (newest due to ordering)

        // Add system-level operations
        foreach (var opName in systemOperations)
        {
            if (opDefsByName.TryGetValue(opName, out var opDef))
            {
                statement.AddSystemOperation(opName, opDef.Canonical, GetDocumentation(opDef));
                _logger.LogDebug("Added system operation to capability: {OperationName}", opName);
            }
            else
            {
                _logger.LogWarning("Operation definition not found for system operation: {OperationName}", opName);
            }
        }

        // Add resource-level operations
        foreach (var (resourceType, operations) in resourceOperations)
        {
            foreach (var opName in operations)
            {
                if (opDefsByName.TryGetValue(opName, out var opDef))
                {
                    statement.AddResourceOperation(resourceType, opName, opDef.Canonical, GetDocumentation(opDef));
                    _logger.LogDebug("Added resource operation to capability: {ResourceType}/${OperationName}", resourceType, opName);
                }
                else
                {
                    _logger.LogWarning(
                        "Operation definition not found for resource operation: {ResourceType}/{OperationName}",
                        resourceType,
                        opName);
                }
            }
        }
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        var fhirVersionString = GetFhirVersionString(context.FhirVersion);

        // Hash all feature declarations so cache invalidates if features change
        var featureDeclarations = new StringBuilder();

        foreach (var feature in _features.OrderBy(f => f.PackageId))
        {
            // Skip features that don't support this FHIR version
            if (feature.SupportedFhirVersions != null &&
                !feature.SupportedFhirVersions.Contains(fhirVersionString, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            featureDeclarations.Append(feature.PackageId);
            featureDeclarations.Append(':');

            foreach (var op in feature.SystemOperations.OrderBy(x => x))
            {
                featureDeclarations.Append('S');
                featureDeclarations.Append(op);
                featureDeclarations.Append('|');
            }

            foreach (var (resourceType, operations) in feature.ResourceOperations.OrderBy(x => x.Key))
            {
                foreach (var op in operations.OrderBy(x => x))
                {
                    featureDeclarations.Append('R');
                    featureDeclarations.Append(resourceType);
                    featureDeclarations.Append(':');
                    featureDeclarations.Append(op);
                    featureDeclarations.Append('|');
                }
            }

            featureDeclarations.Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(featureDeclarations.ToString()));
        var hashString = BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        return ValueTask.FromResult(hashString);
    }

    private static string GetFhirVersionString(FhirVersion fhirVersion)
    {
        return fhirVersion switch
        {
            FhirVersion.R4 => "R4",
            FhirVersion.R4B => "R4B",
            FhirVersion.R5 => "R5",
            FhirVersion.Stu3 => "Stu3",
            _ => throw new ArgumentOutOfRangeException(nameof(fhirVersion), fhirVersion, "Unsupported FHIR version"),
        };
    }

    private static string? GetDocumentation(Domain.Models.PackageResource operationDef)
    {
        // For now, return null. In the future, this could parse the OperationDefinition
        // resource JSON and extract the description field.
        return null;
    }
}
