// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Pediatric Ear Infection (Acute Otitis Media) scenarios.
/// </summary>
public static class EarInfectionScenario
{
    // SNOMED CT codes
    private static readonly FhirCode AcuteOtitisMedia = new(
        FhirCode.Systems.SnomedCt,
        "7091009",
        "Acute otitis media");

    private static readonly FhirCode OtoscopyProcedure = new(
        FhirCode.Systems.SnomedCt,
        "16247007",
        "Otoscopy");

    // LOINC codes for observations
    private static readonly FhirCode BodyTemperature = new(
        FhirCode.Systems.Loinc,
        "8310-5",
        "Body temperature");

    private static readonly FhirCode PainSeverity = new(
        FhirCode.Systems.Loinc,
        "72514-3",
        "Pain severity - 0-10 verbal numeric rating [Score] - Reported");

    /// <summary>
    /// Generates a Pediatric Acute Ear Infection scenario.
    ///
    /// Timeline:
    /// 1. Initial visit for ear pain
    /// 2. Clinical observations (fever, pain)
    /// 3. Otoscopy examination
    /// 4. Acute otitis media diagnosis
    /// 5. Antibiotic prescription (Amoxicillin)
    /// 6. Optional follow-up visit after 10 days with resolution
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Child's age in years (default: 4, range 2-10 for typical presentation).</param>
    /// <param name="gender">Child's gender (default: random).</param>
    /// <param name="includeFollowUp">Whether to include follow-up visit with resolution (default: true).</param>
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetPediatricEarInfection(
        this IFhirSchemaProvider schemaProvider,
        int age = 4,
        string? gender = null,
        bool includeFollowUp = true)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Pediatric Ear Infection (Acute Otitis Media)")
            .WithDescription("Child presenting with ear pain, fever, and diagnosis of acute otitis media, treated with antibiotics.")

            // Initial patient (child)
            .WithPatient(age: age, gender: gender)

            // Initial visit - ear pain complaint
            .AddEncounter("Ear pain visit")

            // Symptoms - elevated temperature (fever)
            .AddObservation(BodyTemperature, minValue: 38.0m, maxValue: 39.5m, unit: "Cel", unitCode: "Cel")

            // Pain severity assessment (pediatric scale 5-8 out of 10)
            .AddObservation(PainSeverity, minValue: 5m, maxValue: 8m, unit: "{score}", unitCode: "{score}")

            // Physical examination - otoscopy
            .AddProcedure(
                OtoscopyProcedure,
                duration: TimeSpan.FromMinutes(10),
                outcome: "Erythematous, bulging tympanic membrane observed consistent with acute otitis media",
                bodySite: "Ear",
                reason: "Evaluation of ear pain")

            // Diagnosis
            .AddConditionOnset(AcuteOtitisMedia, severity: 2, assignToAttribute: "ear_infection_condition")

            // Treatment - Amoxicillin (standard pediatric dosing for otitis media)
            // Standard dose: 40-45 mg/kg/day divided into 2 doses for 10 days
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Amoxicillin500mg,
                IsChronic = false,
                Frequency = "twice-daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                DurationDays = 10,
                DosageInstructions = "Take 1 tablet by mouth twice daily for 10 days. Complete full course even if symptoms improve.",
                ReasonConditionAttribute = "ear_infection_condition"
            });

        // Optional follow-up visit
        if (includeFollowUp)
        {
            builder
                .DelayDays(10)
                .AddEncounter("Ear infection follow-up")
                .AddObservation(BodyTemperature, minValue: 36.5m, maxValue: 37.2m, unit: "Cel", unitCode: "Cel")
                .AddObservation(PainSeverity, minValue: 0m, maxValue: 1m, unit: "{score}", unitCode: "{score}")
                .AddProcedure(
                    OtoscopyProcedure,
                    duration: TimeSpan.FromMinutes(5),
                    outcome: "Tympanic membrane normal appearance. Resolution of infection.",
                    bodySite: "Ear",
                    reason: "Follow-up examination")
                .EndCondition("ear_infection_condition", clinicalStatus: ConditionClinicalStatus.Resolved);
        }

        return builder.Build();
    }
}
