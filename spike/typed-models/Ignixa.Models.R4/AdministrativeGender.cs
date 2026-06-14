// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;

namespace Ignixa.Models.R4;

/// <summary>
/// FHIR R4 AdministrativeGender value set (the gender of a person used for administrative purposes).
/// </summary>
public enum AdministrativeGender
{
    [EnumLiteral("male")]
    Male,

    [EnumLiteral("female")]
    Female,

    [EnumLiteral("other")]
    Other,

    [EnumLiteral("unknown")]
    Unknown,
}
