// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// FHIR version enumeration for version-aware primitive type support.
/// </summary>
public enum FhirVersion : byte
{
    /// <summary>FHIR STU3 (3.0.x)</summary>
    STU3 = 3,

    /// <summary>FHIR R4 (4.0.x)</summary>
    R4 = 4,

    /// <summary>FHIR R4B (4.1.x / 4.3.x)</summary>
    R4B = 41,

    /// <summary>FHIR R5 (5.0.x)</summary>
    R5 = 5,

    /// <summary>FHIR R6 (6.0.x)</summary>
    R6 = 6
}
