// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating pediatric asthma onset scenarios.
/// Demonstrates Layer 2 features: probabilistic disease onset with evidence-based prevalence rates.
/// </summary>
/// <remarks>
/// Asthma prevalence in children (CDC/NHIS 2021):
/// - Overall pediatric prevalence: 26.3% of children ages 1-17 have ever been diagnosed with asthma
/// - Peak onset: Ages 3-6 (early childhood respiratory infections are major triggers)
/// - Gender differences: Higher in boys before puberty, higher in girls after puberty
/// - Environmental triggers: Allergens, respiratory infections, air pollution, tobacco smoke
///
/// This scenario demonstrates:
/// - **Probabilistic branching**: 26.3% chance of asthma diagnosis during childhood
/// - **Reusable fragments**: CommonScenarios.InfectionMonitoringVitals() for respiratory assessment
/// - **Temporal progression**: Multi-year follow-up with exacerbation patterns
/// </remarks>
public static class PediatricAsthmaOnsetScenario
{
    /// <summary>
    /// Generates a pediatric asthma onset scenario demonstrating realistic 26.3% prevalence.
    ///
    /// Demonstrates:
    /// - **Probabilistic branching**: 26.3% chance of asthma onset (CDC prevalence data)
    /// - **Reusable fragments**: CommonScenarios.InfectionMonitoringVitals() for respiratory monitoring
    /// - **Trigger-based onset**: Respiratory infection as asthma trigger
    /// - **Disease progression**: Initial diagnosis → controller medication → periodic monitoring
    ///
    /// Timeline:
    /// Year 0 (Age 3): Well-child visit - baseline respiratory health
    /// Year 1 (Age 4): Respiratory infection (URI/bronchiolitis) - potential asthma trigger
    /// Year 1 (Age 4): Probabilistic branch: 26.3% develop asthma diagnosis
    ///     - TRUE PATH: Asthma diagnosis with controller + rescue medications
    ///     - FALSE PATH: Recovery without chronic condition
    /// Year 2 (Age 5): Follow-up visits with peak flow monitoring (if asthma diagnosed)
    /// Year 3 (Age 6): Continued monitoring and medication management
    ///
    /// Generated Resources (if asthma develops):
    /// - 5-7 Encounters (well-child visits, sick visits, follow-ups)
    /// - 15+ Observations (vital signs, peak flow measurements)
    /// - 1 Condition (asthma)
    /// - 2 MedicationRequests (controller: inhaled corticosteroid, rescue: albuterol)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="startingAge">Child's starting age (default: 3 years - peak onset period).</param>
    /// <param name="gender">Child's gender (default: random, but "male" has higher prevalence before puberty).</param>
    /// <param name="includeProbabilisticOnset">
    /// Whether to include probabilistic asthma onset (default: true).
    /// Set to false for deterministic test scenarios.
    /// </param>
    /// <returns>A complete scenario context with pediatric asthma onset journey.</returns>
    public static ScenarioContext GetPediatricAsthmaOnset(
        this IFhirSchemaProvider schemaProvider,
        int startingAge = 3,
        string? gender = null,
        bool includeProbabilisticOnset = true)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Pediatric Asthma Onset with Evidence-Based Prevalence")
            .WithDescription("Multi-year pediatric journey demonstrating realistic 26.3% asthma onset probability following respiratory infection trigger.")

            // === YEAR 0: Baseline Well-Child Visit (Age 3) ===
            .WithPatient(age: startingAge, gender: gender)
            .AddEncounter("Well-child visit")

            // Pediatric vital signs - normal ranges
            .AddObservation(VitalSigns.BodyHeight, minValue: 90m, maxValue: 100m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, minValue: 13m, maxValue: 16m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.HeartRate, minValue: 80m, maxValue: 120m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.RespiratoryRate, minValue: 20m, maxValue: 30m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.BodyTemperature, value: 36.8m, unit: "°C", unitCode: "Cel")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, minValue: 97m, maxValue: 100m, unit: "%", unitCode: "%")

            // === YEAR 1: Respiratory Infection - Asthma Trigger Event (Age 4) ===
            .DelayMonths(10)
            .AddEncounter("Sick visit - cough and wheezing")

            // REUSABLE FRAGMENT: Infection monitoring vitals
            .AddSubScenario(CommonScenarios.InfectionMonitoringVitals(), "Respiratory Infection Assessment")

            // Fever indicating viral URI
            .AddObservation(VitalSigns.BodyTemperature, minValue: 38.0m, maxValue: 39.0m, unit: "°C", unitCode: "Cel")

            // Diagnose acute upper respiratory infection (potential asthma trigger)
            .AddConditionOnset(FhirCode.Conditions.AcuteUpperRespiratoryInfection, severity: 2, assignToAttribute: "uri_condition")

            // Short-term treatment for URI
            .DelayWeeks(2)
            .AddEncounter("Follow-up - URI resolution check");

        // PROBABILISTIC BRANCHING: 26.3% develop asthma (CDC prevalence)
        if (includeProbabilisticOnset)
        {
            builder.AddProbabilisticBranch(
                0.263,  // 26.3% probability based on CDC/NHIS pediatric asthma prevalence

                // TRUE PATH: Asthma diagnosis (26.3% of children)
                // Persistent respiratory symptoms lead to asthma diagnosis
                new CompositeState
                {
                    Name = "Asthma_Onset_Pathway",
                    States =
                    [
                        // Continued respiratory symptoms after URI
                        new DelayState { Exact = TimeSpan.FromDays(14) },
                        new EncounterState
                        {
                            Name = "Asthma_Evaluation",
                            EncounterClass = "ambulatory",
                            Reason = "Persistent cough and wheezing after URI"
                        },

                        // Asthma diagnosis with peak flow assessment
                        new ConditionOnsetState
                        {
                            Name = "Asthma_Diagnosis",
                            Code = FhirCode.Conditions.Asthma,
                            Severity = 2,  // Mild persistent asthma
                            AssignToAttribute = "asthma_condition"
                        },

                        // Peak flow measurement (reduced during diagnosis)
                        ObservationState.PeakFlow(value: 180m),  // Below expected for age

                        // Rescue inhaler (immediate relief)
                        MedicationOrderState.Albuterol(),

                        // Controller medication (long-term management)
                        MedicationOrderState.FlucticasonePropionate(),

                        // === YEAR 2: Asthma Management Follow-Ups (Age 5) ===
                        DelayState.Months(3),
                        new EncounterState
                        {
                            Name = "Asthma_3Month_Followup",
                            EncounterClass = "ambulatory",
                            Reason = "Asthma follow-up - 3 month"
                        },
                        ObservationState.PeakFlow(),  // Improved with treatment

                        DelayState.Months(3),
                        new EncounterState
                        {
                            Name = "Asthma_6Month_Followup",
                            EncounterClass = "ambulatory",
                            Reason = "Asthma follow-up - 6 month"
                        },
                        ObservationState.PeakFlow(),

                        // Potential exacerbation (20% chance in first year)
                        ProbabilisticBranchState.Binary(
                            0.20,  // 20% experience exacerbation in first year
                            new CompositeState
                            {
                                Name = "Asthma_Exacerbation",
                                States =
                                [
                                    DelayState.Months(2),
                                    new EncounterState
                                    {
                                        Name = "Asthma_Exacerbation_Visit",
                                        EncounterClass = "emergency",
                                        Reason = "Asthma exacerbation - severe wheezing and shortness of breath"
                                    },
                                    ObservationState.PeakFlow(value: 150m),  // Significantly reduced
                                    new SetAttributeState { AttributeName = "asthma_exacerbation_count", Value = 1 },
                                    DelayState.Weeks(1),
                                    new EncounterState
                                    {
                                        Name = "Post_Exacerbation_Followup",
                                        EncounterClass = "ambulatory",
                                        Reason = "Post-exacerbation follow-up"
                                    },
                                    ObservationState.PeakFlow()
                                ]
                            },
                            new DelayState { Name = "No_Exacerbation", Exact = TimeSpan.Zero }
                        ),

                        // === YEAR 3: Annual Asthma Review (Age 6) ===
                        DelayState.Months(6),
                        new EncounterState
                        {
                            Name = "Asthma_Annual_Review",
                            EncounterClass = "ambulatory",
                            Reason = "Asthma annual review"
                        },
                        ObservationState.PeakFlow(),

                        // Asthma control assessment (well-controlled: consider stepping down therapy)
                        new SetAttributeState { AttributeName = "asthma_control_status", Value = "well-controlled" }
                    ]
                },

                // FALSE PATH: No asthma diagnosis (73.7% of children)
                // URI resolves without chronic respiratory condition
                new CompositeState
                {
                    Name = "No_Asthma_Pathway",
                    States =
                    [
                        new DelayState { Exact = TimeSpan.FromDays(7) },
                        new EncounterState
                        {
                            Name = "URI_Resolution",
                            EncounterClass = "ambulatory",
                            Reason = "Follow-up - URI fully resolved"
                        },

                        // Normal respiratory assessment
                        new SetAttributeState { AttributeName = "respiratory_status", Value = "normal" },

                        // Continue routine well-child visits
                        DelayState.Months(12),
                        new EncounterState
                        {
                            Name = "Annual_Wellchild_Visit",
                            EncounterClass = "ambulatory",
                            Reason = "Annual well-child visit"
                        }
                    ]
                }
            );
        }
        else
        {
            // Deterministic path for testing (no asthma)
            builder.DelayWeeks(1)
                .AddEncounter("URI resolved - no chronic condition");
        }

        return builder.Build();
    }

    /// <summary>
    /// Generates multiple pediatric patients to demonstrate asthma prevalence statistics.
    /// Creates a cohort of children and applies 26.3% onset probability to each.
    ///
    /// Use case: Generate test data showing realistic population-level asthma prevalence.
    /// Expected outcome: Approximately 26% of children will have asthma diagnosis.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="cohortSize">Number of children in the cohort (default: 100).</param>
    /// <param name="startingAge">Age range for cohort (default: 3-10 years).</param>
    /// <returns>List of scenario contexts representing pediatric cohort.</returns>
    public static IReadOnlyList<ScenarioContext> GetPediatricCohortWithAsthmaPrevalence(
        this IFhirSchemaProvider schemaProvider,
        int cohortSize = 100,
        int startingAge = 5)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var contexts = new List<ScenarioContext>(cohortSize);

        for (var i = 0; i < cohortSize; i++)
        {
            var context = schemaProvider.GetPediatricAsthmaOnset(
                startingAge: startingAge,
                gender: i % 2 == 0 ? "male" : "female",  // Alternate gender
                includeProbabilisticOnset: true);

            contexts.Add(context);
        }

        return contexts;
    }

    /// <summary>
    /// Generates a pediatric asthma scenario with environmental triggers and allergy correlations.
    /// Demonstrates complex interaction between allergies, environmental exposures, and asthma development.
    ///
    /// Risk factors modeled:
    /// - Allergic rhinitis: Increases asthma risk to 40% (Allergic March progression)
    /// - Family history: Doubles baseline risk (not modeled in this demo - would need patient genealogy)
    /// - Environmental tobacco smoke: Increases risk by 30-70% (CDC)
    ///
    /// Timeline:
    /// Year 0 (Age 2): Allergic rhinitis diagnosis (hay fever)
    /// Year 1 (Age 3): Probabilistic asthma onset (40% in children with allergic rhinitis)
    /// Year 2-3: Asthma management with allergen avoidance education
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Starting age (default: 2 years - before typical allergic march).</param>
    /// <param name="gender">Child's gender.</param>
    /// <returns>A scenario context with allergy-associated asthma progression.</returns>
    public static ScenarioContext GetAllergicMarchAsthma(
        this IFhirSchemaProvider schemaProvider,
        int age = 2,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Allergic March: Rhinitis → Asthma Progression")
            .WithDescription("Pediatric scenario demonstrating 'allergic march' - progression from allergic rhinitis to asthma (40% prevalence in allergic children).")

            // === YEAR 0: Allergic Rhinitis Diagnosis (Age 2) ===
            .WithPatient(age: age, gender: gender)
            .AddEncounter("Sick visit - persistent nasal congestion and sneezing")

            // Vital signs
            .AddObservation(VitalSigns.BodyHeight, minValue: 85m, maxValue: 90m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, minValue: 12m, maxValue: 14m, unit: "kg", unitCode: "kg")
            .AddSubScenario(CommonScenarios.InfectionMonitoringVitals(), "Baseline Assessment")

            // Allergic rhinitis diagnosis
            .AddConditionOnset(FhirCode.Conditions.AllergicRhinitis, severity: 2, assignToAttribute: "rhinitis_condition")

            // TODO: Allergy testing reveals environmental allergens (AddAllergyIntolerance method not yet implemented)
            // .AddAllergyIntolerance(FhirCode.Allergens.DustMites, category: "environment", severity: "moderate")
            // .AddAllergyIntolerance(FhirCode.Allergens.Pollen, category: "environment", severity: "mild")

            // Antihistamine prescription
            .AddMedicationOrder(FhirCode.Medications.Cetirizine, isChronic: true)

            // === YEAR 1: Asthma Development (40% in allergic children) ===
            .DelayMonths(12)
            .AddEncounter("Annual well-child visit")

            // PROBABILISTIC BRANCH: Allergic March - 40% develop asthma
            .AddProbabilisticBranch(
                0.40,  // 40% of children with allergic rhinitis develop asthma (Allergic March)

                // TRUE PATH: Asthma develops (Allergic March progression)
                new CompositeState
                {
                    Name = "Allergic_Asthma_Development",
                    States =
                    [
                        new DelayState { Exact = TimeSpan.FromDays(60) },
                        new EncounterState
                        {
                            Name = "Asthma_Symptoms_Evaluation",
                            EncounterClass = "ambulatory",
                            Reason = "Recurrent wheezing and cough - especially at night"
                        },
                        new ConditionOnsetState
                        {
                            Name = "Allergic_Asthma_Diagnosis",
                            Code = FhirCode.Conditions.Asthma,
                            Severity = 2,
                            AssignToAttribute = "asthma_condition"
                        },
                        ObservationState.PeakFlow(value: 160m),
                        MedicationOrderState.Albuterol(),
                        MedicationOrderState.FlucticasonePropionate(),

                        // Education: Allergen avoidance strategies
                        new SetAttributeState { AttributeName = "allergen_avoidance_education", Value = "completed" },

                        // Follow-up visits
                        DelayState.Months(3),
                        new EncounterState
                        {
                            Name = "Allergic_Asthma_Followup",
                            EncounterClass = "ambulatory",
                            Reason = "Asthma and allergy management follow-up"
                        },
                        ObservationState.PeakFlow()
                    ]
                },

                // FALSE PATH: Rhinitis only, no asthma progression (60%)
                new DelayState { Name = "Rhinitis_Only", Exact = TimeSpan.Zero }
            )

            .Build();
    }
}
