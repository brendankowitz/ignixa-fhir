// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>The facts of <c>patient_cohort</c>.</summary>
/// <param name="AsOf">The instant the criteria were evaluated at.</param>
/// <param name="AsOfSource">
/// One of <see cref="Clock.AsOfSources"/>: <c>argument</c> when the request gave asOf; <c>clock</c> when it defaulted to the clock's now
/// (<c>default-now</c> before the asOf sources were unified).
/// </param>
/// <param name="Count">How many patients meet every criterion: exact, never cut.</param>
/// <param name="Steps">Each criterion in the order applied, with the patients left after it.</param>
/// <param name="Patients">The patients counted, as Patient/&lt;id&gt;, in folder order; as many as fit, the rest counted in omitted.</param>
/// <param name="AsOfPrecision">The given asOf's precision when it is not an instant (<c>year</c>, <c>month</c>, <c>date</c>); absent otherwise.</param>
/// <param name="AsOfZoneAssumed"><c>true</c> when the given asOf was a date and time without a zone, read as UTC; absent otherwise.</param>
public sealed record CohortFacts(
    string AsOf,
    string AsOfSource,
    int Count,
    IReadOnlyList<CohortStep> Steps,
    IReadOnlyList<string> Patients,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AsOfPrecision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AsOfZoneAssumed = null);
