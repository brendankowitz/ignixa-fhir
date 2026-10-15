// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The Patient resource's demographics; members without a value are left out. Values are data copied from the resource.</summary>
/// <param name="Reference">The Patient, <c>Patient/id</c> (the id as a digest when it is not a FHIR id).</param>
/// <param name="Name">The first official, else usual, else use-less name, as its given names then family (never the name's free <c>text</c>).</param>
/// <param name="Gender">Administrative gender.</param>
/// <param name="BirthDate">Birth date as written.</param>
/// <param name="Deceased"><c>true</c>/<c>false</c> or the date of death as written; null when not stated.</param>
public sealed record Demographics(
    string Reference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Gender,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BirthDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Deceased);
