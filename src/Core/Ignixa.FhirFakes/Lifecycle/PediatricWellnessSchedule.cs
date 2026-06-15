// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Lifecycle event that generates pediatric wellness visits at recommended ages.
/// Follows AAP (American Academy of Pediatrics) Bright Futures schedule.
/// </summary>
/// <remarks>
/// <para>
/// Pediatric wellness visits are scheduled at the following ages:
/// 1, 2, 4, 6, 8, 10, 12, 14, 16, and 18 years.
/// </para>
/// <para>
/// Each visit generates an Encounter resource and may include age-appropriate observations
/// such as height, weight, BMI, and developmental assessments.
/// </para>
/// </remarks>
public sealed class PediatricWellnessSchedule : ILifecycleEvent
{
    /// <summary>
    /// The ages at which pediatric wellness visits are scheduled (in years).
    /// Based on AAP Bright Futures periodicity schedule for children and adolescents.
    /// </summary>
    private static readonly int[] WellnessVisitAges = [1, 2, 4, 6, 8, 10, 12, 14, 16, 18];

    /// <inheritdoc />
    public string Name => "PediatricWellnessSchedule";

    /// <inheritdoc />
    public bool IsApplicable(int patientAge) => WellnessVisitAges.Contains(patientAge);

    /// <inheritdoc />
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Create a wellness visit using the existing scenario infrastructure
        var builder = new ScenarioBuilder(schemaProvider);

        // Add the wellness visit encounter and age-appropriate growth vitals (height, weight, BMI, BP)
        builder
            .AddWellnessVisit($"Pediatric wellness visit - age {context.CurrentAge}")
            .AddSubScenario(
                CommonScenarios.RecordAgeAppropriateVitalSigns(context.CurrentAge, context.GetAttribute<string>("gender")),
                "Record Vitals");

        // Execute the scenario states against our context
        var states = builder.GetStates();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        foreach (var state in states)
        {
            state.Execute(context, faker);
        }
    }
}
