// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One <c>compose.include</c> or <c>compose.exclude</c> entry.</summary>
/// <param name="System">The code system; null when the rule selects only through <paramref name="ValueSets"/>.</param>
/// <param name="Version">The code system version the rule pins; null for any.</param>
/// <param name="Concepts">Concepts listed explicitly; empty when the rule uses filters or the whole system.</param>
/// <param name="Filters">Filters, all of which a concept must pass.</param>
/// <param name="ValueSets">Canonicals of ValueSets whose intersection (with the system part, if any) the rule selects.</param>
public sealed record ValueSetRule(
    string? System,
    string? Version,
    IReadOnlyList<ValueSetRuleConcept> Concepts,
    IReadOnlyList<ValueSetFilter> Filters,
    IReadOnlyList<string> ValueSets);
