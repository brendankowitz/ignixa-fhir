// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>One patient in <c>patient_list</c>.</summary>
/// <param name="Id">The Patient's id, or its digest when it is not a FHIR id; pass it to <c>context_get_root</c>.</param>
/// <param name="Name">The first official, else usual, else use-less name, as its given names then family: data copied from the resource (never its free <c>text</c>).</param>
/// <param name="Gender">Administrative gender.</param>
/// <param name="BirthDate">Birth date as written.</param>
/// <param name="ResourceCount">The Patient and every resource in its compartment.</param>
/// <param name="EarliestDate">The earliest main clinical date among those resources, as written; null when none has one.</param>
/// <param name="LatestDate">The latest main clinical date among those resources, as written; null when none has one.</param>
/// <param name="Domains">The domains with at least one resource, in display order.</param>
public sealed record PatientSummary(string Id, string? Name, string? Gender, string? BirthDate, int ResourceCount, string? EarliestDate, string? LatestDate, IReadOnlyList<string> Domains);
