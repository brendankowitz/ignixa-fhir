// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>The facts of <c>patient_cohort</c>.</summary>
/// <param name="AsOf">The instant the criteria were evaluated at.</param>
/// <param name="Count">How many patients meet every criterion: exact, never cut.</param>
/// <param name="Steps">Each criterion in the order applied, with the patients left after it.</param>
/// <param name="Patients">The patients counted, as Patient/&lt;id&gt;, in folder order; as many as fit, the rest counted in omitted.</param>
public sealed record CohortFacts(string AsOf, int Count, IReadOnlyList<CohortStep> Steps, IReadOnlyList<string> Patients);
