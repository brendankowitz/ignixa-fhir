// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Checks;

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
    /// Composes multiple <see cref="ValidationSchema"/> instances into a single schema.
    /// Universal, spec, and profile check lists are concatenated in input order, preserving
    /// the tier-aware execution semantics of <see cref="Validate"/>.
    /// <para>
    /// The first schema in <paramref name="schemas"/> donates its <c>CanonicalUrl</c> and
    /// <c>ResourceType</c> to the composed result; this matches the convention of treating
    /// the resource's base StructureDefinition as the primary schema with profiles layered
    /// on top.
    /// </para>
    /// </summary>
    /// <param name="schemas">Schemas to compose. Must not be empty.</param>
    /// <returns>A new schema whose check lists are the union of the inputs.</returns>
    public static ValidationSchema Compose(IReadOnlyList<ValidationSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (schemas.Count == 0)
        {
            throw new ArgumentException("Cannot compose an empty list of schemas.", nameof(schemas));
        }

        if (schemas.Count == 1)
        {
            return schemas[0];
        }

        var primary = schemas[0];

        // Universal checks: deduplicate stateless singleton checks (JsonStructureCheck,
        // NarrativeCheck, ResourceTypeValidationCheck) that are per-resource and must run
        // exactly once. Parameterized per-element checks (CardinalityCheck, TypeCheck) are
        // concatenated normally — each carries distinct element metadata.
        var seenSingletonTypes = new HashSet<Type>
        {
            typeof(JsonStructureCheck),
            typeof(NarrativeCheck),
            typeof(ResourceTypeValidationCheck),
        };
        var seenAdded = new HashSet<Type>();
        var universal = new List<IValidationCheck>();
        foreach (var s in schemas)
        {
            foreach (var c in s._universalChecks)
            {
                if (seenSingletonTypes.Contains(c.GetType()))
                {
                    if (seenAdded.Add(c.GetType()))
                    {
                        universal.Add(c);
                    }
                }
                else
                {
                    universal.Add(c);
                }
            }
        }

        // Spec checks: concatenate normally (cardinality, binding, choice, reference checks are
        // per-element and must all run), but deduplicate UnknownPropertyCheck by type (the first
        // schema's list covers all known property names for the resource).
        var hasUnknownPropertyCheck = false;
        var spec = new List<IValidationCheck>();
        foreach (var s in schemas)
        {
            foreach (var c in s._specChecks)
            {
                if (c is UnknownPropertyCheck)
                {
                    if (!hasUnknownPropertyCheck)
                    {
                        hasUnknownPropertyCheck = true;
                        spec.Add(c);
                    }
                }
                else
                {
                    spec.Add(c);
                }
            }
        }

        // Profile checks: no dedup — all profile-tier checks (invariants, slicing) are meaningful
        var profile = new List<IValidationCheck>();
        foreach (var s in schemas)
        {
            profile.AddRange(s._profileChecks);
        }

        return new ValidationSchema(
            canonicalUrl: primary.CanonicalUrl,
            resourceType: primary.ResourceType,
            universalChecks: universal,
            specChecks: spec,
            profileChecks: profile);
    }

    /// <summary>
    /// Validates an element using depth-appropriate checks.
    /// Depth.Minimal: Run universal checks only.
    /// Depth.Spec: Run universal + spec checks.
    /// Depth.Full: Run universal + spec + profile checks.
    /// Depth.Compatibility: Run universal + spec checks (same as Spec, no profile checks).
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings (including depth).</param>
    /// <param name="state">Current validation state. Optional - a default state will be used if not provided.</param>
    /// <returns>Combined validation result from all checks.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState? state = null)
    {
        state ??= new ValidationState();
        var results = new List<ValidationResult>();

        // Universal checks always run (all depths)
        foreach (var check in _universalChecks)
        {
            results.Add(check.Validate(element, settings, state));
        }

        // Spec checks: run for Spec, Full, and Compatibility depths
        if (settings.Depth >= ValidationDepth.Spec)
        {
            foreach (var check in _specChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }

        // Profile checks: run ONLY for Full depth (not Compatibility)
        if (settings.Depth == ValidationDepth.Full)
        {
            foreach (var check in _profileChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }

        return ValidationResult.Combine(results);
    }
}
