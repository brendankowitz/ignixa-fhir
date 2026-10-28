// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Unified validation depth controlling both structural and terminology validation.
/// </summary>
public enum ValidationDepth
{
    /// <summary>
    /// Minimal validation: basic structure only. No invariants, no terminology.
    /// </summary>
    Minimal = 0,

    /// <summary>
    /// Specification-level validation: structure, cardinality and types of every element and nested datatype (complex choice
    /// variants, extension values, and the insides of CodeableConcept, Coding and Reference included), of each resource a
    /// Bundle or Parameters carries, required terminology bindings and reference target types.
    /// </summary>
    Spec = 1,

    /// <summary>
    /// Full validation: Spec, plus extensible bindings, display checks, invariants, slicing, and extensions against their
    /// loaded definitions (their contexts and modifier placement included).
    /// </summary>
    Full = 2,

    /// <summary>
    /// Compatibility validation: matches Microsoft FHIR Server validation behavior.
    /// More lenient than Spec - accepts resources that pass in Microsoft FHIR Server (relative Coding.system, text-only
    /// concepts under required bindings, unchecked reference target types, relative extension urls).
    /// Use for migration scenarios and E2E test compatibility.
    /// </summary>
    Compatibility = 3
}
