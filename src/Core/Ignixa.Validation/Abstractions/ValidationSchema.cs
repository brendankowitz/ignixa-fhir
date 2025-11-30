// <copyright file="ValidationSchema.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Represents a compiled validation schema for a FHIR resource type or profile.
/// Contains pre-built validation checks derived from StructureDefinition metadata.
/// Immutable after construction for thread-safe caching.
/// Depth-aware: Organizes checks into Minimal (universal), Spec (schema-driven), and Full (advanced) tiers.
/// </summary>
public sealed class ValidationSchema
{
    private readonly IReadOnlyList<IValidationCheck> _universalChecks;  // Depth.Minimal
    private readonly IReadOnlyList<IValidationCheck> _specChecks;       // Depth.Spec
    private readonly IReadOnlyList<IValidationCheck> _profileChecks;    // Depth.Full

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationSchema"/> class.
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the StructureDefinition.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="universalChecks">Universal checks (Minimal depth) - JsonStructure, IdFormat, Narrative.</param>
    /// <param name="specChecks">Spec checks (Spec depth) - Cardinality, Type, Required, etc.</param>
    /// <param name="profileChecks">Profile checks (Full depth) - Slicing, advanced terminology, etc.</param>
    public ValidationSchema(
        string canonicalUrl,
        string resourceType,
        IReadOnlyList<IValidationCheck> universalChecks,
        IReadOnlyList<IValidationCheck> specChecks,
        IReadOnlyList<IValidationCheck> profileChecks)
    {
        CanonicalUrl = canonicalUrl ?? throw new ArgumentNullException(nameof(canonicalUrl));
        ResourceType = resourceType ?? throw new ArgumentNullException(nameof(resourceType));
        _universalChecks = universalChecks ?? throw new ArgumentNullException(nameof(universalChecks));
        _specChecks = specChecks ?? throw new ArgumentNullException(nameof(specChecks));
        _profileChecks = profileChecks ?? throw new ArgumentNullException(nameof(profileChecks));
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
    /// Gets all validation checks for backward compatibility with tests.
    /// Returns combined list of universal + spec + profile checks.
    /// </summary>
    public IReadOnlyList<IValidationCheck> Checks =>
        _universalChecks.Concat(_specChecks).Concat(_profileChecks).ToList();

    /// <summary>
    /// Validates an element using depth-appropriate checks.
    /// Depth.Minimal: Run universal checks only.
    /// Depth.Spec: Run universal + spec checks.
    /// Depth.Full: Run universal + spec + profile checks.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings (including depth).</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>Combined validation result from all checks.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var results = new List<ValidationResult>();

        // Depth.Minimal+: Run universal checks
        if (settings.Depth >= ValidationDepth.Minimal)
        {
            foreach (var check in _universalChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }

        // Depth.Spec+: Run spec checks
        if (settings.Depth >= ValidationDepth.Spec)
        {
            foreach (var check in _specChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }

        // Depth.Full: Run profile checks
        if (settings.Depth >= ValidationDepth.Full)
        {
            foreach (var check in _profileChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }

        return ValidationResult.Combine(results);
    }
}
