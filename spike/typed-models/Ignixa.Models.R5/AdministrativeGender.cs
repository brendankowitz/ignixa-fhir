// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;

namespace Ignixa.Models.R5;

/// <summary>
/// FHIR R5 AdministrativeGender value set. Distinct type from <see cref="Ignixa.Models.R4.AdministrativeGender"/>
/// to demonstrate per-version namespace isolation.
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
