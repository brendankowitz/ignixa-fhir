// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Base for Unicode-family strategies. Gates application by FHIR type: only unbound free-text
/// primitives (<c>string</c>/<c>markdown</c>) are eligible, so a CJK/RTL/emoji value is never
/// dropped into a bound code, system URL, reference, or id. The schema supplies the type, so every
/// free-text field is reachable without an element-name allowlist.
/// </summary>
/// <remarks>
/// Type alone is not sufficient, because FHIR declares three structural elements as plain
/// <c>string</c>: <c>Reference.reference</c>, <c>Element.id</c> and <c>Expression.expression</c>.
/// None carries a terminology binding, so the type-and-binding gate admits all three, and a strategy
/// declaring <see cref="ValidityIntent.PreservesValidity"/> would emit
/// <c>"reference": "Patient/[emoji]"</c>. They are excluded by name because that is the only thing
/// that distinguishes them - the schema says <c>string</c> and means it.
/// <para>
/// The exclusion was previously supplied by accident: before issue #454, the element model's
/// name-equality recursion heuristic mistyped <c>Reference.reference</c> as <c>Reference</c>, which
/// the type gate rejected. Correcting the element model removed that accidental guard, so the rule
/// now lives here, where the promise above is actually made.
/// </para>
/// </remarks>
public abstract class FreeTextEdgeCaseStrategy : IEdgeCaseStrategy
{
    /// <inheritdoc />
    public abstract string Category { get; }

    /// <inheritdoc />
    public virtual EdgeCaseFamily Family => EdgeCaseFamily.Unicode;

    /// <inheritdoc />
    public virtual ValidityIntent Intent => ValidityIntent.PreservesValidity;

    /// <inheritdoc />
    public bool CanApply(MutationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.InstanceType is "string" or "markdown"
            && !target.IsRequiredBound
            && !IsStructuralString(target.ElementName);
    }

    /// <summary>
    /// True for the elements FHIR types as <c>string</c> but whose value is machine-read: a resource
    /// reference, an element id, and a FHIRPath expression. Free text in any of them is not a
    /// validity-preserving mutation whatever the schema calls the type.
    /// </summary>
    private static bool IsStructuralString(string elementName)
        => elementName is "reference" or "id" or "expression";

    /// <inheritdoc />
    public abstract MutationResult Apply(MutationTarget target, Randomizer rng);
}
