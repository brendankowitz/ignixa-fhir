// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Validation depth modes for FHIR $validate operation.
/// Corresponds to Prefer: mode= header values.
/// See https://hl7.org/fhir/R4/resource-operation-validate.html
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// Minimal validation: Structure only (required fields, cardinality).
    /// No terminology validation or invariants.
    /// </summary>
    Minimal = 0,

    /// <summary>
    /// Normal validation: Structure + required terminology bindings.
    /// Skips extensible bindings and display validation.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Full validation: Structure + all bindings (required + extensible) + display + invariants.
    /// Most comprehensive validation mode.
    /// </summary>
    Full = 2
}
