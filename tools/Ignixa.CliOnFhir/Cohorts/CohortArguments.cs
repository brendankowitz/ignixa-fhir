// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>The arguments of <c>patient_cohort</c> as the client sent them, before <see cref="CohortRequest.Parse"/> checks them.</summary>
/// <param name="AsOf">The date (or instant) the question is asked on; null for now.</param>
/// <param name="Living">False counts the deceased too; null or true counts only the living.</param>
/// <param name="AgeMin">Youngest age in whole years at asOf, inclusive.</param>
/// <param name="AgeMax">Oldest age in whole years at asOf, inclusive.</param>
/// <param name="Gender">Patient.gender.</param>
/// <param name="Conditions">Condition criteria: one object or a list.</param>
/// <param name="Medications">MedicationRequest criteria: one object or a list.</param>
/// <param name="Immunizations">Immunization criteria: one object or a list.</param>
/// <param name="Observations">Observation criteria: one object or a list.</param>
internal sealed record CohortArguments(
    string? AsOf = null,
    bool? Living = null,
    int? AgeMin = null,
    int? AgeMax = null,
    string? Gender = null,
    JsonElement? Conditions = null,
    JsonElement? Medications = null,
    JsonElement? Immunizations = null,
    JsonElement? Observations = null,
    string? Detail = null);
