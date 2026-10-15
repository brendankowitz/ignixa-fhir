// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The Patient resource's demographics.</summary>
/// <param name="Reference">The Patient, <c>Patient/id</c>.</param>
/// <param name="Name">The first official, else usual, else use-less name, as its text or given names then family.</param>
/// <param name="Gender">Administrative gender.</param>
/// <param name="BirthDate">Birth date as written.</param>
/// <param name="Deceased"><c>true</c>/<c>false</c> or the date of death as written; null when not stated.</param>
public sealed record Demographics(string Reference, string? Name, string? Gender, string? BirthDate, string? Deceased);
