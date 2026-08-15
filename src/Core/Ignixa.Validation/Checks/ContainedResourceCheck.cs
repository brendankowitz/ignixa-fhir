// <copyright file="ContainedResourceCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates contained resources by resolving each contained resource's schema
/// based on its resourceType and validating against that schema.
/// Tier 2 (Spec) validator - ensures contained resources conform to their own StructureDefinition.
/// </summary>
/// <remarks>
/// FHIR contained resources are full resources embedded within a parent resource.
/// Each contained resource must be validated against its own StructureDefinition,
/// not the parent resource's schema.
///
/// Example:
/// <code>
/// {
///   "resourceType": "Observation",
///   "contained": [
///     {
///       "resourceType": "Patient",  // Must validate against Patient schema
///       "id": "patient-1",
///       "identifier": [...]         // Valid for Patient, invalid for Observation
///     }
///   ]
/// }
/// </code>
/// </remarks>
public class ContainedResourceCheck(IValidationSchemaResolver schemaResolver) : IValidationCheck
{
    private readonly IValidationSchemaResolver _schemaResolver = schemaResolver ?? throw new ArgumentNullException(nameof(schemaResolver));

    /// <summary>
    /// Validates all contained resources within the element.
    /// </summary>
    /// <param name="element">The element containing the "contained" array.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result with issues from all contained resource validations.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // validateContains=IGNORE: contained resources are not validated against their own schema.
        if (!settings.ValidateContainedResources)
        {
            return ValidationResult.Success();
        }

        var containedElements = element.Children("contained").ToList();

        if (containedElements.Count == 0)
        {
            return ValidationResult.Success();
        }

        if (!state.TryDescend(out var descended))
        {
            return new ValidationResult(
                isValid: true,
                issues: new[] { ValidationIssue.NestingLimitExceeded(state.Location.InstancePath, "contained") });
        }

        // Issues propagate regardless of the contained result's validity: gating on !IsValid would
        // drop every non-failing Warning raised inside the contained resource, including the
        // engine-refusal warnings that exist to be reported without failing the resource.
        var issues = new List<ValidationIssue>();
        var isValid = true;

        for (int i = 0; i < containedElements.Count; i++)
        {
            var containedElement = containedElements[i];
            var containedPath = $"contained[{i}]";

            // Extract resourceType from contained resource
            var resourceType = containedElement.InstanceType;

            if (string.IsNullOrEmpty(resourceType) || resourceType == "Resource")
            {
                // Try to get resourceType from children (if InstanceType not properly set)
#pragma warning disable CA1826 // Children() returns IEnumerable, not an indexable collection
                var resourceTypeChild = containedElement.Children("resourceType").FirstOrDefault();
#pragma warning restore CA1826
                if (resourceTypeChild is not null)
                {
                    resourceType = resourceTypeChild.Value?.ToString();
                }
            }

            // Validate resourceType presence
            if (string.IsNullOrEmpty(resourceType))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "contained-missing-resourcetype",
                    $"{element.Location}.{containedPath}",
                    "Contained resource must have a 'resourceType' property"));
                continue;
            }

            // Resolve schema for the contained resource type (resolver accepts resource type name directly)
            var containedSchema = _schemaResolver.GetSchema(resourceType);

            if (containedSchema is null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "contained-invalid-resourcetype",
                    $"{element.Location}.{containedPath}",
                    $"Unknown resource type '{resourceType}' in contained resource"));
                continue;
            }

            // Validate contained resource against its own schema. Re-scope so %resource points at
            // the contained resource and %rootResource at the containing parent (FHIR dom-* rule).
            var containedState = descended.WithLocation(containedPath).EnterContainedResource(containedElement);
            var containedResult = containedSchema.Validate(containedElement, settings, containedState);

            isValid &= containedResult.IsValid;

            // Adjust paths to be relative to parent resource
            var parentPrefix = $"{element.Location}.{containedPath}";
            foreach (var issue in containedResult.Issues)
            {
                // The nested validation returns paths relative to the contained resource (e.g., "Patient.unknownField")
                // We need to replace the resource type prefix with the contained path
                var adjustedPath = issue.Path;

                // If path starts with the contained resource type (e.g., "Patient."), replace with contained path
                if (adjustedPath.StartsWith($"{resourceType}.", StringComparison.Ordinal))
                {
                    adjustedPath = $"{parentPrefix}.{adjustedPath.Substring(resourceType.Length + 1)}";
                }
                else if (adjustedPath == resourceType)
                {
                    // Path is just the resource type (e.g., "Patient")
                    adjustedPath = parentPrefix;
                }
                else if (!adjustedPath.StartsWith(parentPrefix, StringComparison.Ordinal))
                {
                    // Path doesn't start with parent prefix, so prepend it
                    adjustedPath = $"{parentPrefix}.{adjustedPath}";
                }

                issues.Add(issue with { Path = adjustedPath });
            }
        }

        // Validity is the conjunction of the contained results' own verdicts and the severity of the
        // issues raised directly here (missing/unknown resourceType), which never pass through a
        // nested result to contribute their invalidity.
        var hasErrors = issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal);
        return new ValidationResult(isValid && !hasErrors, issues);
    }
}
