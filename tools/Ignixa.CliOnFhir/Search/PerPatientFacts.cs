// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>What <c>perPatient</c> kept of a search's matches.</summary>
/// <param name="Pick">The argument as given: <c>latest</c>, <c>earliest</c> or <c>first</c>.</param>
/// <param name="Sort">The <c>_sort</c> the pick ordered the matches by (<c>-date</c>); absent for <c>first</c>, which uses the query's.</param>
/// <param name="Matches">The matches before the selection.</param>
/// <param name="Patients">The distinct patients the matches belong to, before and after the selection alike.</param>
/// <param name="Kept">The matches kept, one per patient (fewer only when one match is the first of two patients); <c>total</c> counts these.</param>
/// <param name="WithoutPatient">Matches in no patient's compartment, which the selection leaves out.</param>
public sealed record PerPatientFacts(
    string Pick,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sort,
    int Matches,
    int Patients,
    int Kept,
    int WithoutPatient);
