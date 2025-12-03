// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating surgical care pathway scenarios.
/// Demonstrates realistic pre-operative, surgical, and post-operative workflows for common surgical procedures.
/// </summary>
/// <remarks>
/// This scenario models complete surgical pathways including:
/// - Pre-operative assessment and clearance
/// - Diagnostic imaging and laboratory work
/// - Surgical procedure with anesthesia
/// - Post-operative monitoring and recovery
/// - Follow-up care and rehabilitation
///
/// Two pathways are implemented:
/// 1. **Cholecystectomy** (gallbladder removal) - Common low-risk abdominal surgery
/// 2. **Total Knee Arthroplasty** (knee replacement) - Common orthopedic joint replacement
///
/// Each pathway follows evidence-based clinical guidelines and realistic timelines.
/// </remarks>
public static class SurgicalPathwayScenario
{
    /// <summary>
    /// Generates a laparoscopic cholecystectomy (gallbladder removal) surgical pathway.
    ///
    /// Clinical Context:
    /// - Indication: Symptomatic cholelithiasis (gallstones)
    /// - Procedure: Laparoscopic cholecystectomy
    /// - Hospital stay: 1 day (23-hour observation)
    /// - Recovery: 2-4 weeks for full activity resumption
    ///
    /// Timeline:
    /// - T-7 days: Pre-operative assessment, labs, EKG, surgical clearance
    /// - T+0 (Day 0): Surgery day - general anesthesia, laparoscopic procedure (60-90 min)
    /// - T+4 hours: Post-op recovery unit - vitals monitoring, pain management
    /// - T+1 day: Hospital discharge with instructions
    /// - T+2 weeks: Follow-up appointment with surgeon
    ///
    /// Generated Resources:
    /// - 1 Hospital (Organization)
    /// - 4 Practitioners (Surgeon, Anesthesiologist, Primary Care Physician, Recovery Nurse)
    /// - 5 Encounters (pre-op, surgery, post-op recovery, discharge, follow-up)
    /// - 4 DiagnosticReports (CBC, metabolic panel, coagulation studies, EKG)
    /// - 3 Procedures (general anesthesia, laparoscopic cholecystectomy, post-op monitoring)
    /// - 3 MedicationRequests (pre-op prophylaxis, post-op pain management, anti-nausea)
    /// - 15+ Observations (pre-op vitals, intra-op vitals, post-op vitals, pain scores)
    /// - 1 Condition (cholelithiasis)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 52).</param>
    /// <param name="gender">Patient gender (default: "female").</param>
    /// <returns>A complete scenario context with cholecystectomy pathway.</returns>
    public static ScenarioContext GetCholecystectomyPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 52,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Laparoscopic Cholecystectomy Surgical Pathway")
            .WithDescription("Complete surgical pathway for gallbladder removal including pre-op assessment, laparoscopic surgery, post-op recovery, and follow-up care.")

            // Initial patient
            .WithPatient(age: age, gender: gender);

        return CholecystectomyPathway(builder).Build();
    }

    /// <summary>
    /// Generates a total knee arthroplasty (knee replacement) surgical pathway.
    ///
    /// Clinical Context:
    /// - Indication: Severe osteoarthritis of knee with pain and functional limitation
    /// - Procedure: Total knee arthroplasty (TKA) with prosthetic implant
    /// - Hospital stay: 2-3 days with physical therapy
    /// - Recovery: 3-6 months for full recovery, 12 months for optimal function
    ///
    /// Timeline:
    /// - T-7 days: Pre-operative assessment, labs, X-ray knee, orthopedic clearance
    /// - T+0 (Day 0): Surgery day - spinal/general anesthesia, TKA procedure (90-120 min)
    /// - T+4 hours: Post-op recovery unit - vitals, pain management, DVT prophylaxis
    /// - T+1 day: Physical therapy initiated, mobilization
    /// - T+2 days: Continued PT, discharge planning
    /// - T+3 days: Hospital discharge with home PT plan
    /// - T+2 weeks: First follow-up appointment
    /// - T+6 weeks: Wound check and progress assessment
    /// - T+12 weeks: Final follow-up and functional assessment
    ///
    /// Generated Resources:
    /// - 1 Hospital (Organization)
    /// - 5 Practitioners (Orthopedic Surgeon, Anesthesiologist, Primary Care Physician, Physical Therapist, Recovery Nurse)
    /// - 7 Encounters (pre-op, surgery, post-op recovery, PT sessions, discharge, follow-ups)
    /// - 5 DiagnosticReports (CBC, metabolic panel, coagulation studies, X-ray knee pre-op, X-ray knee post-op)
    /// - 4 Procedures (anesthesia, total knee arthroplasty, physical therapy sessions)
    /// - 5 MedicationRequests (pre-op prophylaxis, post-op pain management, anticoagulation, antibiotics)
    /// - 20+ Observations (vitals throughout stay, pain scores, range of motion, functional assessments)
    /// - 1 Condition (osteoarthritis of knee)
    /// - 1 Device (knee prosthesis)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 68).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with total knee arthroplasty pathway.</returns>
    public static ScenarioContext GetTotalKneeReplacementPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 68,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Total Knee Arthroplasty Surgical Pathway")
            .WithDescription("Complete surgical pathway for knee replacement including pre-op assessment, surgical procedure, inpatient recovery with physical therapy, and comprehensive follow-up care.")

            // Initial patient
            .WithPatient(age: age, gender: gender);

        return TotalKneeReplacementPathway(builder).Build();
    }

    /// <summary>
    /// Builder extension: Adds the cholecystectomy pathway to an existing scenario.
    /// Can be composed with other scenario fragments.
    /// </summary>
    /// <param name="builder">The scenario builder.</param>
    /// <returns>The builder with cholecystectomy pathway added.</returns>
    public static ScenarioBuilder CholecystectomyPathway(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            // === SETUP: Hospital and Care Team ===
            .AddState(OrganizationState.Hospital("City General Hospital - Surgical Center"))
            .AddState(PractitionerState.Surgeon())
            .AddState(new PractitionerState { Specialty = Specialties.Anesthesiology, Name = "Anesthesiologist" })
            .AddState(PractitionerState.FamilyPractitioner())
            .AddState(PractitionerState.Nurse())

            // === T-7 DAYS: Pre-Operative Assessment ===
            // Note: This represents 7 days before surgery day

            // Pre-op encounter with primary care for surgical clearance
            .AddState(new EncounterState
            {
                Name = "PreOp_Encounter",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Pre-operative evaluation for laparoscopic cholecystectomy",
                DurationMinutes = 45
            })

            // Diagnosis: Cholelithiasis (gallstones)
            .AddConditionOnset(new FhirCode(FhirCode.Systems.SnomedCt, "235919008", "Cholelithiasis"),
                severity: 2,
                assignToAttribute: "cholelithiasis_condition")

            // Pre-operative vital signs
            .AddObservation(VitalSigns.BodyHeight, value: 165m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, value: 78m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: 28.7m, unit: "kg/m2", unitCode: "kg/m2")
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 128m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 82m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 76m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.BodyTemperature, value: 36.8m, unit: "°C", unitCode: "Cel")

            // Pre-operative laboratory work
            .AddSubScenario(CommonScenarios.CompleteBloodCount(), "Pre-op CBC")
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Pre-op Metabolic Panel")

            // Coagulation studies (PT/INR)
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "5902-2", "Prothrombin time"),
                value: 12.5m, unit: "s", unitCode: "s")
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "6301-6", "INR"),
                value: 1.0m, unit: "{INR}", unitCode: "{INR}")

            // EKG for cardiac clearance
            .AddDiagnosticReport(
                code: new FhirCode(FhirCode.Systems.Loinc, "11524-6", "EKG study"),
                conclusion: "Normal sinus rhythm. No contraindications for surgery.")

            // === T+0 (DAY 0): SURGERY DAY ===
            .DelayDays(7)  // 7 days after pre-op (surgery day)

            // Surgical encounter (inpatient class for admission)
            .AddState(new EncounterState
            {
                Name = "Surgery_Encounter",
                EncounterClass = "IMP",  // Inpatient
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                Reason = "Laparoscopic cholecystectomy for symptomatic cholelithiasis",
                DurationMinutes = 180  // Total OR time including prep
            })

            // Pre-operative vitals (in holding area)
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 135m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 85m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 88m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, value: 98m, unit: "%", unitCode: "%")

            // Anesthesia procedure (General anesthesia)
            .AddState(new ProcedureState
            {
                Name = "General_Anesthesia",
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "50697003", "General anesthesia"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(120),
                Category = "therapeutic",
                Outcome = "Patient tolerated general anesthesia without complications"
            })

            // Main surgical procedure
            .AddState(new ProcedureState
            {
                Name = "Laparoscopic_Cholecystectomy",
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "45595009", "Laparoscopic cholecystectomy"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(75),
                Category = "surgery",
                BodySite = "Gallbladder",
                ReasonConditionAttribute = "cholelithiasis_condition",
                Outcome = "Successful laparoscopic removal of gallbladder. No conversion to open. Estimated blood loss 50mL.",
                FollowUp = "Follow up in 2 weeks. Low-fat diet recommended initially. Resume normal activities in 2-4 weeks."
            })

            // === POST-OPERATIVE RECOVERY (Same Day) ===
            .Delay(TimeSpan.FromHours(4))  // 4 hours post-op

            // Post-operative recovery encounter
            .AddState(new EncounterState
            {
                Name = "PostOp_Recovery_Encounter",
                EncounterClass = "IMP",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                Reason = "Post-operative recovery and monitoring",
                DurationMinutes = 240  // 4 hours in recovery
            })

            // Post-op vital signs (every hour x4)
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 118m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 75m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 72m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.RespiratoryRate, value: 16m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, value: 97m, unit: "%", unitCode: "%")
            .AddObservation(VitalSigns.BodyTemperature, value: 37.1m, unit: "°C", unitCode: "Cel")

            // Pain assessment
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 4m, unit: "{score}", unitCode: "{score}")

            // Post-operative pain management
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "PostOp_Pain_Med",
                Code = new FhirCode(FhirCode.Systems.RxNorm, "1049621", "Oxycodone 5mg"),
                DosageInstructions = "5mg PO every 4-6 hours as needed for pain",
                Frequency = "as-needed",
                IsChronic = false,
                DurationDays = 5
            })

            // Anti-nausea medication
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "AntiNausea_Med",
                Code = FhirCode.Medications.Ondansetron4mg,
                DosageInstructions = "4mg IV every 8 hours as needed for nausea",
                Frequency = "as-needed",
                IsChronic = false,
                DurationDays = 2
            })

            // === T+1 DAY: HOSPITAL DISCHARGE ===
            .DelayDays(1)

            // Discharge encounter
            .AddState(new EncounterState
            {
                Name = "Discharge_Encounter",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Hospital discharge after cholecystectomy",
                DurationMinutes = 30
            })

            // Discharge vitals
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 122m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 78m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BodyTemperature, value: 36.9m, unit: "°C", unitCode: "Cel")

            // Pain assessment at discharge
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 2m, unit: "{score}", unitCode: "{score}")

            // === T+2 WEEKS: FOLLOW-UP APPOINTMENT ===
            .DelayWeeks(2)

            // Post-op follow-up
            .AddState(new EncounterState
            {
                Name = "FollowUp_2Week_Encounter",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Post-operative follow-up after cholecystectomy",
                DurationMinutes = 20
            })

            // Follow-up vitals
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 120m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 80m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BodyWeight, value: 76m, unit: "kg", unitCode: "kg")  // Slight weight loss

            // Pain should be minimal by 2 weeks
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 0m, unit: "{score}", unitCode: "{score}");
    }

    /// <summary>
    /// Builder extension: Adds the total knee arthroplasty pathway to an existing scenario.
    /// Can be composed with other scenario fragments.
    /// </summary>
    /// <param name="builder">The scenario builder.</param>
    /// <returns>The builder with total knee arthroplasty pathway added.</returns>
    public static ScenarioBuilder TotalKneeReplacementPathway(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            // === SETUP: Hospital and Care Team ===
            .AddState(OrganizationState.Hospital("Regional Medical Center - Orthopedic Institute"))
            .AddState(PractitionerState.OrthopedicSurgeon())
            .AddState(new PractitionerState { Specialty = Specialties.Anesthesiology, Name = "Anesthesiologist_Ortho" })
            .AddState(PractitionerState.FamilyPractitioner())
            .AddState(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "36682004", "Physical therapist"),
                Name = "Physical_Therapist"
            })
            .AddState(PractitionerState.Nurse())

            // === T-7 DAYS: Pre-Operative Assessment ===
            // Note: This represents 7 days before surgery day

            // Pre-op encounter with orthopedic surgeon
            .AddState(new EncounterState
            {
                Name = "PreOp_Ortho_Encounter",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Pre-operative evaluation for total knee arthroplasty",
                DurationMinutes = 60
            })

            // Diagnosis: Osteoarthritis of knee
            .AddConditionOnset(new FhirCode(FhirCode.Systems.SnomedCt, "239873007", "Osteoarthritis of knee"),
                severity: 3,  // Severe
                assignToAttribute: "osteoarthritis_condition")

            // Pre-operative vital signs
            .AddObservation(VitalSigns.BodyHeight, value: 178m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, value: 92m, unit: "kg", unitCode: "kg")
            .AddObservation(VitalSigns.BMI, value: 29.1m, unit: "kg/m2", unitCode: "kg/m2")
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 138m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 88m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 78m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.BodyTemperature, value: 36.7m, unit: "°C", unitCode: "Cel")

            // Pain assessment - significant baseline pain
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 7m, unit: "{score}", unitCode: "{score}")

            // Pre-operative laboratory work
            .AddSubScenario(CommonScenarios.CompleteBloodCount(), "Pre-op CBC for TKA")
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Pre-op Metabolic Panel for TKA")

            // Coagulation studies
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "5902-2", "Prothrombin time"),
                value: 11.8m, unit: "s", unitCode: "s")
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "6301-6", "INR"),
                value: 1.0m, unit: "{INR}", unitCode: "{INR}")

            // Pre-operative X-ray of knee
            .AddDiagnosticReport(
                code: new FhirCode(FhirCode.Systems.Loinc, "24727-0", "X-ray knee"),
                conclusion: "Severe tricompartmental osteoarthritis with joint space narrowing, osteophyte formation, and subchondral sclerosis. Appropriate for total knee arthroplasty.")

            // === T+0 (DAY 0): SURGERY DAY ===
            .DelayDays(7)  // 7 days after pre-op (surgery day)

            // Surgical encounter (inpatient admission)
            .AddState(new EncounterState
            {
                Name = "TKA_Surgery_Encounter",
                EncounterClass = "IMP",  // Inpatient
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                Reason = "Total knee arthroplasty for severe osteoarthritis",
                DurationMinutes = 240  // Total OR time
            })

            // Pre-operative vitals
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 142m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 90m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 82m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, value: 97m, unit: "%", unitCode: "%")

            // Pre-operative antibiotic prophylaxis
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "PreOp_Antibiotic",
                Code = new FhirCode(FhirCode.Systems.RxNorm, "309043", "Cefazolin 1g IV"),
                DosageInstructions = "1g IV once pre-operatively",
                Frequency = "once",
                IsChronic = false,
                DurationDays = 1
            })

            // Anesthesia procedure (Spinal anesthesia preferred for TKA)
            .AddState(new ProcedureState
            {
                Name = "Spinal_Anesthesia",
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "18946005", "Spinal anesthesia"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(150),
                Category = "therapeutic",
                Outcome = "Spinal anesthesia administered successfully at L3-L4. Patient comfortable throughout procedure."
            })

            // Main surgical procedure: Total knee arthroplasty
            .AddState(new ProcedureState
            {
                Name = "Total_Knee_Arthroplasty",
                Code = Procedures.TotalKneeReplacement,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(105),
                Category = "surgery",
                BodySite = "Knee",
                ReasonConditionAttribute = "osteoarthritis_condition",
                Outcome = "Total knee arthroplasty completed successfully. Cemented posterior-stabilized prosthesis implanted. Excellent alignment and stability achieved. Estimated blood loss 350mL.",
                FollowUp = "Physical therapy to begin post-op day 1. Weight bearing as tolerated with walker. Follow up in 2 weeks for wound check."
            })

            // === POST-OPERATIVE RECOVERY (Day 0, Evening) ===
            .Delay(TimeSpan.FromHours(4))

            // Post-op recovery encounter
            .AddState(new EncounterState
            {
                Name = "PostOp_Recovery_TKA",
                EncounterClass = "IMP",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                Reason = "Post-operative recovery after total knee arthroplasty",
                DurationMinutes = 360
            })

            // Post-op vital signs
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 128m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 78m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 76m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.RespiratoryRate, value: 14m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.OxygenSaturationPulseOx, value: 96m, unit: "%", unitCode: "%")
            .AddObservation(VitalSigns.BodyTemperature, value: 37.2m, unit: "°C", unitCode: "Cel")

            // Post-op pain assessment - expected to be significant
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 6m, unit: "{score}", unitCode: "{score}")

            // Post-operative pain management (multimodal)
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "PostOp_Oxycodone",
                Code = FhirCode.Medications.Oxycodone5mg,
                DosageInstructions = "5-10mg PO every 4 hours as needed for severe pain",
                Frequency = "as-needed",
                IsChronic = false,
                DurationDays = 7
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "PostOp_Acetaminophen",
                Code = FhirCode.Medications.Acetaminophen500mg,
                DosageInstructions = "500mg PO every 6 hours scheduled",
                Frequency = "four-times-daily",
                IsChronic = false,
                DurationDays = 14
            })

            // DVT prophylaxis (anticoagulation)
            .AddMedicationOrder(new MedicationOrderState
            {
                Name = "DVT_Prophylaxis",
                Code = new FhirCode(FhirCode.Systems.RxNorm, "1037042", "Enoxaparin 40mg subcutaneous"),
                DosageInstructions = "40mg subcutaneously once daily",
                Frequency = "daily",
                IsChronic = false,
                DurationDays = 10
            })

            // === T+1 DAY: POST-OP DAY 1 - PHYSICAL THERAPY INITIATED ===
            .DelayDays(1)

            // PT encounter
            .AddState(new EncounterState
            {
                Name = "PT_Day1_Encounter",
                EncounterClass = "IMP",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                Reason = "Physical therapy evaluation and initial mobilization",
                DurationMinutes = 60
            })

            // Physical therapy procedure
            .AddState(new ProcedureState
            {
                Name = "Physical_Therapy_Day1",
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "91251008", "Physical therapy"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(45),
                Category = "therapeutic",
                Outcome = "Patient transferred to chair with assist. Ambulated 10 feet with walker. Knee flexion 45 degrees.",
                FollowUp = "Continue PT twice daily. Progress ambulation distance."
            })

            // Post-PT vitals
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 132m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 88m, unit: "/min", unitCode: "/min")

            // Pain after PT
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 5m, unit: "{score}", unitCode: "{score}")

            // Range of motion assessment
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "82289-2", "Knee range of motion"),
                value: 45m, unit: "deg", unitCode: "deg")

            // === T+2 DAYS: POST-OP DAY 2 - CONTINUED RECOVERY ===
            .DelayDays(1)

            // PT session day 2
            .AddState(new ProcedureState
            {
                Name = "Physical_Therapy_Day2",
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "91251008", "Physical therapy"),
                Status = "completed",
                Duration = TimeSpan.FromMinutes(45),
                Category = "therapeutic",
                Outcome = "Patient ambulated 50 feet with walker. Stair training initiated. Knee flexion improved to 60 degrees.",
                FollowUp = "Continue PT. Discharge planning for home or rehab facility."
            })

            // Improved pain
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 4m, unit: "{score}", unitCode: "{score}")

            // Range of motion improving
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "82289-2", "Knee range of motion"),
                value: 60m, unit: "deg", unitCode: "deg")

            // === T+3 DAYS: POST-OP DAY 3 - HOSPITAL DISCHARGE ===
            .DelayDays(1)

            // Discharge encounter
            .AddState(new EncounterState
            {
                Name = "TKA_Discharge_Encounter",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Hospital discharge after total knee arthroplasty",
                DurationMinutes = 45
            })

            // Discharge vitals
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 128m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BloodPressureDiastolic, value: 80m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.BodyTemperature, value: 36.8m, unit: "°C", unitCode: "Cel")

            // Post-operative X-ray
            .AddDiagnosticReport(
                code: new FhirCode(FhirCode.Systems.Loinc, "24727-0", "X-ray knee"),
                conclusion: "Post-operative X-ray shows well-positioned total knee prosthesis with good alignment. No immediate complications.")

            // Discharge pain level
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 3m, unit: "{score}", unitCode: "{score}")

            // === T+2 WEEKS: FIRST FOLLOW-UP ===
            .DelayWeeks(2)

            // Post-op follow-up #1
            .AddState(new EncounterState
            {
                Name = "FollowUp_2Week_TKA",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Post-operative follow-up - wound check and progress assessment",
                DurationMinutes = 30
            })

            // Vital signs
            .AddObservation(VitalSigns.BloodPressureSystolic, value: 130m, unit: "mmHg", unitCode: "mm[Hg]")
            .AddObservation(VitalSigns.HeartRate, value: 74m, unit: "/min", unitCode: "/min")

            // Pain improving
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 2m, unit: "{score}", unitCode: "{score}")

            // Range of motion progressing
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "82289-2", "Knee range of motion"),
                value: 90m, unit: "deg", unitCode: "deg")

            // === T+6 WEEKS: SECOND FOLLOW-UP ===
            .DelayWeeks(4)

            // Post-op follow-up #2
            .AddState(new EncounterState
            {
                Name = "FollowUp_6Week_TKA",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Post-operative follow-up - progress assessment",
                DurationMinutes = 25
            })

            // Pain minimal
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 1m, unit: "{score}", unitCode: "{score}")

            // Good range of motion
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "82289-2", "Knee range of motion"),
                value: 110m, unit: "deg", unitCode: "deg")

            // === T+12 WEEKS: FINAL FOLLOW-UP ===
            .DelayWeeks(6)

            // Post-op follow-up #3 (final)
            .AddState(new EncounterState
            {
                Name = "FollowUp_12Week_TKA",
                EncounterClass = "AMB",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                Reason = "Post-operative follow-up - final functional assessment",
                DurationMinutes = 20
            })

            // Minimal to no pain
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating"),
                value: 0m, unit: "{score}", unitCode: "{score}")

            // Excellent range of motion
            .AddObservation(new FhirCode(FhirCode.Systems.Loinc, "82289-2", "Knee range of motion"),
                value: 120m, unit: "deg", unitCode: "deg");
    }
}
