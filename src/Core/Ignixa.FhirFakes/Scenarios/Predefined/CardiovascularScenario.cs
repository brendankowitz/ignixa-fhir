// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating cardiovascular disease scenarios.
/// Demonstrates acute care, chronic disease management, and rehabilitation workflows.
/// </summary>
/// <remarks>
/// Cardiovascular diseases are leading causes of hospitalization and death. This scenario
/// implements three major clinical pathways:
///
/// <list type="number">
///   <item>
///     <term>Myocardial Infarction (MI)</term>
///     <description>Acute heart attack with emergency intervention, PCI/stent placement,
///     and cardiac rehabilitation</description>
///   </item>
///   <item>
///     <term>Congestive Heart Failure (CHF)</term>
///     <description>Chronic heart failure with exacerbation, hospitalization, and
///     longitudinal management</description>
///   </item>
///   <item>
///     <term>Ischemic Stroke</term>
///     <description>Acute stroke with tPA, rehabilitation therapy, and secondary prevention</description>
///   </item>
/// </list>
///
/// Each pathway follows evidence-based clinical guidelines with realistic timelines,
/// medication regimens, and resource generation.
/// </remarks>
public static class CardiovascularScenario
{
    #region Acute Myocardial Infarction Pathway

    /// <summary>
    /// Generates an Acute Myocardial Infarction (MI) pathway with PCI intervention
    /// and cardiac rehabilitation.
    ///
    /// Clinical Context:
    /// - Indication: STEMI (ST-Elevation Myocardial Infarction)
    /// - Intervention: Percutaneous Coronary Intervention with stent placement
    /// - Recovery: Cardiac rehabilitation over 12 weeks
    ///
    /// Timeline:
    /// - T+0 (0 hours): Emergency presentation with chest pain, ESI Level 1
    /// - T+2 hours: Cardiac catheterization lab, PCI with stent
    /// - T+24 hours: Inpatient monitoring, echocardiogram, serial troponin
    /// - T+3 days: Hospital discharge with dual antiplatelet therapy
    /// - T+2 weeks: Cardiac rehabilitation begins
    /// - T+6 weeks: Progress assessment
    /// - T+12 weeks: Final cardiac rehab follow-up
    ///
    /// Generated Resources (40-50 total):
    /// - 2 Organizations (Hospital, Cardiac Rehab Center)
    /// - 3 Practitioners (Emergency Physician, Interventional Cardiologist, Cardiac Rehab Nurse)
    /// - 6 Encounters (ED, Cath Lab, Inpatient, Discharge, Rehab sessions, Follow-ups)
    /// - 2 Procedures (PCI with stent, Cardiac catheterization)
    /// - 5 DiagnosticReports (EKG, Echocardiogram, Troponin panels, Lipid panel, BMP)
    /// - 15+ Observations (Vitals, Cardiac biomarkers, Pain scores, Ejection fraction)
    /// - 5 MedicationRequests (Aspirin, Clopidogrel, Metoprolol, Lisinopril, Atorvastatin)
    /// - 1 Condition (Acute MI)
    /// - 2 Goals (LDL target, BP target)
    /// - 1 CarePlan (Post-MI secondary prevention)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 62 - typical MI age).</param>
    /// <param name="gender">Patient gender (default: "male" - higher cardiac risk).</param>
    /// <returns>A complete scenario context with acute MI pathway.</returns>
    public static ScenarioContext GetAcuteMyocardialInfarction(
        this IFhirSchemaProvider schemaProvider,
        int age = 62,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Acute Myocardial Infarction - STEMI with PCI and Cardiac Rehabilitation")
            .WithDescription("Complete MI pathway including emergency presentation, PCI with stent placement, hospital stay, and 12-week cardiac rehabilitation program.")

            // Initial patient
            .WithPatient(age: age, gender: gender);

        return AcuteMyocardialInfarction(builder).Build();
    }

    /// <summary>
    /// Builder extension: Adds the Acute MI pathway to an existing scenario.
    /// Can be composed with other scenario fragments.
    /// </summary>
    /// <param name="builder">The scenario builder.</param>
    /// <returns>The builder with acute MI pathway added.</returns>
    public static ScenarioBuilder AcuteMyocardialInfarction(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            // === PHASE 1: EMERGENCY PRESENTATION (T+0) ===
            .AddState(OrganizationState.Hospital("City Medical Center - Cardiac Care"))
            .AddState(OrganizationState.EmergencyDepartment("Emergency Department"))
            .AddState(PractitionerState.EmergencyPhysician())

            // Emergency encounter - ESI Level 1 (Resuscitation)
            .AddState(new EncounterState
            {
                Name = "ED_Encounter_STEMI",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Chest pain, crushing, radiating to left arm and jaw, diaphoresis, onset 1 hour ago",
                DurationMinutes = 120
            })

            // Set ED-specific attributes
            .SetAttribute("chief_complaint", "Chest pain - suspected STEMI")
            .SetAttribute("esi_score", 1) // ESI 1: Resuscitation required

            // Abnormal vital signs indicating cardiac stress
            .AddObservation(ObservationState.BloodPressure(systolic: 165m, diastolic: 95m))
            .AddObservation(ObservationState.HeartRate(value: 110m)) // Tachycardia
            .AddObservation(ObservationState.RespiratoryRate(value: 24m)) // Tachypnea
            .AddObservation(ObservationState.BodyTemperature(value: 37.2m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 94m,
                Unit = "%",
                UnitCode = "%"
            })
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 9m,
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // Diagnosis: Acute STEMI
            .AddConditionOnset(CardiovascularConditions.AcuteSTEMI,
                severity: 4,
                assignToAttribute: "stemi_condition")

            // Immediate EKG - showing ST elevation
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.ECG12Lead,
                IsImagingReport = true,
                Conclusion = "Acute ST-elevation in leads V1-V4 and I, aVL. ST depression in II, III, aVF. Consistent with acute anterolateral STEMI."
            })

            // Initial cardiac biomarkers - Troponin I elevated
            .AddState(new DiagnosticReportState
            {
                Name = "Initial_Troponin",
                Code = CardiovascularDiagnostics.TroponinPanel,
                Observations =
                [
                    (CardiovascularLabCodes.TroponinI, 2.8m, "ng/mL"), // Significantly elevated (normal <0.04)
                    (LabObservations.BUN, 22m, "mg/dL"),
                    (LabObservations.Creatinine, 1.2m, "mg/dL")
                ]
            })

            // Immediate medications - MONA protocol
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Aspirin81mg,
                DosageInstructions = "Chew 4 tablets (324mg total) immediately",
                Frequency = "once",
                DoseQuantity = 4,
                DoseUnit = "tablet",
                IsChronic = false
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.NitroglycerinSublingual,
                DosageInstructions = "0.4mg sublingual every 5 minutes x3 as needed for chest pain",
                Frequency = "as-needed",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = false
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Morphine2mg,
                DosageInstructions = "2-4mg IV every 5-15 minutes as needed for pain not relieved by nitroglycerin",
                Frequency = "as-needed",
                DoseQuantity = 2,
                DoseUnit = "mg",
                IsChronic = false
            })

            // === PHASE 2: INTERVENTION (T+2 hours) ===
            .Delay(TimeSpan.FromHours(2))

            // Interventional Cardiologist takes over
            .AddState(new PractitionerState
            {
                Specialty = Specialties.Cardiology,
                Qualifications = ["ABIM Board Certified - Interventional Cardiology"]
            })

            // Transfer to cath lab
            .AddState(new EncounterState
            {
                Name = "CathLab_Encounter",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                EncounterClass = "IMP",
                Status = "finished",
                Reason = "Emergent cardiac catheterization for STEMI",
                DurationMinutes = 90
            })

            // Cardiac catheterization procedure
            .AddState(new ProcedureState
            {
                Name = "Cardiac_Catheterization",
                Code = Procedures.CardiacCatheterization,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(30),
                Category = "diagnostic",
                BodySite = "Heart",
                ReasonConditionAttribute = "stemi_condition",
                Outcome = "Complete occlusion of proximal LAD artery. LV function mildly reduced."
            })

            // PCI with stent placement
            .AddState(new ProcedureState
            {
                Name = "PCI_Stent",
                Code = Procedures.PCI,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(45),
                Category = "surgery",
                BodySite = "Left anterior descending coronary artery",
                ReasonConditionAttribute = "stemi_condition",
                Outcome = "Successful PCI with drug-eluting stent placement to proximal LAD. TIMI 3 flow restored. No residual stenosis.",
                FollowUp = "Dual antiplatelet therapy for 12 months. Cardiac rehabilitation referral."
            })

            // Post-PCI antiplatelet loading
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Ticagrelor180mg,
                DosageInstructions = "180mg loading dose immediately post-PCI",
                Frequency = "once",
                DoseQuantity = 2,
                DoseUnit = "tablet",
                IsChronic = false
            })

            // === PHASE 3: HOSPITALIZATION (T+24 hours to 3 days) ===
            .Delay(TimeSpan.FromHours(22)) // T+24 hours

            // Inpatient encounter - Cardiac Care Unit
            .AddState(new EncounterState
            {
                Name = "CCU_Encounter",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                EncounterClass = "IMP",
                Status = "finished",
                Reason = "Post-STEMI monitoring and management",
                DurationMinutes = 2880 // 48 hours
            })

            // Post-intervention vitals
            .AddObservation(ObservationState.BloodPressure(systolic: 128m, diastolic: 78m))
            .AddObservation(ObservationState.HeartRate(value: 72m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 97m,
                Unit = "%",
                UnitCode = "%"
            })
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 2m,
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // Serial troponin monitoring
            .AddState(new DiagnosticReportState
            {
                Name = "Serial_Troponin_24h",
                Code = CardiovascularDiagnostics.TroponinPanel,
                Observations =
                [
                    (CardiovascularLabCodes.TroponinI, 45.2m, "ng/mL") // Peak troponin
                ]
            })

            // Echocardiogram - assess ejection fraction
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.EchocardiographyTTE,
                IsImagingReport = true,
                Conclusion = "LVEF 40% (mildly reduced). Anterior wall hypokinesis. No significant valvular abnormalities. No pericardial effusion."
            })

            // Ejection fraction observation
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.EjectionFraction,
                Value = 40m,
                Unit = "%",
                UnitCode = "%"
            })

            // Lipid panel
            .AddLipidPanel()

            // Basic metabolic panel
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Post-MI BMP")

            // === DISCHARGE MEDICATIONS ===
            // Dual antiplatelet therapy
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Aspirin81mg,
                DosageInstructions = "81mg daily indefinitely",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stemi_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Ticagrelor90mg,
                DosageInstructions = "90mg twice daily for 12 months",
                Frequency = "twice-daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                DurationDays = 365,
                ReasonConditionAttribute = "stemi_condition"
            })

            // Beta-blocker
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Metoprolol25mg,
                DosageInstructions = "25mg twice daily, titrate to target HR 55-65",
                Frequency = "twice-daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stemi_condition"
            })

            // ACE inhibitor
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Lisinopril10mg,
                DosageInstructions = "10mg daily, titrate to 20-40mg as tolerated",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stemi_condition"
            })

            // High-intensity statin
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Atorvastatin80mg,
                DosageInstructions = "80mg daily at bedtime",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stemi_condition"
            })

            // === PHASE 4: CARDIAC REHABILITATION (T+2 weeks to 12 weeks) ===
            .DelayDays(11) // T+3 days: Discharge
            .DelayDays(11) // T+2 weeks: Start cardiac rehab

            // Cardiac rehabilitation center
            .AddState(OrganizationState.Hospital("Cardiac Rehabilitation Center"))
            .AddState(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "36682004", "Physical therapist"),
                Name = "Cardiac_Rehab_Therapist"
            })

            // Cardiac rehab referral (ServiceRequest equivalent via encounter)
            .AddState(new EncounterState
            {
                Name = "CardiacRehab_Initial",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Cardiac rehabilitation - Phase II initial evaluation",
                DurationMinutes = 90
            })

            // Initial cardiac rehab procedure
            .AddState(new ProcedureState
            {
                Name = "CardiacRehab_Exercise_Initial",
                Code = CardiovascularProcedures.CardiacRehabilitation,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stemi_condition",
                Outcome = "Initial exercise tolerance test. Baseline 4 METs. Target: 7+ METs by week 12.",
                FollowUp = "Sessions 3x/week for 12 weeks. Dietary counseling. Smoking cessation support."
            })

            // Vital signs during rehab
            .AddObservation(ObservationState.BloodPressure(systolic: 124m, diastolic: 76m))
            .AddObservation(ObservationState.HeartRate(value: 68m))

            // Goals
            .SetAttribute("goal_ldl", "< 70 mg/dL")
            .SetAttribute("goal_bp", "< 130/80 mmHg")
            .SetAttribute("goal_exercise", "150 min/week moderate intensity")

            // === T+6 WEEKS: PROGRESS ASSESSMENT ===
            .DelayWeeks(4)

            .AddState(new EncounterState
            {
                Name = "CardiacRehab_6Week",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Cardiac rehabilitation - 6 week progress assessment",
                DurationMinutes = 60
            })

            .AddObservation(ObservationState.BloodPressure(systolic: 120m, diastolic: 74m))
            .AddObservation(ObservationState.HeartRate(value: 64m))

            // Follow-up lipid panel
            .AddLipidPanel()

            // === T+12 WEEKS: FINAL CARDIAC REHAB FOLLOW-UP ===
            .DelayWeeks(6)

            .AddState(new EncounterState
            {
                Name = "CardiacRehab_12Week",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Cardiac rehabilitation - 12 week final assessment and discharge",
                DurationMinutes = 60
            })

            // Final cardiac rehab procedure
            .AddState(new ProcedureState
            {
                Name = "CardiacRehab_Exercise_Final",
                Code = CardiovascularProcedures.CardiacRehabilitation,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stemi_condition",
                Outcome = "Final exercise tolerance test. Achieved 7.5 METs. Excellent functional recovery.",
                FollowUp = "Continue home exercise program. Annual cardiology follow-up."
            })

            // Improved ejection fraction
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.EjectionFraction,
                Value = 48m, // Improved from 40%
                Unit = "%",
                UnitCode = "%"
            })

            // Final vitals showing improvement
            .AddObservation(ObservationState.BloodPressure(systolic: 118m, diastolic: 72m))
            .AddObservation(ObservationState.HeartRate(value: 62m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.PainSeverity,
                Value = 0m,
                Unit = "{score}",
                UnitCode = "{score}"
            });
    }

    #endregion

    #region Congestive Heart Failure Pathway

    /// <summary>
    /// Generates a Congestive Heart Failure (CHF) pathway with exacerbation
    /// and chronic disease management.
    ///
    /// Clinical Context:
    /// - Diagnosis: Heart Failure with Reduced Ejection Fraction (HFrEF)
    /// - Course: Initial diagnosis, exacerbation requiring hospitalization, ongoing management
    /// - Goal: Symptom control, prevent readmissions, optimize quality of life
    ///
    /// Timeline:
    /// - T+0: Initial diagnosis - ambulatory encounter with dyspnea, edema
    /// - T+0: Start diuretics, ACE inhibitor, beta-blocker, aldosterone antagonist
    /// - T+3 months: Exacerbation - ED presentation with worsening symptoms
    /// - T+3 months: Hospital admission for 3-5 days - IV diuretics, optimization
    /// - T+4 months: First outpatient follow-up post-discharge
    /// - T+5 months: Second outpatient follow-up
    /// - T+6 months: Cardiology follow-up with echocardiogram
    ///
    /// Generated Resources (30-40 total):
    /// - 2 Organizations (Clinic, Hospital)
    /// - 2 Practitioners (Cardiologist, Hospitalist)
    /// - 6 Encounters (Initial, ED, Inpatient, Follow-ups)
    /// - 4 DiagnosticReports (BNP panels, Echocardiograms, Chest X-rays, BMP)
    /// - 15+ Observations (Vitals, BNP, Weight, Ejection fraction)
    /// - 4 MedicationRequests (Furosemide, Lisinopril, Carvedilol, Spironolactone)
    /// - 1 Condition (Heart failure with reduced EF)
    /// - 2 Goals (Weight stability, Symptom control)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 72 - typical CHF age).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with CHF exacerbation pathway.</returns>
    public static ScenarioContext GetCongestiveHeartFailureExacerbation(
        this IFhirSchemaProvider schemaProvider,
        int age = 72,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Congestive Heart Failure - Exacerbation and Chronic Management")
            .WithDescription("Complete CHF pathway including initial diagnosis, exacerbation with hospitalization, and longitudinal outpatient management.")

            // Initial patient
            .WithPatient(age: age, gender: gender);

        return CongestiveHeartFailureExacerbation(builder).Build();
    }

    /// <summary>
    /// Builder extension: Adds the CHF exacerbation pathway to an existing scenario.
    /// </summary>
    /// <param name="builder">The scenario builder.</param>
    /// <returns>The builder with CHF pathway added.</returns>
    public static ScenarioBuilder CongestiveHeartFailureExacerbation(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            // === PHASE 1: INITIAL DIAGNOSIS (T+0) ===
            .AddState(OrganizationState.ClinicFamilyPractice("Heart Health Clinic"))
            .AddCardiologist()

            // Initial ambulatory encounter
            .AddState(new EncounterState
            {
                Name = "CHF_Initial_Encounter",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Progressive dyspnea on exertion, lower extremity edema, fatigue",
                DurationMinutes = 45
            })

            // Vital signs showing fluid overload
            .AddObservation(ObservationState.BloodPressure(systolic: 148m, diastolic: 92m))
            .AddObservation(ObservationState.HeartRate(value: 96m)) // Elevated
            .AddObservation(ObservationState.RespiratoryRate(value: 22m)) // Elevated
            .AddObservation(ObservationState.BodyWeight(value: 98m)) // Baseline weight
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 93m,
                Unit = "%",
                UnitCode = "%"
            })

            // Diagnosis: Heart Failure with Reduced EF
            .AddConditionOnset(CardiovascularConditions.HeartFailureReducedEF,
                severity: 3,
                assignToAttribute: "hf_condition")

            // BNP elevated
            .AddState(new DiagnosticReportState
            {
                Name = "Initial_BNP",
                Code = CardiovascularDiagnostics.BNPPanel,
                Observations =
                [
                    (CardiovascularLabCodes.BNP, 850m, "pg/mL") // Elevated (normal <100)
                ]
            })

            // Chest X-ray showing pulmonary edema
            .AddChestXRay("Cardiomegaly. Bilateral pulmonary vascular congestion. Small bilateral pleural effusions. Findings consistent with congestive heart failure.")

            // Echocardiogram - reduced EF
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.EchocardiographyTTE,
                IsImagingReport = true,
                Conclusion = "LVEF 35% (moderately reduced). Global hypokinesis. Mild mitral regurgitation. Dilated left ventricle. No pericardial effusion."
            })

            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.EjectionFraction,
                Value = 35m,
                Unit = "%",
                UnitCode = "%"
            })

            // Initial treatment
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Furosemide40mg,
                DosageInstructions = "40mg daily in the morning",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Lisinopril10mg,
                DosageInstructions = "10mg daily, titrate to 20-40mg as tolerated",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Carvedilol3125mg,
                DosageInstructions = "3.125mg twice daily, titrate to 25mg twice daily as tolerated",
                Frequency = "twice-daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Spironolactone25mg,
                DosageInstructions = "25mg daily",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            // Goal: Daily weight monitoring
            .SetAttribute("goal_weight", "Maintain dry weight 92-94 kg")
            .SetAttribute("goal_symptoms", "No exacerbations")

            // === PHASE 2: EXACERBATION (T+3 months) ===
            .DelayMonths(3)

            .AddState(OrganizationState.EmergencyDepartment())
            .AddState(PractitionerState.EmergencyPhysician())

            // ED presentation
            .AddState(new EncounterState
            {
                Name = "CHF_Exacerbation_ED",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Worsening dyspnea at rest, orthopnea, 5 lb weight gain over 3 days",
                DurationMinutes = 180
            })

            // Worsening vitals
            .AddObservation(ObservationState.BloodPressure(systolic: 158m, diastolic: 98m))
            .AddObservation(ObservationState.HeartRate(value: 108m))
            .AddObservation(ObservationState.RespiratoryRate(value: 28m))
            .AddObservation(ObservationState.BodyWeight(value: 103m)) // 5 kg weight gain
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 88m, // Hypoxic
                Unit = "%",
                UnitCode = "%"
            })

            // Significantly elevated BNP
            .AddState(new DiagnosticReportState
            {
                Name = "Exacerbation_BNP",
                Code = CardiovascularDiagnostics.BNPPanel,
                Observations =
                [
                    (CardiovascularLabCodes.BNP, 2200m, "pg/mL") // Very elevated
                ]
            })

            // === PHASE 3: INPATIENT MANAGEMENT (3-5 days) ===
            .AddState(OrganizationState.Hospital("City Medical Center"))
            .AddState(new PractitionerState
            {
                Specialty = Specialties.InternalMedicine,
                Name = "Hospitalist"
            })

            // Hospital admission
            .AddState(new EncounterState
            {
                Name = "CHF_Inpatient",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                EncounterClass = "IMP",
                Status = "finished",
                Reason = "Acute decompensated heart failure requiring IV diuresis",
                DurationMinutes = 5760 // 4 days
            })

            // IV diuretics
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.FurosemideIV,
                DosageInstructions = "40mg IV every 12 hours, adjust based on urine output",
                Frequency = "twice-daily",
                DoseQuantity = 40,
                DoseUnit = "mg",
                IsChronic = false,
                DurationDays = 4,
                ReasonConditionAttribute = "hf_condition"
            })

            // Daily weights during hospitalization
            .DelayDays(1)
            .AddObservation(ObservationState.BodyWeight(value: 100m)) // Day 1: -3 kg
            .DelayDays(1)
            .AddObservation(ObservationState.BodyWeight(value: 97m)) // Day 2: -3 kg
            .DelayDays(1)
            .AddObservation(ObservationState.BodyWeight(value: 95m)) // Day 3: -2 kg
            .DelayDays(1)
            .AddObservation(ObservationState.BodyWeight(value: 94m)) // Day 4: -1 kg (near dry weight)

            // Improved BNP at discharge
            .AddState(new DiagnosticReportState
            {
                Name = "Discharge_BNP",
                Code = CardiovascularDiagnostics.BNPPanel,
                Observations =
                [
                    (CardiovascularLabCodes.BNP, 450m, "pg/mL") // Improved
                ]
            })

            // Discharge vitals
            .AddObservation(ObservationState.BloodPressure(systolic: 118m, diastolic: 72m))
            .AddObservation(ObservationState.HeartRate(value: 74m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 96m, // Improved
                Unit = "%",
                UnitCode = "%"
            })

            // Optimized oral medications
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Furosemide80mg,
                DosageInstructions = "80mg daily in the morning (increased from 40mg)",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Lisinopril20mg,
                DosageInstructions = "20mg daily (titrated up)",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "hf_condition"
            })

            // === PHASE 4: CHRONIC MANAGEMENT ===
            // 1 month post-discharge follow-up
            .DelayDays(30)

            .AddState(new EncounterState
            {
                Name = "CHF_FollowUp_1Month",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Post-hospitalization heart failure follow-up",
                DurationMinutes = 30
            })

            .AddObservation(ObservationState.BodyWeight(value: 93m)) // Maintaining dry weight
            .AddObservation(ObservationState.BloodPressure(systolic: 116m, diastolic: 70m))
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Post-discharge renal function")

            // 2 months post-discharge
            .DelayDays(30)

            .AddState(new EncounterState
            {
                Name = "CHF_FollowUp_2Month",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Heart failure medication optimization",
                DurationMinutes = 30
            })

            .AddObservation(ObservationState.BodyWeight(value: 93m))
            .AddObservation(ObservationState.BloodPressure(systolic: 114m, diastolic: 68m))

            // 3 months post-discharge - cardiology follow-up with echo
            .DelayDays(30)

            .AddCardiologist()
            .AddState(new EncounterState
            {
                Name = "CHF_Cardiology_3Month",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Cardiology follow-up with repeat echocardiogram",
                DurationMinutes = 45
            })

            // Repeat echo - improved EF
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.EchocardiographyTTE,
                IsImagingReport = true,
                Conclusion = "LVEF 42% (improved from 35%). Mild global hypokinesis (improved). Trace mitral regurgitation. LV cavity size improved."
            })

            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.EjectionFraction,
                Value = 42m, // Improved
                Unit = "%",
                UnitCode = "%"
            })

            .AddState(new DiagnosticReportState
            {
                Name = "FollowUp_BNP",
                Code = CardiovascularDiagnostics.BNPPanel,
                Observations =
                [
                    (CardiovascularLabCodes.BNP, 180m, "pg/mL") // Near normal
                ]
            });
    }

    #endregion

    #region Ischemic Stroke Pathway

    /// <summary>
    /// Generates an Ischemic Stroke pathway with tPA, rehabilitation, and long-term follow-up.
    ///
    /// Clinical Context:
    /// - Diagnosis: Acute ischemic stroke in MCA territory
    /// - Intervention: tPA (alteplase) within 4.5-hour window
    /// - Workup: CT, MRI, carotid ultrasound, echocardiogram
    /// - Recovery: Inpatient rehabilitation, outpatient therapy
    ///
    /// Timeline:
    /// - T+0: Acute ED presentation with sudden weakness, aphasia
    /// - T+1 hour: tPA administration after CT rules out hemorrhage
    /// - T+24 hours: MRI brain, carotid ultrasound, echocardiogram
    /// - T+3 days: Transfer to inpatient rehabilitation
    /// - T+1 week: Continue PT/OT/ST
    /// - T+2 weeks: Discharge from inpatient rehab, continue outpatient
    /// - T+4 weeks: Outpatient therapy progress
    /// - T+8 weeks: Neurology follow-up
    /// - T+12 weeks: Final stroke follow-up
    ///
    /// Generated Resources (40-50 total):
    /// - 3 Organizations (Hospital, Rehab facility, Outpatient therapy)
    /// - 5 Practitioners (ER physician, Neurologist, PT, OT, ST)
    /// - 8 Encounters (ED, ICU, Inpatient rehab, Outpatient sessions, Follow-ups)
    /// - 6 DiagnosticReports (CT, MRI, Carotid US, Echo, Labs)
    /// - 15+ Observations (Vitals, NIHSS scores, Modified Rankin Scale, Range of motion)
    /// - 5 Procedures (tPA, PT sessions, OT sessions, ST sessions, Carotid endarterectomy)
    /// - 4 MedicationRequests (Aspirin, Statin, BP control, Alteplase)
    /// - 1 Condition (Ischemic stroke)
    /// - 2 Goals (Functional recovery, Secondary prevention)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 68 - typical stroke age).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with ischemic stroke pathway.</returns>
    public static ScenarioContext GetIschemicStrokeWithRehabilitation(
        this IFhirSchemaProvider schemaProvider,
        int age = 68,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Ischemic Stroke - tPA Treatment and Comprehensive Rehabilitation")
            .WithDescription("Complete stroke pathway including acute tPA treatment, diagnostic workup, inpatient rehabilitation, and long-term follow-up with secondary prevention.")

            // Initial patient
            .WithPatient(age: age, gender: gender);

        return IschemicStrokeWithRehabilitation(builder).Build();
    }

    /// <summary>
    /// Builder extension: Adds the ischemic stroke pathway to an existing scenario.
    /// </summary>
    /// <param name="builder">The scenario builder.</param>
    /// <returns>The builder with stroke pathway added.</returns>
    public static ScenarioBuilder IschemicStrokeWithRehabilitation(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            // === PHASE 1: ACUTE PRESENTATION (T+0) ===
            .AddState(OrganizationState.EmergencyDepartment("Stroke Center - Emergency Department"))
            .AddState(PractitionerState.EmergencyPhysician())

            // Emergency encounter
            .AddState(new EncounterState
            {
                Name = "Stroke_ED_Encounter",
                EncounterType = FhirCode.EncounterTypes.Emergency,
                EncounterClass = "EMER",
                Status = "finished",
                Reason = "Sudden onset right-sided weakness, speech difficulty, onset 45 minutes ago. Last known well 1 hour ago.",
                DurationMinutes = 180
            })

            // Set stroke-specific attributes
            .SetAttribute("chief_complaint", "Stroke symptoms - weakness, aphasia")
            .SetAttribute("esi_score", 1) // ESI 1: Immediate
            .SetAttribute("last_known_well", "1 hour ago")

            // Vital signs
            .AddObservation(ObservationState.BloodPressure(systolic: 178m, diastolic: 102m))
            .AddObservation(ObservationState.HeartRate(value: 92m))
            .AddObservation(ObservationState.RespiratoryRate(value: 18m))
            .AddObservation(ObservationState.BodyTemperature(value: 36.9m))
            .AddObservation(new ObservationState
            {
                Code = VitalSigns.OxygenSaturationPulseOx,
                Value = 96m,
                Unit = "%",
                UnitCode = "%"
            })

            // NIHSS score
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.NIHSS,
                Value = 12m, // Moderate stroke
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // Diagnosis: Ischemic stroke
            .AddConditionOnset(CardiovascularConditions.IschemicStroke,
                severity: 3,
                assignToAttribute: "stroke_condition")

            // CT Head - no hemorrhage
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.CTHeadWoContrast,
                IsImagingReport = true,
                Conclusion = "No acute intracranial hemorrhage. No mass effect. Loss of gray-white differentiation in left MCA territory, suggestive of early ischemic changes."
            })

            // Labs
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Stroke Labs")
            .AddSubScenario(CommonScenarios.CompleteBloodCount(), "Stroke CBC")

            // Coagulation studies
            .AddObservation(new ObservationState
            {
                Code = LabObservations.INR,
                Value = 1.0m,
                Unit = "{INR}",
                UnitCode = "{INR}"
            })

            // === PHASE 2: tPA INTERVENTION (T+1 hour) ===
            .AddState(new PractitionerState
            {
                Specialty = Specialties.Neurology,
                Qualifications = ["ABPN Board Certified - Vascular Neurology"]
            })

            // tPA administration
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Alteplase,
                DosageInstructions = "0.9 mg/kg IV (max 90mg): 10% as bolus, 90% as infusion over 60 minutes",
                Frequency = "once",
                DoseQuantity = 72, // For ~80kg patient
                DoseUnit = "mg",
                IsChronic = false,
                ReasonConditionAttribute = "stroke_condition"
            })

            // tPA procedure
            .AddState(new ProcedureState
            {
                Name = "tPA_Administration",
                Code = CardiovascularProcedures.tPAAdministration,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Alteplase 72mg (0.9 mg/kg) administered per protocol. Door-to-needle time: 45 minutes.",
                FollowUp = "Monitor for 24 hours. No anticoagulation or antiplatelet therapy for 24 hours post-tPA."
            })

            // ICU admission
            .AddState(OrganizationState.Hospital("Stroke Center - Neuro ICU"))
            .AddState(new EncounterState
            {
                Name = "Stroke_ICU_Encounter",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                EncounterClass = "IMP",
                Status = "finished",
                Reason = "Post-tPA monitoring for acute ischemic stroke",
                DurationMinutes = 1440 // 24 hours
            })

            // Serial neuro exams
            .Delay(TimeSpan.FromHours(2))
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.NIHSS,
                Value = 8m, // Improving
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // === PHASE 3: DIAGNOSTIC WORKUP (T+24 hours) ===
            .Delay(TimeSpan.FromHours(22))

            // MRI Brain
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.MRIBrainWoContrast,
                IsImagingReport = true,
                Conclusion = "Acute infarct in left MCA territory involving the left frontal and parietal lobes. DWI positive, ADC restricted. MRA shows reconstituted flow in left MCA. No hemorrhagic transformation."
            })

            // Carotid ultrasound - significant stenosis
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = CardiovascularDiagnostics.CarotidUltrasound,
                IsImagingReport = true,
                Conclusion = "Left internal carotid artery: 70% stenosis with heterogeneous plaque. Right internal carotid artery: 30% stenosis. Left vertebral artery patent."
            })

            // Echocardiogram - rule out cardioembolic source
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = DiagnosticReports.EchocardiographyTTE,
                IsImagingReport = true,
                Conclusion = "LVEF 55%. No intracardiac thrombus. No PFO by bubble study. No significant valvular disease. Large-artery atherosclerosis most likely stroke mechanism."
            })

            // Lipid panel
            .AddLipidPanel()

            // HbA1c for diabetes screening
            .AddObservation(new ObservationState
            {
                Code = LabObservations.HemoglobinA1c,
                Value = 6.8m, // Prediabetic/diabetic range
                Unit = "%",
                UnitCode = "%"
            })

            // Swallowing assessment
            .Delay(TimeSpan.FromHours(12))

            // Updated NIHSS
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.NIHSS,
                Value = 6m, // Further improvement
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // === PHASE 4: INPATIENT REHABILITATION (T+3 days to 2 weeks) ===
            .DelayDays(2) // T+3 days

            .AddState(OrganizationState.Hospital("Stroke Rehabilitation Center"))
            .AddState(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "36682004", "Physical therapist"),
                Name = "Stroke_PT"
            })
            .AddState(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "80546007", "Occupational therapist"),
                Name = "Stroke_OT"
            })
            .AddState(new PractitionerState
            {
                Specialty = new FhirCode(FhirCode.Systems.SnomedCt, "159026005", "Speech therapist"),
                Name = "Stroke_ST"
            })

            // Inpatient rehab encounter
            .AddState(new EncounterState
            {
                Name = "Stroke_Inpatient_Rehab",
                EncounterType = FhirCode.EncounterTypes.Inpatient,
                EncounterClass = "IMP",
                Status = "finished",
                Reason = "Acute inpatient rehabilitation post-ischemic stroke",
                DurationMinutes = 14400 // 10 days
            })

            // PT evaluation and treatment
            .AddState(new ProcedureState
            {
                Name = "PT_Evaluation",
                Code = CardiovascularProcedures.PhysicalTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Right hemiparesis, moderate severity. Transfers with moderate assist. Ambulation: 20 feet with rolling walker and CGA. Balance impaired.",
                FollowUp = "Daily PT sessions. Goals: independent transfers, ambulation 150 feet with assistive device."
            })

            // OT evaluation
            .AddState(new ProcedureState
            {
                Name = "OT_Evaluation",
                Code = CardiovascularProcedures.OccupationalTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Right upper extremity weakness 3/5. Fine motor deficits. ADL performance: moderate assistance for dressing, grooming, feeding.",
                FollowUp = "Daily OT sessions. Goals: modified independence with ADLs, improved fine motor function."
            })

            // Speech therapy evaluation
            .AddState(new ProcedureState
            {
                Name = "ST_Evaluation",
                Code = CardiovascularProcedures.SpeechTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(45),
                Category = "therapeutic",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Expressive aphasia, moderate severity. Comprehension relatively preserved. Passed bedside swallow evaluation - regular diet with thin liquids.",
                FollowUp = "Daily speech therapy sessions. Goals: improved verbal expression, compensatory strategies."
            })

            // Modified Rankin Scale at admission
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.ModifiedRankinScale,
                Value = 4m, // Moderately severe disability
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // Secondary prevention medications
            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Aspirin81mg,
                DosageInstructions = "81mg daily (started 24h post-tPA)",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stroke_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = CardiovascularMedications.Atorvastatin80mg,
                DosageInstructions = "80mg daily at bedtime",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stroke_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Amlodipine10mg,
                DosageInstructions = "10mg daily for blood pressure control",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stroke_condition"
            })

            .AddMedicationOrder(new MedicationOrderState
            {
                Code = FhirCode.Medications.Lisinopril10mg,
                DosageInstructions = "10mg daily for blood pressure control",
                Frequency = "daily",
                DoseQuantity = 1,
                DoseUnit = "tablet",
                IsChronic = true,
                ReasonConditionAttribute = "stroke_condition"
            })

            // Carotid endarterectomy for significant stenosis
            .DelayDays(5)
            .AddState(PractitionerState.Surgeon())
            .AddState(new ProcedureState
            {
                Name = "Carotid_Endarterectomy",
                Code = CardiovascularProcedures.CarotidEndarterectomy,
                Status = "completed",
                Duration = TimeSpan.FromHours(2),
                Category = "surgery",
                BodySite = "Left internal carotid artery",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Successful left carotid endarterectomy. Atherosclerotic plaque removed. Excellent flow restored.",
                FollowUp = "Post-operative monitoring. Continue dual antiplatelet therapy. Follow-up carotid ultrasound in 6 months."
            })

            // Progress at 1 week
            .DelayDays(5)
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.NIHSS,
                Value = 3m, // Significant improvement
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // === PHASE 5: DISCHARGE FROM INPATIENT REHAB (T+2 weeks) ===
            .DelayDays(5)

            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.ModifiedRankinScale,
                Value = 2m, // Slight disability
                Unit = "{score}",
                UnitCode = "{score}"
            })

            // Discharge encounter
            .AddState(new EncounterState
            {
                Name = "Stroke_Rehab_Discharge",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Discharge from inpatient stroke rehabilitation",
                DurationMinutes = 60
            })

            // Discharge vitals
            .AddObservation(ObservationState.BloodPressure(systolic: 132m, diastolic: 78m))
            .AddObservation(ObservationState.HeartRate(value: 72m))

            // === PHASE 6: OUTPATIENT REHABILITATION (T+4 weeks) ===
            .DelayWeeks(2)

            .AddState(new EncounterState
            {
                Name = "Outpatient_Rehab_4Week",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Outpatient stroke rehabilitation - PT/OT/ST",
                DurationMinutes = 180
            })

            // Continued PT
            .AddState(new ProcedureState
            {
                Name = "PT_Outpatient",
                Code = CardiovascularProcedures.PhysicalTherapy,
                Status = "completed",
                Duration = TimeSpan.FromMinutes(60),
                Category = "therapeutic",
                ReasonConditionAttribute = "stroke_condition",
                Outcome = "Ambulating 200 feet with single-point cane. Transfers independent. Balance improved.",
                FollowUp = "Continue outpatient PT 2x/week."
            })

            // === T+8 WEEKS: NEUROLOGY FOLLOW-UP ===
            .DelayWeeks(4)

            .AddState(new PractitionerState
            {
                Specialty = Specialties.Neurology,
                Name = "Stroke_Neurologist_FollowUp"
            })

            .AddState(new EncounterState
            {
                Name = "Neurology_8Week",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Neurology follow-up - stroke recovery assessment",
                DurationMinutes = 45
            })

            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.NIHSS,
                Value = 1m, // Near complete recovery
                Unit = "{score}",
                UnitCode = "{score}"
            })

            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.ModifiedRankinScale,
                Value = 1m, // No significant disability
                Unit = "{score}",
                UnitCode = "{score}"
            })

            .AddObservation(ObservationState.BloodPressure(systolic: 128m, diastolic: 76m))

            // === T+12 WEEKS: FINAL FOLLOW-UP ===
            .DelayWeeks(4)

            .AddState(new EncounterState
            {
                Name = "Stroke_Final_FollowUp",
                EncounterType = FhirCode.EncounterTypes.Ambulatory,
                EncounterClass = "AMB",
                Status = "finished",
                Reason = "Final stroke follow-up - secondary prevention assessment",
                DurationMinutes = 30
            })

            // Final assessments
            .AddObservation(new ObservationState
            {
                Code = CardiovascularLabCodes.ModifiedRankinScale,
                Value = 1m, // Stable improvement
                Unit = "{score}",
                UnitCode = "{score}"
            })

            .AddObservation(ObservationState.BloodPressure(systolic: 124m, diastolic: 74m))

            // Follow-up lipid panel
            .AddLipidPanel()

            // Goals achieved
            .SetAttribute("goal_secondary_prevention", "On optimal medical therapy")
            .SetAttribute("goal_functional_status", "Independent in ADLs, ambulating with cane");
    }

    #endregion

    #region Cardiovascular-Specific Codes

    /// <summary>
    /// Cardiovascular condition codes (SNOMED CT).
    /// </summary>
    public static class CardiovascularConditions
    {
        /// <summary>Acute STEMI (ST-elevation myocardial infarction) - SNOMED CT: 401314000</summary>
        public static readonly FhirCode AcuteSTEMI = new(
            FhirCode.Systems.SnomedCt, "401314000", "Acute ST segment elevation myocardial infarction");

        /// <summary>Acute NSTEMI (non-ST-elevation myocardial infarction) - SNOMED CT: 401303003</summary>
        public static readonly FhirCode AcuteNSTEMI = new(
            FhirCode.Systems.SnomedCt, "401303003", "Acute non-ST segment elevation myocardial infarction");

        /// <summary>Heart failure with reduced ejection fraction (HFrEF) - SNOMED CT: 441481004</summary>
        public static readonly FhirCode HeartFailureReducedEF = new(
            FhirCode.Systems.SnomedCt, "441481004", "Heart failure with reduced ejection fraction");

        /// <summary>Ischemic stroke - SNOMED CT: 422504002</summary>
        public static readonly FhirCode IschemicStroke = new(
            FhirCode.Systems.SnomedCt, "422504002", "Ischemic stroke");

        /// <summary>Carotid artery stenosis - SNOMED CT: 64586002</summary>
        public static readonly FhirCode CarotidStenosis = new(
            FhirCode.Systems.SnomedCt, "64586002", "Carotid artery stenosis");
    }

    /// <summary>
    /// Cardiovascular procedure codes (SNOMED CT).
    /// </summary>
    public static class CardiovascularProcedures
    {
        /// <summary>Cardiac rehabilitation - SNOMED CT: 310163003</summary>
        public static readonly FhirCode CardiacRehabilitation = new(
            FhirCode.Systems.SnomedCt, "310163003", "Cardiac rehabilitation");

        /// <summary>tPA administration - SNOMED CT: 713216001</summary>
        public static readonly FhirCode tPAAdministration = new(
            FhirCode.Systems.SnomedCt, "713216001", "Administration of tissue plasminogen activator");

        /// <summary>Carotid endarterectomy - SNOMED CT: 32153003</summary>
        public static readonly FhirCode CarotidEndarterectomy = new(
            FhirCode.Systems.SnomedCt, "32153003", "Carotid endarterectomy");

        /// <summary>Physical therapy - SNOMED CT: 91251008</summary>
        public static readonly FhirCode PhysicalTherapy = new(
            FhirCode.Systems.SnomedCt, "91251008", "Physical therapy");

        /// <summary>Occupational therapy - SNOMED CT: 84478008</summary>
        public static readonly FhirCode OccupationalTherapy = new(
            FhirCode.Systems.SnomedCt, "84478008", "Occupational therapy");

        /// <summary>Speech therapy - SNOMED CT: 311555007</summary>
        public static readonly FhirCode SpeechTherapy = new(
            FhirCode.Systems.SnomedCt, "311555007", "Speech and language therapy");
    }

    /// <summary>
    /// Cardiovascular medication codes (RxNorm).
    /// </summary>
    public static class CardiovascularMedications
    {
        /// <summary>Nitroglycerin 0.4mg sublingual tablet</summary>
        public static readonly FhirCode NitroglycerinSublingual = new(
            FhirCode.Systems.RxNorm, "316365", "Nitroglycerin 0.4 MG Sublingual Tablet");

        /// <summary>Morphine 2mg/mL injectable solution</summary>
        public static readonly FhirCode Morphine2mg = new(
            FhirCode.Systems.RxNorm, "892534", "Morphine Sulfate 2 MG/ML Injectable Solution");

        /// <summary>Ticagrelor 180mg loading dose</summary>
        public static readonly FhirCode Ticagrelor180mg = new(
            FhirCode.Systems.RxNorm, "1116634", "Ticagrelor 90 MG Oral Tablet");

        /// <summary>Ticagrelor 90mg maintenance dose</summary>
        public static readonly FhirCode Ticagrelor90mg = new(
            FhirCode.Systems.RxNorm, "1116634", "Ticagrelor 90 MG Oral Tablet");

        /// <summary>Metoprolol succinate 25mg extended-release tablet</summary>
        public static readonly FhirCode Metoprolol25mg = new(
            FhirCode.Systems.RxNorm, "866427", "Metoprolol Succinate 25 MG Extended Release Oral Tablet");

        /// <summary>Atorvastatin 80mg high-intensity statin</summary>
        public static readonly FhirCode Atorvastatin80mg = new(
            FhirCode.Systems.RxNorm, "259255", "Atorvastatin 80 MG Oral Tablet");

        /// <summary>Furosemide 40mg tablet</summary>
        public static readonly FhirCode Furosemide40mg = new(
            FhirCode.Systems.RxNorm, "310429", "Furosemide 40 MG Oral Tablet");

        /// <summary>Furosemide 80mg tablet</summary>
        public static readonly FhirCode Furosemide80mg = new(
            FhirCode.Systems.RxNorm, "200801", "Furosemide 80 MG Oral Tablet");

        /// <summary>Furosemide IV 40mg</summary>
        public static readonly FhirCode FurosemideIV = new(
            FhirCode.Systems.RxNorm, "313988", "Furosemide 10 MG/ML Injectable Solution");

        /// <summary>Carvedilol 3.125mg tablet</summary>
        public static readonly FhirCode Carvedilol3125mg = new(
            FhirCode.Systems.RxNorm, "200031", "Carvedilol 3.125 MG Oral Tablet");

        /// <summary>Spironolactone 25mg tablet</summary>
        public static readonly FhirCode Spironolactone25mg = new(
            FhirCode.Systems.RxNorm, "313096", "Spironolactone 25 MG Oral Tablet");

        /// <summary>Alteplase (tPA) for thrombolysis</summary>
        public static readonly FhirCode Alteplase = new(
            FhirCode.Systems.RxNorm, "204611", "Alteplase 50 MG Injection");
    }

    /// <summary>
    /// Cardiovascular diagnostic report codes (LOINC).
    /// </summary>
    public static class CardiovascularDiagnostics
    {
        /// <summary>Troponin I panel</summary>
        public static readonly FhirCode TroponinPanel = new(
            FhirCode.Systems.Loinc, "10839-9", "Troponin I.cardiac [Mass/volume] in Serum or Plasma");

        /// <summary>BNP (B-type natriuretic peptide) panel</summary>
        public static readonly FhirCode BNPPanel = new(
            FhirCode.Systems.Loinc, "42637-9", "Natriuretic peptide B [Mass/volume] in Blood");

        /// <summary>Carotid ultrasound</summary>
        public static readonly FhirCode CarotidUltrasound = new(
            FhirCode.Systems.Loinc, "24715-5", "US Carotid arteries");
    }

    /// <summary>
    /// Cardiovascular lab observation codes (LOINC).
    /// </summary>
    public static class CardiovascularLabCodes
    {
        /// <summary>Troponin I - cardiac marker</summary>
        public static readonly FhirCode TroponinI = new(
            FhirCode.Systems.Loinc, "10839-9", "Troponin I.cardiac [Mass/volume] in Serum or Plasma");

        /// <summary>BNP (B-type natriuretic peptide)</summary>
        public static readonly FhirCode BNP = new(
            FhirCode.Systems.Loinc, "42637-9", "Natriuretic peptide B [Mass/volume] in Blood");

        /// <summary>NT-proBNP</summary>
        public static readonly FhirCode NTproBNP = new(
            FhirCode.Systems.Loinc, "33762-6", "Natriuretic peptide.B prohormone N-Terminal [Mass/volume] in Serum or Plasma");

        /// <summary>Ejection fraction by echocardiography</summary>
        public static readonly FhirCode EjectionFraction = new(
            FhirCode.Systems.Loinc, "10230-1", "Left ventricular Ejection fraction");

        /// <summary>NIH Stroke Scale</summary>
        public static readonly FhirCode NIHSS = new(
            FhirCode.Systems.Loinc, "72089-6", "NIH Stroke Scale total score");

        /// <summary>Modified Rankin Scale</summary>
        public static readonly FhirCode ModifiedRankinScale = new(
            FhirCode.Systems.Loinc, "72196-9", "Modified Rankin Scale score");
    }

    #endregion
}
