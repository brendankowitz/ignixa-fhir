// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A strategy's declared claim about whether its mutation keeps the resource spec-valid.
/// The claim is verified downstream (validate-and-bucket), never assumed.
/// </summary>
public enum ValidityIntent
{
    /// <summary>The mutation is intended to keep the resource valid against the FHIR specification.</summary>
    PreservesValidity,

    /// <summary>The mutation may produce an invalid resource depending on profile/context.</summary>
    MayViolate,

    /// <summary>The mutation deliberately produces an invalid resource (negative testing).</summary>
    AlwaysInvalid,
}
