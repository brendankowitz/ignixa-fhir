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
/// Tier-aware: Organizes checks into Fast (universal), Spec (schema-driven), and Profile (advanced) tiers.
/// </summary>
public sealed class ValidationSchema
{
    private readonly IReadOnlyList<IValidationCheck> _universalChecks;  // Tier.Fast
    private readonly IReadOnlyList<IValidationCheck> _specChecks;       // Tier.Spec
    private readonly IReadOnlyList<IValidationCheck> _profileChecks;    // Tier.Profile

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationSchema"/> class.
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the StructureDefinition.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="universalChecks">Universal checks (Fast tier) - JsonStructure, IdFormat, Narrative.</param>
    /// <param name="specChecks">Spec checks (Spec tier) - Cardinality, Type, Required, etc.</param>
    /// <param name="profileChecks">Profile checks (Profile tier) - Slicing, advanced terminology, etc.</param>
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
    /// Validates a source node using tier-appropriate checks.
    /// Tier.None: Skip validation.
    /// Tier.Fast: Run universal checks only.
    /// Tier.Spec: Run universal + spec checks.
    /// Tier.Profile: Run universal + spec + profile checks.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings (including tier).</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>Combined validation result from all checks.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        // Tier.None: Skip validation
        if (settings.Tier == ValidationTier.None)
        {
            return ValidationResult.Success();
        }

        var results = new List<ValidationResult>();

        // Tier.Fast+: Run universal checks
        if (settings.Tier >= ValidationTier.Fast)
        {
            foreach (var check in _universalChecks)
            {
                results.Add(check.Validate(node, settings, state));
            }
        }

        // Tier.Spec+: Run spec checks
        if (settings.Tier >= ValidationTier.Spec)
        {
            foreach (var check in _specChecks)
            {
                results.Add(check.Validate(node, settings, state));
            }
        }

        // Tier.Profile: Run profile checks
        if (settings.Tier >= ValidationTier.Profile)
        {
            foreach (var check in _profileChecks)
            {
                results.Add(check.Validate(node, settings, state));
            }
        }

        return ValidationResult.Combine(results);
    }
}
