// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>A page of the folder's patients.</summary>
/// <param name="Total">Patients in the folder.</param>
/// <param name="Offset">Patients skipped before the first returned.</param>
/// <param name="Returned">Patients in <paramref name="Patients"/>.</param>
/// <param name="Sort"><c>name</c> (then id) or <c>latest</c> (latest clinical date first, then id).</param>
/// <param name="DateRule">How each resource's main clinical date is read, by type; types not listed have none.</param>
/// <param name="Patients">The page.</param>
public sealed record PatientListFacts(int Total, int Offset, int Returned, string Sort, IReadOnlyDictionary<string, string> DateRule, IReadOnlyList<PatientSummary> Patients);
