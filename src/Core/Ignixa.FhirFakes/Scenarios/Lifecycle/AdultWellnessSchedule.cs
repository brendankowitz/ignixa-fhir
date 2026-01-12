// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Lifecycle;

/// <summary>
/// Implements annual adult wellness visit schedule starting at age 18.
/// Generates yearly wellness encounters with vital signs and age-appropriate preventive screenings.
/// </summary>
/// <remarks>
/// <para>
/// This lifecycle event follows the Medicare Annual Wellness Visit (AWV) and USPSTF
/// (U.S. Preventive Services Task Force) recommendations for adult preventive care.
/// Annual wellness visits are critical for:
/// - Chronic disease monitoring and management
/// - Preventive health screening (cancer, cardiovascular disease)
/// - Health risk assessment and counseling
/// - Medication review and reconciliation
/// </para>
/// <para>
/// Age-appropriate screenings vary by patient age:
/// - Ages 18-39: Blood pressure, cholesterol (if risk factors), STI screening
/// - Ages 40-64: Blood pressure, cholesterol, diabetes, cancer screening (mammography, colonoscopy)
/// - Ages 65+: All of the above plus bone density, fall risk, cognitive assessment
/// </para>
/// <para>
/// Clinical rationale:
/// - Early detection of chronic conditions (hypertension, diabetes, hyperlipidemia)
/// - Cancer screening saves lives (breast, colorectal, cervical, lung)
/// - Preventive counseling (smoking cessation, weight management, alcohol use)
/// - Immunization updates (influenza, pneumococcal, shingles)
/// </para>
/// </remarks>
public sealed class AdultWellnessSchedule : ILifecycleEvent
{
    /// <summary>
    /// Determines if an annual wellness visit is due for adults aged 18 and older.
    /// </summary>
    /// <param name="patientAge">The patient's current age in years.</param>
    /// <returns><c>true</c> if patient is 18 or older; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// This implementation schedules wellness visits for every year starting at age 18.
    /// In a production system, you might want to skip certain years or add logic to
    /// vary the frequency based on patient risk factors.
    /// </remarks>
    public bool IsApplicable(int patientAge) => patientAge >= 18;

    /// <summary>
    /// Executes the annual wellness visit by creating an encounter, recording vital signs,
    /// and performing age-appropriate preventive screenings.
    /// </summary>
    /// <param name="context">The scenario context for resource generation.</param>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <remarks>
    /// <para>
    /// Each wellness visit includes:
    /// 1. An ambulatory encounter with reason "Annual wellness visit"
    /// 2. Standard vital signs (height, weight, BMI, blood pressure)
    /// 3. Age-appropriate lab panels and screenings
    /// </para>
    /// <para>
    /// Age-stratified preventive care logic:
    /// - Ages 18-39: Basic vitals and cardiovascular risk assessment
    /// - Ages 40+: Add Comprehensive Metabolic Panel (CMP) for diabetes and kidney function screening
    /// - Ages 45+: Add Lipid Panel for cardiovascular risk assessment
    /// </para>
    /// </remarks>
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var patientAge = context.CurrentAge;

        // Create a scenario builder for composing the wellness visit
        var builder = new ScenarioBuilder(schemaProvider);

        // Start with the wellness encounter and basic vital signs
        builder
            .AddWellnessVisit($"Annual wellness visit - age {patientAge}")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Vitals");

        // Age-appropriate screenings
        if (patientAge >= 40)
        {
            // CMP for diabetes and kidney function screening (recommended starting at age 40)
            builder.AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Metabolic Panel");
        }

        if (patientAge >= 45)
        {
            // Lipid panel for cardiovascular risk assessment (USPSTF recommends starting at 40-45)
            builder.AddSubScenario(CommonScenarios.LipidPanel(), "Lipid Panel");
        }

        // Execute the wellness visit states directly on the context
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
        foreach (var state in builder.GetStates())
        {
            state.Execute(context, faker);
        }
    }
}
