// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Base for Unicode-family strategies. Gates application to an allowlist of FHIR free-text element
/// names so a CJK/RTL/emoji value is never dropped into a bound code, system URL, reference, or id.
/// </summary>
public abstract class FreeTextEdgeCaseStrategy : IEdgeCaseStrategy
{
    private static readonly FrozenSet<string> FreeTextElements = new[]
    {
        "family", "given", "prefix", "suffix", "text", "display", "title", "line",
        "city", "district", "state", "note", "comment", "description", "label",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public abstract string Category { get; }

    /// <inheritdoc />
    public EdgeCaseFamily Family => EdgeCaseFamily.Unicode;

    /// <inheritdoc />
    public ValidityIntent Intent => ValidityIntent.PreservesValidity;

    /// <inheritdoc />
    public bool CanApply(MutationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return FreeTextElements.Contains(target.ElementName);
    }

    /// <inheritdoc />
    public abstract MutationResult Apply(MutationTarget target, Randomizer rng);
}
