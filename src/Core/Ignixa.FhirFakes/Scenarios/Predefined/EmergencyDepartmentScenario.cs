// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating Emergency Department (ED) visit scenarios.
/// Demonstrates realistic ED workflows including triage, diagnostics, treatment, and disposition.
/// </summary>
/// <remarks>
/// Emergency Department visits follow a structured workflow:
/// <list type="number">
///   <item><description><b>Arrival</b>: Patient presents with chief complaint</description></item>
///   <item><description><b>Triage</b>: Vital signs and ESI (Emergency Severity Index) scoring</description></item>
///   <item><description><b>Assessment</b>: Physician evaluation and initial orders</description></item>
///   <item><description><b>Diagnostics</b>: Labs, imaging based on presenting complaint</description></item>
///   <item><description><b>Treatment</b>: Medications, procedures, interventions</description></item>
///   <item><description><b>Disposition</b>: Discharge, admission, or transfer</description></item>
/// </list>
///
/// ESI (Emergency Severity Index) scoring:
/// <list type="bullet">
///   <item><description>ESI 1: Immediate (resuscitation required)</description></item>
///   <item><description>ESI 2: Emergent (high risk, vital sign abnormalities)</description></item>
///   <item><description>ESI 3: Urgent (multiple resources needed)</description></item>
///   <item><description>ESI 4: Less urgent (one resource needed)</description></item>
///   <item><description>ESI 5: Non-urgent (no resources needed)</description></item>
/// </list>
/// </remarks>
public static class EmergencyDepartmentScenario
{
    #region Scenario Extension Methods

    /// <summary>
    /// Generates an Emergency Department visit for chest pain with cardiac workup.
    ///
    /// Demonstrates:
    /// - <b>ESI Level 2</b>: High-risk complaint requiring immediate evaluation
    /// - <b>Cardiac workup</b>: EKG, troponin (serial), chest X-ray
    /// - <b>Probabilistic disposition</b>: Discharge (65%), Admission (30%), Transfer (5%)
    ///
    /// Clinical Pathway:
    /// 1. Patient presents with chest pain, shortness of breath
    /// 2. Triage with abnormal vitals (tachycardia, hypertension)
    /// 3. EKG performed within 10 minutes
    /// 4. Initial troponin and CBC drawn
    /// 5. Chest X-ray performed
    /// 6. Repeat troponin at 3 hours
    /// 7. Treatment based on findings
    /// 8. Disposition decision
    ///
    /// Generated Resources:
    /// - 1 Organization (Emergency Department)
    /// - 1-2 Practitioners (Emergency Physician, possibly Cardiologist)
    /// - 1 Emergency Encounter (2-6 hours duration)
    /// - 7 Vital Sign Observations (BP, HR, RR, Temp, SpO2, Pain, Weight)
    /// - 1-2 DiagnosticReports (EKG, Chest X-ray)
    /// - 1-2 Lab panels (Troponin, BMP)
    /// - 1-2 MedicationRequests (aspirin, nitroglycerin, analgesics)
    /// - 1 Condition (diagnosis: chest pain, possible ACS)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 58 - typical chest pain presentation age).</param>
    /// <param name="gender">Patient gender (default: "male" - higher cardiac risk).</param>
    /// <returns>A complete scenario context with ED chest pain workup.</returns>
    public static ScenarioContext GetChestPainVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Emergency Department Visit - Chest Pain with Cardiac Workup")
            .WithDescription("ED visit demonstrating cardiac chest pain workup with EKG, troponin, imaging, and probabilistic disposition.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // === ED INFRASTRUCTURE ===
            .AddOrganization(OrganizationState.EmergencyDepartment())
            .AddPractitioner(PractitionerState.EmergencyPhysician())

            // === ARRIVAL & REGISTRATION ===
            .AddState(new EncounterState
            {
                Name = "ED_Encounter_ChestPain",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Chest pain, onset 2 hours ago, radiating to left arm",
                DurationMinutes = 240 // 4 hours typical for chest pain workup
            })

            // Set ED-specific attributes
            .SetAttribute("chief_complaint", "Chest pain")
            .SetAttribute("esi_score", 2) // ESI 2: Emergent - high risk

            // === TRIAGE (0-10 minutes) ===
            // Abnormal vitals indicating cardiac stress
            .AddObservation(ObservationState.BloodPressure(systolic: 158m, diastolic: 94m))
            .AddObservation(ObservationState.HeartRate(value: 98m)) // Mild tachycardia
            .AddObservation(ObservationState.RespiratoryRate(value: 22m)) // Tachypnea
            .AddObservation(ObservationState.BodyTemperature(value: 37.0m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 96m,
                Unit = "%",
                UnitCode = "%"
            })
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 7m,
                Unit = "{score}",
                UnitCode = "{score}"
            })
            .AddObservation(ObservationState.BodyWeight(value: 88m))

            // === INITIAL ASSESSMENT (10-30 minutes) ===
            .Delay(TimeSpan.FromMinutes(10))

            // EKG within 10 minutes of arrival (ACC/AHA guidelines)
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.ECG12Lead,
                IsImagingReport = true,
                Conclusion = "Sinus rhythm. No acute ST-segment changes. Non-specific T-wave abnormalities in leads V4-V6. Cannot rule out acute coronary syndrome."
            })

            // Initial labs
            .AddState(new DiagnosticReportState
            {
                Name = "Initial_Cardiac_Labs",
                Code = EdDiagnosticCodes.TroponinPanel,
                Observations =
                [
                    (EdLabCodes.TroponinI, 0.04m, "ng/mL"), // Borderline elevated
                    (LabObservations.BUN, 18m, "mg/dL"),
                    (LabObservations.Creatinine, 1.1m, "mg/dL")
                ]
            })

            .AddDiagnosticReport(DiagnosticReportState.BasicMetabolicPanel())

            // === DIAGNOSTICS (30-90 minutes) ===
            .Delay(TimeSpan.FromMinutes(30))

            // Chest X-ray
            .AddChestXRay("No acute cardiopulmonary abnormality. Heart size upper limits of normal. Clear lung fields.")

            // === TREATMENT ===
            // Aspirin for chest pain protocol
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Aspirin81mg,
                DosageInstructions = "Chew 4 tablets (324mg total) immediately",
                Frequency = "once",
                DoseQuantity = 4,
                DoseUnit = "tablet"
            })

            // Pain management
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = EdMedicationCodes.Morphine2mg,
                DosageInstructions = "2mg IV push for chest pain",
                Frequency = "as-needed",
                DoseQuantity = 2,
                DoseUnit = "mg"
            })

            // === REPEAT TROPONIN (3 hours) ===
            .Delay(TimeSpan.FromHours(2.5))

            .AddState(new DiagnosticReportState
            {
                Name = "Repeat_Troponin",
                Code = EdDiagnosticCodes.TroponinPanel,
                Observations =
                [
                    (EdLabCodes.TroponinI, 0.03m, "ng/mL") // Trending down - reassuring
                ]
            })

            // === DIAGNOSIS ===
            .AddConditionOnset(EdConditionCodes.ChestPain, severity: 2, assignToAttribute: "chest_pain_condition")

            // === DISPOSITION (probabilistic) ===
            // Based on clinical evidence: ~65% discharge, ~30% admission, ~5% transfer
            .AddProbabilisticBranch(
                (0.65, CreateDischargeState("Chest pain - low risk. Negative serial troponins. Outpatient stress test in 48-72 hours.")),
                (0.30, CreateAdmissionState("Observation admission for chest pain. Rule out ACS protocol.")),
                (0.05, CreateTransferState("Transfer to tertiary care for cardiac catheterization."))
            )

            .Build();
    }

    /// <summary>
    /// Generates an Emergency Department visit for abdominal pain with appendicitis workup.
    ///
    /// Demonstrates:
    /// - <b>ESI Level 3</b>: Urgent, requires multiple resources
    /// - <b>Appendicitis workup</b>: CBC, BMP, CT abdomen/pelvis
    /// - <b>Probabilistic diagnosis</b>: Appendicitis (25%), other causes (75%)
    /// - <b>Surgical consultation</b> if appendicitis diagnosed
    ///
    /// Clinical Pathway:
    /// 1. Patient presents with right lower quadrant pain, nausea
    /// 2. Triage with low-grade fever, localized tenderness
    /// 3. Labs: CBC with differential, BMP
    /// 4. CT abdomen/pelvis with contrast
    /// 5. Surgical consultation if appendicitis
    /// 6. Disposition based on diagnosis
    ///
    /// Generated Resources:
    /// - 1 Organization (Emergency Department)
    /// - 1-2 Practitioners (Emergency Physician, possibly Surgeon)
    /// - 1 Emergency Encounter (3-5 hours duration)
    /// - 6 Vital Sign Observations
    /// - 2 DiagnosticReports (CBC, CT abdomen)
    /// - 1-2 MedicationRequests (analgesics, antibiotics if appendicitis)
    /// - 1 Condition (diagnosis)
    /// - 0-1 Procedure (appendectomy if admitted)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 28 - peak incidence for appendicitis).</param>
    /// <param name="gender">Patient gender (default: "male" - slightly higher incidence).</param>
    /// <returns>A complete scenario context with ED abdominal pain workup.</returns>
    public static ScenarioContext GetAbdominalPainVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 28,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Emergency Department Visit - Abdominal Pain with Appendicitis Workup")
            .WithDescription("ED visit demonstrating acute abdominal pain workup with labs, CT imaging, and probabilistic surgical pathway.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // === ED INFRASTRUCTURE ===
            .AddOrganization(OrganizationState.EmergencyDepartment())
            .AddPractitioner(PractitionerState.EmergencyPhysician())

            // === ARRIVAL & REGISTRATION ===
            .AddState(new EncounterState
            {
                Name = "ED_Encounter_AbdominalPain",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Abdominal pain, right lower quadrant, onset 12 hours ago with nausea",
                DurationMinutes = 210 // 3.5 hours
            })

            // Set ED-specific attributes
            .SetAttribute("chief_complaint", "Abdominal pain")
            .SetAttribute("esi_score", 3) // ESI 3: Urgent - multiple resources needed

            // === TRIAGE ===
            // Vitals suggesting possible infection
            .AddObservation(ObservationState.BloodPressure(systolic: 128m, diastolic: 78m))
            .AddObservation(ObservationState.HeartRate(value: 92m))
            .AddObservation(ObservationState.RespiratoryRate(value: 18m))
            .AddObservation(ObservationState.BodyTemperature(value: 38.2m)) // Low-grade fever
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 98m,
                Unit = "%",
                UnitCode = "%"
            })
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 8m,
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // === INITIAL ASSESSMENT ===
            .Delay(TimeSpan.FromMinutes(15))

            // Pain management before workup
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = EdMedicationCodes.Morphine2mg,
                DosageInstructions = "4mg IV push for abdominal pain",
                Frequency = "once",
                DoseQuantity = 4,
                DoseUnit = "mg"
            })

            // Anti-nausea medication
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Ondansetron4mg,
                DosageInstructions = "4mg IV for nausea",
                Frequency = "once",
                DoseQuantity = 4,
                DoseUnit = "mg"
            })

            // === DIAGNOSTICS ===
            .Delay(TimeSpan.FromMinutes(20))

            // CBC with differential - elevated WBC suggests infection
            .AddState(new DiagnosticReportState
            {
                Name = "CBC_AbdominalPain",
                Code = DiagnosticReports.CompleteBloodCount,
                Observations =
                [
                    (LabObservations.WBC, 14.2m, "10*3/uL"), // Elevated - infection
                    (LabObservations.Hemoglobin, 14.0m, "g/dL"),
                    (LabObservations.Hematocrit, 42m, "%"),
                    (LabObservations.Platelets, 285m, "10*3/uL")
                ]
            })

            .AddDiagnosticReport(DiagnosticReportState.BasicMetabolicPanel())

            // CT Abdomen/Pelvis
            .Delay(TimeSpan.FromMinutes(60))

            // === PROBABILISTIC DIAGNOSIS PATH ===
            // 25% appendicitis (requires surgery), 75% other (conservative management)
            .AddProbabilisticBranch(
                0.25, // Appendicitis
                CreateAppendicitsPath(),
                CreateNonSurgicalAbdominalPath()
            )

            .Build();
    }

    /// <summary>
    /// Generates an Emergency Department visit for minor trauma (laceration).
    ///
    /// Demonstrates:
    /// - <b>ESI Level 4</b>: Less urgent, single resource needed
    /// - <b>Wound care</b>: Laceration assessment, repair, tetanus prophylaxis
    /// - <b>Simple disposition</b>: Discharge with wound care instructions
    ///
    /// Clinical Pathway:
    /// 1. Patient presents with laceration from household injury
    /// 2. Triage with stable vitals
    /// 3. Wound assessment (depth, contamination, tendon/nerve involvement)
    /// 4. Local anesthesia and laceration repair
    /// 5. Tetanus prophylaxis if indicated
    /// 6. Discharge with wound care instructions
    ///
    /// Generated Resources:
    /// - 1 Organization (Emergency Department)
    /// - 1 Practitioner (Emergency Physician or PA)
    /// - 1 Emergency Encounter (1-2 hours duration)
    /// - 5 Vital Sign Observations
    /// - 1 Procedure (laceration repair)
    /// - 1-2 MedicationRequests (local anesthetic, antibiotics if contaminated)
    /// - 0-1 Immunization (tetanus)
    /// - 1 Condition (laceration diagnosis)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 35).</param>
    /// <param name="gender">Patient gender (default: "male" - higher trauma incidence).</param>
    /// <returns>A complete scenario context with ED minor trauma workup.</returns>
    public static ScenarioContext GetMinorTraumaVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 35,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Emergency Department Visit - Minor Trauma (Laceration)")
            .WithDescription("ED visit demonstrating minor trauma management with laceration repair, wound care, and tetanus prophylaxis.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // === ED INFRASTRUCTURE ===
            .AddOrganization(OrganizationState.EmergencyDepartment())
            .AddPractitioner(PractitionerState.EmergencyPhysician())

            // === ARRIVAL & REGISTRATION ===
            .AddState(new EncounterState
            {
                Name = "ED_Encounter_Laceration",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Laceration to left forearm from kitchen knife, actively bleeding",
                DurationMinutes = 90 // 1.5 hours for simple laceration
            })

            // Set ED-specific attributes
            .SetAttribute("chief_complaint", "Laceration")
            .SetAttribute("esi_score", 4) // ESI 4: Less urgent - single resource

            // === TRIAGE ===
            // Stable vitals - minor injury
            .AddObservation(ObservationState.BloodPressure(systolic: 122m, diastolic: 76m))
            .AddObservation(ObservationState.HeartRate(value: 78m))
            .AddObservation(ObservationState.RespiratoryRate(value: 16m))
            .AddObservation(ObservationState.BodyTemperature(value: 36.8m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 99m,
                Unit = "%",
                UnitCode = "%"
            })

            // === WOUND ASSESSMENT ===
            .Delay(TimeSpan.FromMinutes(15))

            // Diagnosis
            .AddConditionOnset(EdConditionCodes.Laceration, severity: 1, assignToAttribute: "laceration_condition")

            // === TREATMENT ===
            // Local anesthesia
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = EdMedicationCodes.Lidocaine1Percent,
                DosageInstructions = "5mL subcutaneous injection for local anesthesia at wound site",
                Frequency = "once",
                DoseQuantity = 5,
                DoseUnit = "mL"
            })

            .Delay(TimeSpan.FromMinutes(10))

            // Laceration repair procedure
            .AddProcedure(new ProcedureState
            {
                Code = EdProcedureCodes.LacerationRepair,
                Duration = TimeSpan.FromMinutes(20),
                BodySite = "Left forearm",
                Outcome = "3cm laceration closed with 6 simple interrupted sutures (4-0 nylon). Hemostasis achieved. No tendon or nerve involvement.",
                FollowUp = "Suture removal in 10-14 days. Keep wound clean and dry. Return if signs of infection.",
                ReasonConditionAttribute = "laceration_condition"
            })

            // Tetanus prophylaxis (if last dose > 5 years or unknown)
            .AddProbabilisticBranch(
                0.60, // 60% need tetanus
                new ImmunizationState
                {
                    Name = "Tetanus_Prophylaxis",
                    Code = EdImmunizationCodes.TetanusDiphtheria,
                    Route = "IM"
                },
                DelayState.ExactDuration(TimeSpan.Zero) // No tetanus needed
            )

            // Prophylactic antibiotics for contaminated wound (probabilistic)
            .AddProbabilisticBranch(
                0.30, // 30% contaminated wounds get antibiotics
                new MedicationOrderState
                {
                    Code = FhirCode.Medications.Cephalexin500mg,
                    DosageInstructions = "500mg by mouth 4 times daily for 7 days",
                    Frequency = "four-times-daily",
                    DurationDays = 7,
                    DoseQuantity = 1,
                    DoseUnit = "capsule"
                },
                DelayState.ExactDuration(TimeSpan.Zero) // No antibiotics
            )

            // === DISCHARGE ===
            .Delay(TimeSpan.FromMinutes(15))
            .AddState(CreateDischargeState("Laceration repaired. Wound care instructions provided. Follow up for suture removal."))

            .Build();
    }

    /// <summary>
    /// Generates an Emergency Department visit for fracture with X-ray and orthopedic evaluation.
    ///
    /// Demonstrates:
    /// - <b>ESI Level 3-4</b>: Based on fracture severity
    /// - <b>Orthopedic workup</b>: X-ray, splinting, pain management
    /// - <b>Probabilistic disposition</b>: Discharge with splint (70%), OR admission (30%)
    ///
    /// Generated Resources:
    /// - 1 Organization (Emergency Department)
    /// - 1-2 Practitioners (Emergency Physician, Orthopedic Surgeon)
    /// - 1 Emergency Encounter
    /// - 5 Vital Sign Observations
    /// - 1 DiagnosticReport (X-ray)
    /// - 1 Procedure (splinting or casting)
    /// - 2 MedicationRequests (analgesics)
    /// - 1 Condition (fracture diagnosis)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 42).</param>
    /// <param name="gender">Patient gender (default: "female").</param>
    /// <returns>A complete scenario context with ED fracture workup.</returns>
    public static ScenarioContext GetFractureVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 42,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Emergency Department Visit - Fracture with Orthopedic Workup")
            .WithDescription("ED visit demonstrating fracture management with imaging, splinting, and probabilistic surgical pathway.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // === ED INFRASTRUCTURE ===
            .AddOrganization(OrganizationState.EmergencyDepartment())
            .AddPractitioner(PractitionerState.EmergencyPhysician())

            // === ARRIVAL & REGISTRATION ===
            .AddState(new EncounterState
            {
                Name = "ED_Encounter_Fracture",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Fall on outstretched hand, wrist pain and deformity",
                DurationMinutes = 180 // 3 hours
            })

            // Set ED-specific attributes
            .SetAttribute("chief_complaint", "Wrist injury after fall")
            .SetAttribute("esi_score", 3) // ESI 3: Urgent

            // === TRIAGE ===
            .AddObservation(ObservationState.BloodPressure(systolic: 138m, diastolic: 82m)) // Elevated from pain
            .AddObservation(ObservationState.HeartRate(value: 88m))
            .AddObservation(ObservationState.RespiratoryRate(value: 18m))
            .AddObservation(ObservationState.BodyTemperature(value: 36.7m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 9m,
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // === INITIAL TREATMENT ===
            // Immediate pain management
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = EdMedicationCodes.Morphine2mg,
                DosageInstructions = "4mg IV push for fracture pain",
                Frequency = "once",
                DoseQuantity = 4,
                DoseUnit = "mg"
            })

            // Oral pain medication for discharge
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Ibuprofen400mg,
                DosageInstructions = "400-800mg by mouth every 6 hours as needed for pain",
                Frequency = "four-times-daily",
                DurationDays = 7,
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = false
            })

            // === IMAGING ===
            .Delay(TimeSpan.FromMinutes(30))

            .AddState(new DiagnosticReportState
            {
                Name = "Wrist_XRay",
                Code = EdDiagnosticCodes.XRayExtremity,
                IsImagingReport = true,
                Conclusion = "Distal radius fracture with dorsal angulation (Colles' fracture). No intra-articular extension. Ulna intact. Soft tissue swelling present."
            })

            // === DIAGNOSIS ===
            .AddConditionOnset(EdConditionCodes.DistalRadiusFracture, severity: 2, assignToAttribute: "fracture_condition")

            // === ORTHOPEDIC CONSULTATION & TREATMENT ===
            .Delay(TimeSpan.FromMinutes(20))
            .AddPractitioner(PractitionerState.OrthopedicSurgeon())

            // === DISPOSITION (probabilistic) ===
            // 70% closed reduction + splint (discharge), 30% ORIF (admission)
            .AddProbabilisticBranch(
                0.70,
                CreateFractureDischargePathway(),
                CreateFractureSurgicalPathway()
            )

            .Build();
    }

    #endregion

    #region Helper Methods - Composite States

    /// <summary>
    /// Creates a discharge disposition state with appropriate documentation.
    /// </summary>
    private static CompositeState CreateDischargeState(string dischargeInstructions)
    {
        return new CompositeState
        {
            Name = "Disposition_Discharge",
            States =
            [
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "discharge"
                },
                new SetAttributeState
                {
                    Name = "Set_Discharge_Instructions",
                    AttributeName = "discharge_instructions",
                    Value = dischargeInstructions
                }
            ]
        };
    }

    /// <summary>
    /// Creates an admission disposition state for hospital admission.
    /// </summary>
    private static CompositeState CreateAdmissionState(string admissionReason)
    {
        return new CompositeState
        {
            Name = "Disposition_Admission",
            States =
            [
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "admission"
                },
                new SetAttributeState
                {
                    Name = "Set_Admission_Reason",
                    AttributeName = "admission_reason",
                    Value = admissionReason
                },
                // Create inpatient encounter
                new EncounterState
                {
                    Name = "Inpatient_Admission",
                    EncounterType = FhirCode.EncounterTypes.Inpatient,
                    EncounterClass = "IMP",
                    Status = "in-progress",
                    Reason = admissionReason,
                    DurationMinutes = 1440 // 24 hours initial
                }
            ]
        };
    }

    /// <summary>
    /// Creates a transfer disposition state for transfer to another facility.
    /// </summary>
    private static CompositeState CreateTransferState(string transferReason)
    {
        return new CompositeState
        {
            Name = "Disposition_Transfer",
            States =
            [
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "transfer"
                },
                new SetAttributeState
                {
                    Name = "Set_Transfer_Reason",
                    AttributeName = "transfer_reason",
                    Value = transferReason
                }
            ]
        };
    }

    /// <summary>
    /// Creates the appendicitis pathway with surgical consultation and admission.
    /// </summary>
    private static CompositeState CreateAppendicitsPath()
    {
        return new CompositeState
        {
            Name = "Appendicitis_Path",
            States =
            [
                // CT showing appendicitis
                new DiagnosticReportState
                {
                    Name = "CT_Appendicitis",
                    Code = DiagnosticReports.CTAbdomenPelvisWContrast,
                    IsImagingReport = true,
                    Conclusion = "Acute appendicitis. Appendix measures 12mm in diameter with periappendiceal fat stranding. No abscess or perforation."
                },
                // Diagnosis
                new ConditionOnsetState
                {
                    Name = "Appendicitis_Diagnosis",
                    Code = FhirCode.Conditions.Appendicitis,
                    Severity = 2,
                    AssignToAttribute = "appendicitis_condition"
                },
                // Surgical consultation
                PractitionerState.Surgeon(),
                // Pre-operative antibiotics
                new MedicationOrderState
                {
                    Code = EdMedicationCodes.Ceftriaxone1g,
                    DosageInstructions = "1g IV for pre-operative prophylaxis",
                    Frequency = "once",
                    DoseQuantity = 1,
                    DoseUnit = "g"
                },
                new MedicationOrderState
                {
                    Code = EdMedicationCodes.Metronidazole500mg,
                    DosageInstructions = "500mg IV for anaerobic coverage",
                    Frequency = "once",
                    DoseQuantity = 500,
                    DoseUnit = "mg"
                },
                // Admission for surgery
                new EncounterState
                {
                    Name = "Surgical_Admission",
                    EncounterType = FhirCode.EncounterTypes.Inpatient,
                    EncounterClass = "IMP",
                    Status = "in-progress",
                    Reason = "Appendectomy for acute appendicitis",
                    DurationMinutes = 2880 // 48 hours
                },
                // Appendectomy
                ProcedureState.Appendectomy()
            ]
        };
    }

    /// <summary>
    /// Creates the non-surgical abdominal pain pathway with conservative management.
    /// </summary>
    private static CompositeState CreateNonSurgicalAbdominalPath()
    {
        return new CompositeState
        {
            Name = "NonSurgical_AbdominalPain_Path",
            States =
            [
                // CT with alternative diagnosis
                new DiagnosticReportState
                {
                    Name = "CT_NonAppendix",
                    Code = DiagnosticReports.CTAbdomenPelvisWContrast,
                    IsImagingReport = true,
                    Conclusion = "No evidence of appendicitis. Mild mesenteric lymphadenopathy consistent with mesenteric adenitis. No bowel obstruction or free fluid."
                },
                // Alternative diagnosis
                new ConditionOnsetState
                {
                    Name = "Mesenteric_Adenitis",
                    Code = EdConditionCodes.MesentericAdenitis,
                    Severity = 1,
                    AssignToAttribute = "abdominal_condition"
                },
                // Discharge disposition
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "discharge"
                },
                new SetAttributeState
                {
                    Name = "Set_Discharge_Instructions",
                    AttributeName = "discharge_instructions",
                    Value = "Mesenteric adenitis - viral/self-limiting. Clear liquids, advance diet as tolerated. Return if fever > 101.5F, worsening pain, or inability to keep fluids down."
                }
            ]
        };
    }

    /// <summary>
    /// Creates the fracture discharge pathway with closed reduction and splinting.
    /// </summary>
    private static CompositeState CreateFractureDischargePathway()
    {
        return new CompositeState
        {
            Name = "Fracture_Discharge_Path",
            States =
            [
                // Closed reduction and splinting
                new ProcedureState
                {
                    Name = "Closed_Reduction_Splint",
                    Code = EdProcedureCodes.ClosedReductionSplinting,
                    Duration = TimeSpan.FromMinutes(30),
                    BodySite = "Right wrist",
                    Outcome = "Closed reduction of distal radius fracture with volar splint application. Post-reduction X-ray shows improved alignment.",
                    FollowUp = "Orthopedic follow-up in 5-7 days for cast application. Keep splint dry. Elevate extremity.",
                    ReasonConditionAttribute = "fracture_condition"
                },
                // Discharge
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "discharge"
                },
                new SetAttributeState
                {
                    Name = "Set_Discharge_Instructions",
                    AttributeName = "discharge_instructions",
                    Value = "Distal radius fracture. Splint applied. Follow up with orthopedics in 5-7 days. Ice and elevate. Return for numbness, severe pain, or color changes."
                }
            ]
        };
    }

    /// <summary>
    /// Creates the fracture surgical pathway with ORIF.
    /// </summary>
    private static CompositeState CreateFractureSurgicalPathway()
    {
        return new CompositeState
        {
            Name = "Fracture_Surgical_Path",
            States =
            [
                // Temporary splint while awaiting OR
                new ProcedureState
                {
                    Name = "PreOp_Splint",
                    Code = EdProcedureCodes.ClosedReductionSplinting,
                    Duration = TimeSpan.FromMinutes(15),
                    BodySite = "Right wrist",
                    Outcome = "Temporary splint applied pending surgical fixation.",
                    ReasonConditionAttribute = "fracture_condition"
                },
                // Admission for ORIF
                new EncounterState
                {
                    Name = "Orthopedic_Admission",
                    EncounterType = FhirCode.EncounterTypes.Inpatient,
                    EncounterClass = "IMP",
                    Status = "in-progress",
                    Reason = "ORIF of distal radius fracture",
                    DurationMinutes = 1440 // 24 hours
                },
                // ORIF procedure
                new ProcedureState
                {
                    Name = "ORIF_Wrist",
                    Code = Procedures.ORIF,
                    Duration = TimeSpan.FromMinutes(90),
                    BodySite = "Right distal radius",
                    Category = "surgery",
                    Outcome = "Open reduction internal fixation of distal radius with volar locking plate. Excellent reduction achieved.",
                    FollowUp = "Post-op X-ray. Physical therapy in 6 weeks. Follow up in 2 weeks for wound check.",
                    ReasonConditionAttribute = "fracture_condition"
                },
                new SetAttributeState
                {
                    Name = "Set_Disposition",
                    AttributeName = "disposition",
                    Value = "admission_surgical"
                }
            ]
        };
    }

    #endregion

    #region ED-Specific Codes

    /// <summary>
    /// ED-specific condition codes (SNOMED CT).
    /// </summary>
    public static class EdConditionCodes
    {
        /// <summary>Chest pain finding (SNOMED CT: 29857009)</summary>
        public static readonly FhirCode ChestPain = new(
            FhirCode.Systems.SnomedCt, "29857009", "Chest pain");

        /// <summary>Abdominal pain (SNOMED CT: 21522001)</summary>
        public static readonly FhirCode AbdominalPain = new(
            FhirCode.Systems.SnomedCt, "21522001", "Abdominal pain");

        /// <summary>Laceration of skin (SNOMED CT: 312608009)</summary>
        public static readonly FhirCode Laceration = new(
            FhirCode.Systems.SnomedCt, "312608009", "Laceration of skin");

        /// <summary>Distal radius fracture (SNOMED CT: 263102004)</summary>
        public static readonly FhirCode DistalRadiusFracture = new(
            FhirCode.Systems.SnomedCt, "263102004", "Fracture of distal radius");

        /// <summary>Mesenteric lymphadenitis (SNOMED CT: 65370003)</summary>
        public static readonly FhirCode MesentericAdenitis = new(
            FhirCode.Systems.SnomedCt, "65370003", "Mesenteric lymphadenitis");

        /// <summary>Acute coronary syndrome (SNOMED CT: 394659003)</summary>
        public static readonly FhirCode AcuteCoronarySyndrome = new(
            FhirCode.Systems.SnomedCt, "394659003", "Acute coronary syndrome");
    }

    /// <summary>
    /// ED-specific diagnostic codes (LOINC).
    /// </summary>
    public static class EdDiagnosticCodes
    {
        /// <summary>Troponin I (cardiac marker)</summary>
        public static readonly FhirCode TroponinPanel = new(
            FhirCode.Systems.Loinc, "10839-9", "Troponin I.cardiac [Mass/volume] in Serum or Plasma");

        /// <summary>X-ray of extremity</summary>
        public static readonly FhirCode XRayExtremity = new(
            FhirCode.Systems.Loinc, "37637-3", "XR Extremity");
    }

    /// <summary>
    /// ED-specific lab observation codes (LOINC).
    /// </summary>
    public static class EdLabCodes
    {
        /// <summary>Troponin I</summary>
        public static readonly FhirCode TroponinI = new(
            FhirCode.Systems.Loinc, "10839-9", "Troponin I.cardiac [Mass/volume] in Serum or Plasma");
    }

    /// <summary>
    /// ED-specific medication codes (RxNorm).
    /// </summary>
    public static class EdMedicationCodes
    {
        /// <summary>Morphine 2mg/mL injection</summary>
        public static readonly FhirCode Morphine2mg = new(
            FhirCode.Systems.RxNorm, "373529000", "Morphine sulfate 2 MG/ML Injectable Solution");

        /// <summary>Lidocaine 1% injection for local anesthesia</summary>
        public static readonly FhirCode Lidocaine1Percent = new(
            FhirCode.Systems.RxNorm, "1718928", "Lidocaine hydrochloride 10 MG/ML Injectable Solution");

        /// <summary>Ceftriaxone 1g IV</summary>
        public static readonly FhirCode Ceftriaxone1g = new(
            FhirCode.Systems.RxNorm, "309090", "Ceftriaxone 1000 MG Injection");

        /// <summary>Metronidazole 500mg IV</summary>
        public static readonly FhirCode Metronidazole500mg = new(
            FhirCode.Systems.RxNorm, "311681", "Metronidazole 500 MG Injection");

        /// <summary>Nitroglycerin 0.4mg sublingual</summary>
        public static readonly FhirCode NitroglycerinSublingual = new(
            FhirCode.Systems.RxNorm, "316365", "Nitroglycerin 0.4 MG Sublingual Tablet");
    }

    /// <summary>
    /// ED-specific procedure codes (SNOMED CT).
    /// </summary>
    public static class EdProcedureCodes
    {
        /// <summary>Laceration repair (suturing)</summary>
        public static readonly FhirCode LacerationRepair = new(
            FhirCode.Systems.SnomedCt, "288086009", "Suture of skin");

        /// <summary>IV catheter insertion</summary>
        public static readonly FhirCode IVCatheterInsertion = new(
            FhirCode.Systems.SnomedCt, "392248005", "Insertion of intravenous catheter");

        /// <summary>Closed reduction and splinting</summary>
        public static readonly FhirCode ClosedReductionSplinting = new(
            FhirCode.Systems.SnomedCt, "274474001", "Closed reduction of fracture with splinting");
    }

    /// <summary>
    /// ED-specific immunization codes (CVX).
    /// </summary>
    public static class EdImmunizationCodes
    {
        /// <summary>Tetanus and diphtheria toxoids (Td)</summary>
        public static readonly FhirCode TetanusDiphtheria = new(
            FhirCode.Systems.Cvx, "09", "Td (adult), adsorbed");

        /// <summary>Tetanus, diphtheria, and pertussis (Tdap)</summary>
        public static readonly FhirCode Tdap = new(
            FhirCode.Systems.Cvx, "115", "Tdap");
    }

    #endregion
}
