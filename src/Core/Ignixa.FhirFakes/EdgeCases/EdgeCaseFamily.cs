// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// Top-level grouping of edge-case strategies. A family is a coarse selector (e.g. "unicode")
/// under which one or more hierarchical categories live (e.g. "unicode.rtl").
/// </summary>
/// <remarks>
/// Only <see cref="Unicode"/> and <see cref="Temporal"/> ship strategies in this MVP. The remaining
/// members are defined so the catalog vocabulary is stable while later families are added.
/// </remarks>
public enum EdgeCaseFamily
{
    /// <summary>Unicode-heavy free-text perturbations (CJK, RTL, combining marks, emoji, zero-width).</summary>
    Unicode,

    /// <summary>Date/dateTime boundary perturbations (leap years, far past/future, partial precision).</summary>
    Temporal,

    /// <summary>String length and content boundaries (max-length, whitespace-only, control chars). Not yet implemented.</summary>
    StringBoundary,

    /// <summary>Cardinality perturbations (omit all optional, populate every optional). Not yet implemented.</summary>
    Cardinality,

    /// <summary>Structural perturbations (deep nesting, contained resources). Not yet implemented.</summary>
    Structural,
}
