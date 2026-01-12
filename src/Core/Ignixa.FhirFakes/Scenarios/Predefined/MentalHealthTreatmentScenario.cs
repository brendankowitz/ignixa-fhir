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
/// Provides scenarios for mental health treatment including depression screening, diagnosis, and treatment.
/// Demonstrates realistic workflows for PHQ-9 screening, SSRI medication, psychotherapy, and follow-up monitoring.
/// </summary>
public static class MentalHealthTreatmentScenario
{
    /// <summary>
    /// Mental health specific FHIR codes for conditions, observations, medications, and procedures.
    /// </summary>
    private static class MentalHealthCodes
    {
        // Conditions
        public static readonly FhirCode MajorDepressiveDisorder = new(FhirCode.Systems.SnomedCt, "370143000", "Major depressive disorder");

        // Observations
        public static readonly FhirCode PHQ9 = new(FhirCode.Systems.Loinc, "44249-1", "PHQ-9 quick depression assessment panel");
        public static readonly FhirCode GAD7 = new(FhirCode.Systems.Loinc, "70274-6", "Generalized anxiety disorder 7 item (GAD-7)");
        public static readonly FhirCode SuicideRiskAssessment = new(FhirCode.Systems.Loinc, "73831-0", "Adult suicide risk assessment");

        // Medications - SSRIs
        public static readonly FhirCode Sertraline25mg = new(FhirCode.Systems.RxNorm, "312938", "Sertraline 25 MG Oral Tablet");
        public static readonly FhirCode Sertraline50mg = new(FhirCode.Systems.RxNorm, "312940", "Sertraline 50 MG Oral Tablet");
        public static readonly FhirCode Sertraline100mg = new(FhirCode.Systems.RxNorm, "312942", "Sertraline 100 MG Oral Tablet");
        public static readonly FhirCode Escitalopram10mg = new(FhirCode.Systems.RxNorm, "351249", "Escitalopram 10 MG Oral Tablet");
        public static readonly FhirCode Escitalopram20mg = new(FhirCode.Systems.RxNorm, "351250", "Escitalopram 20 MG Oral Tablet");
        public static readonly FhirCode Fluoxetine20mg = new(FhirCode.Systems.RxNorm, "310384", "Fluoxetine 20 MG Oral Capsule");
        public static readonly FhirCode Fluoxetine40mg = new(FhirCode.Systems.RxNorm, "310385", "Fluoxetine 40 MG Oral Capsule");

        // Procedures - Psychotherapy
        public static readonly FhirCode Psychotherapy = new(FhirCode.Systems.SnomedCt, "75516001", "Psychotherapy");
        public static readonly FhirCode CognitiveBehavioralTherapy = new(FhirCode.Systems.SnomedCt, "443728008", "Cognitive behavioral therapy");
        public static readonly FhirCode InterpersonalPsychotherapy = new(FhirCode.Systems.SnomedCt, "304823005", "Interpersonal psychotherapy");
    }

    /// <summary>
    /// Generates a standard depression screening and treatment scenario with probabilistic outcomes.
    ///
    /// Timeline:
    /// 1. Initial screening with PHQ-9 (probabilistic severity distribution)
    /// 2. If PHQ-9 >= 10: Major depressive disorder diagnosis
    /// 3. Additional assessments: GAD-7 and suicide risk
    /// 4. Week 1: Start SSRI medication + psychotherapy referral
    /// 5. Week 4: Follow-up with repeat PHQ-9
    /// 6. Week 8: Follow-up with medication adjustment if needed
    /// 7. Week 12: Final follow-up to assess treatment response
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 35).</param>
    /// <param name="gender">Patient gender (default: random).</param>
    /// <returns>A complete scenario context with depression screening and treatment journey.</returns>
    public static ScenarioContext DepressionScreeningAndTreatment(
        this IFhirSchemaProvider schemaProvider,
        int age = 35,
        string? gender = null)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Depression Screening and Treatment")
            .WithDescription("Patient screened for depression with PHQ-9, diagnosed with major depressive disorder, treated with SSRI and psychotherapy, monitored with follow-up assessments.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Setup mental health clinic and providers
            .AddOrganization("Wellness Mental Health Clinic", type: new FhirCode(FhirCode.Systems.SnomedCt, "35971002", "Mental health clinic"))
            .AddPractitioner(PractitionerState.Psychiatrist())
            .AddPractitioner(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "225727002", "Clinical psychologist"),
                Name = "Therapist_Psychologist"
            })

            // Initial screening visit
            .AddEncounter("Mental health screening - feeling down, loss of interest")

            // PHQ-9 screening with probabilistic severity distribution
            .AddState(ProbabilisticBranchState.Create(
                (0.40, CreatePHQ9Observation("minimal", 0, 4)),      // 40% minimal depression
                (0.30, CreatePHQ9Observation("mild", 5, 9)),         // 30% mild depression
                (0.20, CreatePHQ9Observation("moderate", 10, 14)),   // 20% moderate depression
                (0.07, CreatePHQ9Observation("moderately_severe", 15, 19)),  // 7% moderately severe
                (0.03, CreatePHQ9Observation("severe", 20, 27))      // 3% severe depression
            ));

        // Only proceed with diagnosis and treatment if PHQ-9 >= 10 (moderate or higher)
        return builder
            .AddState(new GuardState
            {
                Name = "Check_PHQ9_Threshold",
                ConditionType = ConditionType.AttributeValue,
                AttributeName = "phq9_score",
                Operator = ComparisonOperator.GreaterThanOrEqualTo,
                TargetValue = 10
            })

            // Diagnosis phase
            .AddConditionOnset(MentalHealthCodes.MajorDepressiveDisorder, severity: 2, assignToAttribute: "depression_condition")

            // Additional assessments
            .AddObservation(CreateGAD7Observation())
            .AddObservation(CreateSuicideRiskObservation())

            // Treatment initiation (1 week later)
            .DelayWeeks(1)
            .AddEncounter("Mental health treatment initiation")

            // Start first-line SSRI (probabilistic medication selection)
            .AddState(ProbabilisticBranchState.Create(
                (0.40, CreateSSRIMedication("sertraline")),      // 40% Sertraline
                (0.35, CreateSSRIMedication("escitalopram")),    // 35% Escitalopram
                (0.25, CreateSSRIMedication("fluoxetine"))       // 25% Fluoxetine
            ))

            // Psychotherapy referral
            .AddProcedure(new ProcedureState
            {
                Code = MentalHealthCodes.CognitiveBehavioralTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(50),
                Category = "therapeutic",
                Note = "Weekly CBT sessions scheduled. Focus on cognitive restructuring and behavioral activation.",
                ReasonConditionAttribute = "depression_condition"
            })

            // 4-week follow-up
            .DelayWeeks(4)
            .AddEncounter("Depression follow-up - 4 weeks")
            .AddProcedure(CreatePsychotherapySession())
            .AddState(CreateFollowUpPHQ9("4week"))

            // 8-week follow-up with medication adjustment if needed
            .DelayWeeks(4)
            .AddEncounter("Depression follow-up - 8 weeks")
            .AddProcedure(CreatePsychotherapySession())
            .AddState(CreateFollowUpPHQ9("8week"))

            // Check treatment response and adjust medication if needed
            .AddState(CreateMedicationAdjustmentLogic())

            // 12-week follow-up
            .DelayWeeks(4)
            .AddEncounter("Depression follow-up - 12 weeks")
            .AddProcedure(CreatePsychotherapySession())
            .AddState(CreateFollowUpPHQ9("12week"))
            .AddObservation(CreateGAD7Observation())

            .Build();
    }

    /// <summary>
    /// Generates a severe depression scenario with suicidal ideation requiring intensive treatment.
    ///
    /// Timeline:
    /// 1. Initial screening with high PHQ-9 score (20-27)
    /// 2. Positive suicide risk assessment
    /// 3. Immediate treatment initiation with SSRI + intensive therapy
    /// 4. Weekly follow-ups for 4 weeks
    /// 5. Then bi-weekly follow-ups
    /// 6. Close monitoring of suicidal ideation
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 28).</param>
    /// <param name="gender">Patient gender (default: random).</param>
    /// <returns>A complete scenario context with severe depression treatment journey.</returns>
    public static ScenarioContext SevereDepressionWithSuicidalIdeation(
        this IFhirSchemaProvider schemaProvider,
        int age = 28,
        string? gender = null)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Severe Depression with Suicidal Ideation")
            .WithDescription("Patient presents with severe depression (PHQ-9: 20-27) and suicidal ideation, requiring intensive treatment and close monitoring.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Setup crisis intervention team
            .AddOrganization("Crisis Mental Health Center", type: new FhirCode(FhirCode.Systems.SnomedCt, "35971002", "Mental health clinic"))
            .AddPractitioner(PractitionerState.Psychiatrist())
            .AddPractitioner(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "225727002", "Clinical psychologist"),
                Name = "Crisis_Therapist"
            })

            // Initial crisis screening
            .AddEncounter("Mental health crisis - severe depression and suicidal ideation")

            // Severe PHQ-9 score
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 20,
                ValueRangeMax = 27,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Severe"
            })
            .SetAttribute("phq9_score", 23)
            .SetAttribute("phq9_severity", "severe")

            // High suicide risk
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.SuicideRiskAssessment,
                Value = 8,
                ValueRangeMin = 7,
                ValueRangeMax = 10,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "SuicideRisk_High"
            })
            .SetAttribute("suicide_risk", "high")

            // GAD-7 (often comorbid anxiety)
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.GAD7,
                ValueRangeMin = 15,
                ValueRangeMax = 21,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "GAD7_Severe"
            })

            // Immediate diagnosis
            .AddConditionOnset(MentalHealthCodes.MajorDepressiveDisorder, severity: 4, assignToAttribute: "depression_condition")

            // Same-day treatment initiation
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = MentalHealthCodes.Escitalopram10mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                ReasonConditionAttribute = "depression_condition",
                DosageInstructions = "Take 10mg once daily. May increase to 20mg after 4 weeks."
            })

            // Safety plan and crisis resources
            .AddProcedure(new ProcedureState
            {
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "225334002", "Crisis intervention"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                Note = "Safety plan created. Crisis hotline numbers provided. Emergency contacts verified. Patient agrees to return to ED if suicidal thoughts worsen.",
                ReasonConditionAttribute = "depression_condition"
            })

            // Intensive psychotherapy
            .AddProcedure(new ProcedureState
            {
                Code = MentalHealthCodes.CognitiveBehavioralTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                Note = "Initial CBT session. Focus on safety planning, identifying triggers, and coping strategies. Scheduled for twice-weekly sessions.",
                ReasonConditionAttribute = "depression_condition"
            })

            // Week 1 follow-up
            .DelayWeeks(1)
            .AddEncounter("Depression crisis follow-up - 1 week")
            .AddObservation(CreateSuicideRiskObservation())
            .AddProcedure(CreatePsychotherapySession())

            // Week 2 follow-up
            .DelayWeeks(1)
            .AddEncounter("Depression crisis follow-up - 2 weeks")
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 18,
                ValueRangeMax = 22,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Week2"
            })
            .AddProcedure(CreatePsychotherapySession())

            // Week 3 follow-up
            .DelayWeeks(1)
            .AddEncounter("Depression crisis follow-up - 3 weeks")
            .AddObservation(CreateSuicideRiskObservation())
            .AddProcedure(CreatePsychotherapySession())

            // Week 4 follow-up
            .DelayWeeks(1)
            .AddEncounter("Depression crisis follow-up - 4 weeks")
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 14,
                ValueRangeMax = 18,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Week4"
            })
            .AddProcedure(CreatePsychotherapySession())

            // Week 6 follow-up (bi-weekly now)
            .DelayWeeks(2)
            .AddEncounter("Depression follow-up - 6 weeks")
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 10,
                ValueRangeMax = 15,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Week6"
            })
            .AddObservation(CreateGAD7Observation())
            .AddProcedure(CreatePsychotherapySession())

            // Week 8 follow-up
            .DelayWeeks(2)
            .AddEncounter("Depression follow-up - 8 weeks")
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 7,
                ValueRangeMax = 12,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Week8"
            })
            .AddProcedure(CreatePsychotherapySession())

            // Week 12 follow-up
            .DelayWeeks(4)
            .AddEncounter("Depression follow-up - 12 weeks")
            .AddObservation(new ObservationState
            {
                Code = MentalHealthCodes.PHQ9,
                ValueRangeMin = 5,
                ValueRangeMax = 10,
                Unit = "{score}",
                UnitCode = "{score}",
                Name = "PHQ9_Week12"
            })
            .AddObservation(CreateGAD7Observation())
            .AddObservation(CreateSuicideRiskObservation())

            .Build();
    }

    #region Helper Methods

    /// <summary>
    /// Creates a PHQ-9 observation with a score in the specified range and sets context attributes.
    /// </summary>
    private static ScenarioState CreatePHQ9Observation(string severity, int minScore, int maxScore)
    {
        return new CallSubScenarioState
        {
            Name = $"PHQ9_{severity}",
            SubScenario = builder => builder
                .AddObservation(new ObservationState
                {
                    Code = MentalHealthCodes.PHQ9,
                    ValueRangeMin = minScore,
                    ValueRangeMax = maxScore,
                    Unit = "{score}",
                    UnitCode = "{score}",
                    Name = $"PHQ9_Score_{severity}"
                })
                .SetAttribute("phq9_score", (minScore + maxScore) / 2)
                .SetAttribute("phq9_severity", severity)
        };
    }

    /// <summary>
    /// Creates a GAD-7 anxiety screening observation.
    /// </summary>
    private static ObservationState CreateGAD7Observation()
    {
        return new ObservationState
        {
            Code = MentalHealthCodes.GAD7,
            ValueRangeMin = 5,
            ValueRangeMax = 15,
            Unit = "{score}",
            UnitCode = "{score}",
            Name = "GAD7_Screening"
        };
    }

    /// <summary>
    /// Creates a suicide risk assessment observation.
    /// </summary>
    private static ObservationState CreateSuicideRiskObservation()
    {
        return new ObservationState
        {
            Code = MentalHealthCodes.SuicideRiskAssessment,
            ValueRangeMin = 0,
            ValueRangeMax = 5,
            Unit = "{score}",
            UnitCode = "{score}",
            Name = "SuicideRisk_Assessment"
        };
    }

    /// <summary>
    /// Creates an SSRI medication order based on the medication type.
    /// </summary>
    private static ScenarioState CreateSSRIMedication(string medicationType)
    {
        return medicationType switch
        {
            "sertraline" => new MedicationOrderState
            {
                Code = MentalHealthCodes.Sertraline50mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                ReasonCode = MentalHealthCodes.MajorDepressiveDisorder,
                DosageInstructions = "Take 50mg once daily. May increase to 100mg after 4-6 weeks if needed."
            },
            "escitalopram" => new MedicationOrderState
            {
                Code = MentalHealthCodes.Escitalopram10mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                ReasonCode = MentalHealthCodes.MajorDepressiveDisorder,
                DosageInstructions = "Take 10mg once daily. May increase to 20mg after 4-6 weeks if needed."
            },
            "fluoxetine" => new MedicationOrderState
            {
                Code = MentalHealthCodes.Fluoxetine20mg,
                IsChronic = true,
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "capsule",
                ReasonCode = MentalHealthCodes.MajorDepressiveDisorder,
                DosageInstructions = "Take 20mg once daily. May increase to 40mg after 4-6 weeks if needed."
            },
            _ => throw new ArgumentException($"Unknown SSRI medication type: {medicationType}", nameof(medicationType))
        };
    }

    /// <summary>
    /// Creates a psychotherapy session procedure.
    /// </summary>
    private static ProcedureState CreatePsychotherapySession()
    {
        return new ProcedureState
        {
            Code = MentalHealthCodes.CognitiveBehavioralTherapy,
            Status = "completed",
            Duration = TimeSpan.FromMinutes(50),
            Category = "therapeutic",
            Note = "Continuing CBT. Reviewed homework assignments, worked on identifying negative thought patterns, practiced relaxation techniques."
        };
    }

    /// <summary>
    /// Creates a follow-up PHQ-9 observation with probabilistic improvement.
    /// </summary>
    private static ScenarioState CreateFollowUpPHQ9(string timepoint)
    {
        return new CallSubScenarioState
        {
            Name = $"PHQ9_FollowUp_{timepoint}",
            SubScenario = builder =>
            {
                // Get baseline PHQ-9 score
                var baselineScore = builder.SchemaProvider.GetType().Name == "IFhirSchemaProvider" ? 12 : 12;

                return builder.AddState(ProbabilisticBranchState.Create(
                    // 60% show good improvement (50% reduction)
                    (0.60, new CallSubScenarioState
                    {
                        Name = "PHQ9_Improved",
                        SubScenario = b => b
                            .AddObservation(new ObservationState
                            {
                                Code = MentalHealthCodes.PHQ9,
                                ValueRangeMin = 4,
                                ValueRangeMax = 8,
                                Unit = "{score}",
                                UnitCode = "{score}",
                                Name = $"PHQ9_{timepoint}_improved"
                            })
                            .SetAttribute($"phq9_improvement_{timepoint}", "good")
                    }),
                    // 30% show partial improvement (25% reduction)
                    (0.30, new CallSubScenarioState
                    {
                        Name = "PHQ9_Partial",
                        SubScenario = b => b
                            .AddObservation(new ObservationState
                            {
                                Code = MentalHealthCodes.PHQ9,
                                ValueRangeMin = 8,
                                ValueRangeMax = 12,
                                Unit = "{score}",
                                UnitCode = "{score}",
                                Name = $"PHQ9_{timepoint}_partial"
                            })
                            .SetAttribute($"phq9_improvement_{timepoint}", "partial")
                    }),
                    // 10% show minimal/no improvement
                    (0.10, new CallSubScenarioState
                    {
                        Name = "PHQ9_NoResponse",
                        SubScenario = b => b
                            .AddObservation(new ObservationState
                            {
                                Code = MentalHealthCodes.PHQ9,
                                ValueRangeMin = 11,
                                ValueRangeMax = 15,
                                Unit = "{score}",
                                UnitCode = "{score}",
                                Name = $"PHQ9_{timepoint}_no_response"
                            })
                            .SetAttribute($"phq9_improvement_{timepoint}", "none")
                    })
                ));
            }
        };
    }

    /// <summary>
    /// Creates medication adjustment logic based on treatment response.
    /// </summary>
    private static ScenarioState CreateMedicationAdjustmentLogic()
    {
        return new CallSubScenarioState
        {
            Name = "MedicationAdjustment_Check",
            SubScenario = builder =>
            {
                // Check if patient has partial or no response at 8 weeks
                if (builder.GetStates().Any())
                {
                    // Add probabilistic medication adjustment
                    return builder.AddState(ProbabilisticBranchState.Create(
                        // 70% continue current medication
                        (0.70, new CallSubScenarioState
                        {
                            Name = "Continue_Current_Medication",
                            SubScenario = b => b
                        }),
                        // 20% increase dose
                        (0.20, new CallSubScenarioState
                        {
                            Name = "Increase_Medication_Dose",
                            SubScenario = b => b
                                .AddMedicationOrder(new MedicationOrderState
                                {
                                    Code = MentalHealthCodes.Sertraline100mg,
                                    IsChronic = true,
                                    Frequency = "daily",
                                    DoseQuantity = 1,
                                    DoseUnit = "tablet",
                                    ReasonCode = MentalHealthCodes.MajorDepressiveDisorder,
                                    DosageInstructions = "Increased to 100mg once daily for improved symptom control."
                                })
                        }),
                        // 10% switch medication
                        (0.10, new CallSubScenarioState
                        {
                            Name = "Switch_Medication",
                            SubScenario = b => b
                                .AddMedicationOrder(new MedicationOrderState
                                {
                                    Code = MentalHealthCodes.Escitalopram20mg,
                                    IsChronic = true,
                                    Frequency = "daily",
                                    DoseQuantity = 1,
                                    DoseUnit = "tablet",
                                    ReasonCode = MentalHealthCodes.MajorDepressiveDisorder,
                                    DosageInstructions = "Switched to Escitalopram 20mg once daily due to inadequate response to previous medication."
                                })
                        })
                    ));
                }

                return builder;
            }
        };
    }

    #endregion
}
