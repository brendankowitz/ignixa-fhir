// <copyright file="ValidationSchema.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.SourceNodeSerialization.ElementModel;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Represents a compiled validation schema for a FHIR resource type or profile.
/// Contains pre-built validation checks derived from StructureDefinition metadata.
/// Immutable after construction for thread-safe caching.
/// </summary>
public sealed class ValidationSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationSchema"/> class.
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the StructureDefinition.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="checks">The validation checks to execute for this schema.</param>
    public ValidationSchema(string canonicalUrl, string resourceType, IReadOnlyList<IValidationCheck> checks)
    {
        CanonicalUrl = canonicalUrl ?? throw new ArgumentNullException(nameof(canonicalUrl));
        ResourceType = resourceType ?? throw new ArgumentNullException(nameof(resourceType));
        Checks = checks ?? throw new ArgumentNullException(nameof(checks));
    }

    /// <summary>
    /// Gets the canonical URL of this schema (e.g., "http://hl7.org/fhir/StructureDefinition/Patient").
    /// </summary>
    public string CanonicalUrl { get; }

    /// <summary>
    /// Gets the FHIR resource type (e.g., "Patient", "Observation").
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// Gets the validation checks to execute for this schema.
    /// Built from StructureDefinition metadata (required elements, cardinality, types, etc.).
    /// </summary>
    public IReadOnlyList<IValidationCheck> Checks { get; }

    /// <summary>
    /// Validates a source node using all checks in this schema.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>Combined validation result from all checks.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var results = new List<ValidationResult>();

        foreach (var check in Checks)
        {
            results.Add(check.Validate(node, settings, state));
        }

        return ValidationResult.Combine(results);
    }
}
