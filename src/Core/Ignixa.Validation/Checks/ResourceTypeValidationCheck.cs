// <copyright file="ResourceTypeValidationCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that a resource's resourceType value is a valid FHIR resource type.
/// Tier 1 (Fast) validator - executes in less than 1ms.
/// Only applies to FHIR resources, not BackboneElements or complex datatypes.
/// </summary>
public class ResourceTypeValidationCheck : IValidationCheck, ISingletonCheck
{
    private readonly IReadOnlySet<string> _validResourceTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceTypeValidationCheck"/> class.
    /// </summary>
    /// <param name="validResourceTypes">The set of valid FHIR resource type names.</param>
    /// <exception cref="ArgumentNullException">Thrown if validResourceTypes is null.</exception>
    public ResourceTypeValidationCheck(IReadOnlySet<string> validResourceTypes)
    {
        _validResourceTypes = validResourceTypes ?? throw new ArgumentNullException(nameof(validResourceTypes));
    }

    /// <summary>
    /// Validates that the resource has a valid FHIR resource type.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        // IElement uses InstanceType to get the resource type
        var resourceType = element.InstanceType;

        if (string.IsNullOrEmpty(resourceType) || resourceType == "root")
        {
            // Empty resourceType is handled by JsonStructureCheck
            return ValidationResult.Success();
        }

        // Validate that resourceType is in the set of known FHIR resource types
        if (!_validResourceTypes.Contains(resourceType, StringComparer.Ordinal))
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "resourcetype-1",
                $"Resource type '{resourceType}' is not a valid FHIR resource type",
                element.Location ?? element.InstanceType ?? "root"));
        }

        return issues.Any()
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }
}
