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
    /// Specification-level validation: structure + required terminology bindings.
    /// </summary>
    Spec = 1,

    /// <summary>
    /// Full validation: structure + required and extensible bindings, display checks, invariants/slicing.
    /// </summary>
    Full = 2,

    /// <summary>
    /// Compatibility validation: matches Microsoft FHIR Server validation behavior.
    /// More lenient than Spec - accepts resources that pass in Microsoft FHIR Server.
    /// Use for migration scenarios and E2E test compatibility.
    /// </summary>
    Compatibility = 3
}
