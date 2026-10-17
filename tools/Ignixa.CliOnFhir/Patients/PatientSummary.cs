// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One patient in <c>patient_list</c>. A lookup by <c>query</c> gives the Patient resource's own fields only, read from the
/// Patient resources alone so it stays fast on any folder; the listing without a query also summarizes each compartment.
/// </summary>
/// <param name="Id">The Patient's id, or its digest when it is not a FHIR id; pass it to <c>patient_context</c> root.</param>
/// <param name="Name">The first official, else usual, else use-less name, as its given names then family: data copied from the resource (never its free <c>text</c>).</param>
/// <param name="Gender">Administrative gender.</param>
/// <param name="BirthDate">Birth date as written.</param>
/// <param name="ResourceCount">The Patient and every resource in its compartment; absent from a lookup by query.</param>
/// <param name="EarliestDate">The earliest main clinical date among those resources, as written; absent when none has one, and from a lookup by query.</param>
/// <param name="LatestDate">The latest main clinical date among those resources, as written; absent when none has one, and from a lookup by query.</param>
/// <param name="Domains">The domains with at least one resource, in display order; absent from a lookup by query.</param>
public sealed record PatientSummary(
    string Id,
    string? Name,
    string? Gender,
    string? BirthDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ResourceCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EarliestDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LatestDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Domains = null);
