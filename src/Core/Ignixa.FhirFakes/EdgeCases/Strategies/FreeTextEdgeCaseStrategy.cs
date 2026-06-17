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
        return (target.InstanceType is "string" or "markdown") && !target.IsRequiredBound;
    }

    /// <inheritdoc />
    public abstract MutationResult Apply(MutationTarget target, Randomizer rng);
}
