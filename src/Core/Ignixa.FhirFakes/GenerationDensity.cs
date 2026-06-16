// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes;

/// <summary>
/// Controls how densely the schema-driven faker populates generated resources.
/// This is a generation concern (which elements are emitted), distinct from the
/// per-leaf edge-case mutation strategies in the edge-case catalog.
/// </summary>
public enum GenerationDensity
{
    /// <summary>
    /// Required elements only (the default behavior). Optional elements are omitted.
    /// Maps to the "all-optional-omitted" cardinality edge case.
    /// </summary>
    Minimal,

    /// <summary>
    /// Reserved for future realistic optional-field selection.
    /// CURRENTLY BEHAVES IDENTICALLY TO <see cref="Minimal"/>: required elements only.
    /// </summary>
    Realistic,

    /// <summary>
    /// Required elements PLUS every optional element populated.
    /// Maps to the "every-optional-present" cardinality edge case.
    /// </summary>
    Maximal
}
