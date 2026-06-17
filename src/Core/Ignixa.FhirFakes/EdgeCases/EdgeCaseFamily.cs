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
/// <see cref="Unicode"/>, <see cref="Temporal"/> and <see cref="StringBoundary"/> ship strategies.
/// <see cref="Cardinality"/> and <see cref="Structural"/> are defined so the catalog vocabulary is
/// stable while those later families are added.
/// </remarks>
public enum EdgeCaseFamily
{
    /// <summary>Unicode-heavy free-text perturbations (CJK, RTL, combining marks, emoji, zero-width).</summary>
    Unicode,

    /// <summary>Date/dateTime boundary perturbations (leap years, far past/future, partial precision).</summary>
    Temporal,

    /// <summary>String length and content boundaries (max-length, whitespace-only, control chars).</summary>
    StringBoundary,

    /// <summary>Cardinality perturbations (omit all optional, populate every optional). Not yet implemented.</summary>
    Cardinality,

    /// <summary>Structural perturbations (deep nesting, contained resources). Not yet implemented.</summary>
    Structural,
}
