// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

        // Singleton checks (marked with ISingletonCheck) are per-resource and must run at most
        // once: dedup by concrete type, first occurrence wins. Parameterized per-element checks
        // (cardinality, type, binding) are not marked and are concatenated — each carries distinct
        // element metadata. The marker keeps this contract with the check, not a hardcoded type list.
        var universal = ConcatDeduplicatingSingletons(schemas, static s => s._universalChecks);
        var spec = ConcatDeduplicatingSingletons(schemas, static s => s._specChecks);

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

    private static List<IValidationCheck> ConcatDeduplicatingSingletons(
        IReadOnlyList<ValidationSchema> schemas,
        Func<ValidationSchema, IReadOnlyList<IValidationCheck>> tierSelector)
    {
        var seenSingletonTypes = new HashSet<Type>();
        var merged = new List<IValidationCheck>();
        foreach (var schema in schemas)
        {
            foreach (var check in tierSelector(schema))
            {
                if (check is ISingletonCheck)
                {
                    if (seenSingletonTypes.Add(check.GetType()))
                    {
                        merged.Add(check);
                    }
                }
                else
                {
                    merged.Add(check);
                }
            }
        }
        return merged;
    }

    /// <summary>
    /// Validates an element using depth-appropriate checks.
    /// Depth.Minimal: Run universal checks only.
    /// Depth.Spec: Run universal + spec checks.
    /// Depth.Full: Run universal + spec + profile checks.
    /// Depth.Compatibility: Run universal + spec checks, plus the subset of profile checks marked
    /// <see cref="ICompatibilityConformanceCheck"/> (all other profile checks stay Full-only).
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings (including depth).</param>
    /// <returns>Combined validation result from all checks.</returns>
    /// <remarks>
    /// Rooting the state at <paramref name="element"/> is correct by construction rather than a guess: a
    /// <see cref="ValidationSchema"/> is built per StructureDefinition and carries the
    /// <see cref="ResourceType"/> it validates, so <paramref name="element"/> is by contract the resource this
    /// schema applies to. That is the difference from <c>FhirPathEvaluator.Evaluate</c>, whose input is any
    /// node and which therefore must not infer a resource.
    /// </remarks>
    public ValidationResult Validate(IElement element, ValidationSettings settings)
        => Validate(element, settings, ValidationState.ForRoot(element));

    /// <summary>
    /// Validates an element as part of an enclosing validation, continuing that validation's state.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings (including depth).</param>
    /// <param name="state">State from the enclosing validation.</param>
    /// <returns>Combined validation result from all checks.</returns>
    /// <remarks>
    /// <para>
    /// Internal because the correct scope discipline depends on what is being descended into, and only the
    /// checks know which applies. <c>ContainedResourceCheck</c> re-roots — <c>%resource</c> moves to the
    /// contained resource while <c>%rootResource</c> stays on the container. <c>NestedComplexTypeCheck</c> and
    /// <c>ChoiceVariantNestedCheck</c> deliberately do not re-root, because a nested datatype is not a
    /// resource and <c>%resource</c> must keep pointing at the one that encloses it. So there is no single
    /// correspondence between <paramref name="element"/> and <paramref name="state"/> that could be asserted
    /// here, and an external caller has no way to know which discipline their case wants.
    /// </para>
    /// <para>
    /// The failure mode this closes is mis-rooting, which <see cref="ValidationState.ForRoot"/> alone does not
    /// prevent: a caller who passes a state rooted at a different element than the one being validated gets
    /// <c>%resource</c> silently bound to the wrong resource, and only at <see cref="ValidationDepth.Full"/>
    /// where invariants run. What actually breaks is not every invariant — two equal-but-not-identical
    /// wrappers over the same underlying resource evaluate every <em>value-based</em> constraint identically.
    /// It is specifically the reference-identity root exemption in <c>FhirPathInvariantCheck</c>
    /// (<c>ReferenceEquals(element, state.Scope.Resource)</c>, which excuses <c>ele-1</c> at the resource
    /// root): a state rooted at a different instance than the one being validated makes that comparison
    /// false, and <c>ele-1</c> fires spuriously on the root. Two calls to the <c>ISourceNavigator.ToElement()</c>
    /// extension are enough to trigger it, since each returns a fresh <c>SchemaAwareElement</c> instance; the
    /// caching <c>ResourceJsonNode.ToElement(ISchema)</c> does not reproduce it, since it returns the same
    /// cached instance for the same schema. External callers use the two-argument overload, which cannot be
    /// mis-rooted.
    /// </para>
    /// </remarks>
    internal ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

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

        // Profile checks: run for Full depth. Compatibility depth runs only the checks marked
        // ICompatibilityConformanceCheck (see that interface for why the boundary is drawn there).
        if (settings.Depth == ValidationDepth.Full)
        {
            foreach (var check in _profileChecks)
            {
                results.Add(check.Validate(element, settings, state));
            }
        }
        else if (settings.Depth == ValidationDepth.Compatibility)
        {
            foreach (var check in _profileChecks)
            {
                if (check is ICompatibilityConformanceCheck)
                {
                    results.Add(check.Validate(element, settings, state));
                }
            }
        }

        return ValidationResult.Combine(results);
    }
}
