// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>A page of the folder's patients.</summary>
/// <param name="Total">Patients in the folder, or those matching <paramref name="Query"/> when one was given.</param>
/// <param name="Offset">Patients skipped before the first returned.</param>
/// <param name="Returned">Patients in <paramref name="Patients"/>.</param>
/// <param name="Sort"><c>name</c> (then id) or <c>latest</c> (latest clinical date first, then id).</param>
/// <param name="Patients">The page.</param>
/// <param name="ValueBindings">
/// <c>checked</c> when the session's terminology was loaded, so shown genders were also checked against
/// their required-binding ValueSets (a value outside is a digest, with an <c>invalid-value</c> issue naming the element and the
/// ValueSet); <c>not-checked</c> when no terminology call has loaded it, so only their shape was checked.
/// </param>
/// <param name="Query">The filter as given; null when the list was not filtered.</param>
/// <param name="DateRule">
/// How each resource's main clinical date is read, by type (types not listed have none). Given only when sorting by latest
/// date, where the rule decides the order; the dates in each row follow the same rule.
/// </param>
public sealed record PatientListFacts(
    int Total,
    int Offset,
    int Returned,
    string Sort,
    IReadOnlyList<PatientSummary> Patients,
    string ValueBindings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Query = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? DateRule = null);
