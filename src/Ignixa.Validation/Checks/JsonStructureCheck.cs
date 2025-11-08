// <copyright file="JsonStructureCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that the resource has basic FHIR structure (resourceType exists).
/// Tier 1 (Fast) validator - executes in less than 5ms.
/// </summary>
public class JsonStructureCheck : IValidationCheck
{
    /// <summary>
    /// Validates the structure of a FHIR resource.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        // ISourceNode uses resourceType as metadata (Name property) rather than as a child
        // Check if the node has a ResourceType via IResourceTypeSupplier
        var resourceTypeSupplier = node as IResourceTypeSupplier;
        var resourceType = resourceTypeSupplier?.ResourceType ?? node.Name;

        if (string.IsNullOrEmpty(resourceType) || resourceType == "root")
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "structure-1",
                "Resource must have a 'resourceType' property",
                node.Location ?? node.Name ?? "root"));
        }

        return issues.Any()
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }
}
