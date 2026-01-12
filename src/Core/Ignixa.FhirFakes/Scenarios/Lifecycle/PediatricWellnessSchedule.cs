// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Lifecycle;

/// <summary>
/// Implements the CDC-recommended pediatric wellness visit schedule.
/// Generates annual wellness encounters with vital signs at key developmental milestones.
/// </summary>
/// <remarks>
/// <para>
/// This lifecycle event follows the American Academy of Pediatrics (AAP) Bright Futures
/// periodicity schedule for well-child visits. These visits are critical for:
/// - Growth and developmental monitoring
/// - Preventive health screening
/// - Immunization administration
/// - Parental guidance and anticipatory care
/// </para>
/// <para>
/// Schedule follows ages: 1, 2, 4, 6, 8, 10, 12, 14, 16, 18 years.
/// Note: The full AAP schedule includes visits at 3-5 days, 1 month, 2 months, 4 months,
/// 6 months, 9 months, 12 months, 15 months, 18 months, 2 years, 2.5 years, 3 years, and annually
/// thereafter. This implementation uses the simplified annual schedule from the ADR specification.
/// </para>
/// <para>
/// Clinical rationale:
/// - Ages 1-5: Rapid growth, motor skill development, language acquisition
/// - Ages 6-12: School readiness, chronic disease screening (obesity, asthma)
/// - Ages 13-18: Adolescent health, mental health screening, risk behavior counseling
/// </para>
/// </remarks>
public sealed class PediatricWellnessSchedule : ILifecycleEvent
{
    private readonly int[] _visitAges = [1, 2, 4, 6, 8, 10, 12, 14, 16, 18];

    /// <summary>
    /// Determines if a wellness visit is scheduled at the specified age.
    /// </summary>
    /// <param name="patientAge">The patient's current age in years.</param>
    /// <returns><c>true</c> if age matches a scheduled wellness visit; otherwise, <c>false</c>.</returns>
    public bool IsApplicable(int patientAge) => _visitAges.Contains(patientAge);

    /// <summary>
    /// Executes the wellness visit by creating an encounter and recording vital signs.
    /// </summary>
    /// <param name="context">The scenario context for resource generation.</param>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <remarks>
    /// <para>
    /// Each wellness visit includes:
    /// 1. An ambulatory encounter with reason "Routine wellness visit"
    /// 2. Standard vital signs (height, weight, BMI, blood pressure)
    /// </para>
    /// <para>
    /// The encounter is marked as "finished" and linked to the patient.
    /// All observations are linked to the encounter for proper clinical context.
    /// </para>
    /// </remarks>
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Create a scenario builder for composing the wellness visit
        var builder = new ScenarioBuilder(schemaProvider);

        // Build the wellness visit scenario with vital signs
        var wellnessScenario = builder
            .AddWellnessVisit($"Routine wellness visit - age {context.CurrentAge}")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Vitals");

        // Execute the wellness visit states directly on the context
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
        foreach (var state in wellnessScenario.GetStates())
        {
            state.Execute(context, faker);
        }
    }
}
