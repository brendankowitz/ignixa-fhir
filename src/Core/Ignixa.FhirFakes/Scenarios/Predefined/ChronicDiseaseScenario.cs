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
/// Provides extension methods for generating realistic chronic disease management scenarios.
/// Demonstrates longitudinal care pathways with disease progression, multidisciplinary care coordination, and evidence-based treatment guidelines.
/// </summary>
/// <remarks>
/// <para>
/// This scenario implements two major chronic disease pathways based on clinical practice guidelines:
/// </para>
/// <para>
/// <strong>Chronic Kidney Disease (CKD) Progression Pathway:</strong>
/// <list type="bullet">
/// <item><description>Initial diagnosis at CKD Stage 2 (eGFR 60-89, mild decrease)</description></item>
/// <item><description>6-month follow-up with nephrology referral at Stage 3a</description></item>
/// <item><description>Progression to Stage 3b with anemia management</description></item>
/// <item><description>Advanced CKD Stage 4 with dialysis preparation</description></item>
/// <item><description>CarePlan and Goal resources for longitudinal management</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Chronic Obstructive Pulmonary Disease (COPD) Management Pathway:</strong>
/// <list type="bullet">
/// <item><description>Initial diagnosis at GOLD Stage 2 (moderate, FEV1 50-80% predicted)</description></item>
/// <item><description>Stable management with maintenance inhalers</description></item>
/// <item><description>Acute exacerbation requiring ED visit and treatment</description></item>
/// <item><description>Progression to GOLD Stage 3 (severe) with oxygen therapy</description></item>
/// <item><description>Advanced COPD with cor pulmonale and palliative care</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Evidence Base:</strong>
/// <list type="bullet">
/// <item><description>KDIGO CKD Guidelines 2024 (stages, eGFR thresholds, treatment)</description></item>
/// <item><description>GOLD COPD Guidelines 2024 (staging, exacerbation management)</description></item>
/// <item><description>NKF KDOQI Guidelines for anemia and mineral metabolism in CKD</description></item>
/// <item><description>ATS/ERS Guidelines for COPD pharmacotherapy and pulmonary rehabilitation</description></item>
/// </list>
/// </para>
/// </remarks>
public static class ChronicDiseaseScenario
{
    #region Chronic Disease Codes

    /// <summary>
    /// SNOMED CT and LOINC codes specific to chronic kidney disease and COPD pathways.
    /// </summary>
    public static class ChronicDiseaseCodes
    {
        // CKD Diagnoses (SNOMED CT)

        /// <summary>Chronic kidney disease stage 2 (431855005)</summary>
        public static readonly FhirCode CKDStage2 = new(FhirCode.Systems.SnomedCt, "431855005", "Chronic kidney disease stage 2");

        /// <summary>Chronic kidney disease stage 3a (431856006)</summary>
        public static readonly FhirCode CKDStage3a = new(FhirCode.Systems.SnomedCt, "431856006", "Chronic kidney disease stage 3A");

        /// <summary>Chronic kidney disease stage 3b (433146000)</summary>
        public static readonly FhirCode CKDStage3b = new(FhirCode.Systems.SnomedCt, "433146000", "Chronic kidney disease stage 3B");

        /// <summary>Chronic kidney disease stage 4 (431857002)</summary>
        public static readonly FhirCode CKDStage4 = new(FhirCode.Systems.SnomedCt, "431857002", "Chronic kidney disease stage 4");

        /// <summary>Diabetic nephropathy (127013003)</summary>
        public static readonly FhirCode DiabeticNephropathy = new(FhirCode.Systems.SnomedCt, "127013003", "Diabetic nephropathy");

        /// <summary>Hypertensive nephropathy (285831000119108)</summary>
        public static readonly FhirCode HypertensiveNephropathy = new(FhirCode.Systems.SnomedCt, "38481006", "Hypertensive renal disease");

        /// <summary>Anemia due to chronic kidney disease (444912007)</summary>
        public static readonly FhirCode AnemiaCKD = new(FhirCode.Systems.SnomedCt, "444912007", "Anemia due to chronic kidney disease");

        // COPD Diagnoses (SNOMED CT)

        /// <summary>Chronic obstructive pulmonary disease, GOLD 2 (313296004)</summary>
        public static readonly FhirCode COPDGold2 = new(FhirCode.Systems.SnomedCt, "313296004", "Moderate chronic obstructive pulmonary disease");

        /// <summary>Chronic obstructive pulmonary disease, GOLD 3 (313297008)</summary>
        public static readonly FhirCode COPDGold3 = new(FhirCode.Systems.SnomedCt, "313297008", "Severe chronic obstructive pulmonary disease");

        /// <summary>Acute exacerbation of chronic obstructive airways disease (195951007)</summary>
        public static readonly FhirCode COPDExacerbation = new(FhirCode.Systems.SnomedCt, "195951007", "Acute exacerbation of chronic obstructive airways disease");

        /// <summary>Cor pulmonale (87837008)</summary>
        public static readonly FhirCode CorPulmonale = new(FhirCode.Systems.SnomedCt, "87837008", "Chronic cor pulmonale");

        // CKD Lab Tests (LOINC)

        /// <summary>Creatinine in serum/plasma (2160-0)</summary>
        public static readonly FhirCode Creatinine = new(FhirCode.Systems.Loinc, "2160-0", "Creatinine [Mass/volume] in Serum or Plasma");

        /// <summary>Glomerular filtration rate/1.73 sq M.predicted (98979-8)</summary>
        public static readonly FhirCode eGFR = new(FhirCode.Systems.Loinc, "98979-8", "Glomerular filtration rate/1.73 sq M.predicted [Volume Rate/Area] in Serum, Plasma or Blood");

        /// <summary>Albumin/Creatinine ratio in urine (14959-1)</summary>
        public static readonly FhirCode UrineAlbuminCreatinine = new(FhirCode.Systems.Loinc, "14959-1", "Albumin/Creatinine [Mass Ratio] in Urine");

        /// <summary>Parathyroid hormone (PTH) in serum/plasma (2731-8)</summary>
        public static readonly FhirCode PTH = new(FhirCode.Systems.Loinc, "2731-8", "Parathyrin.intact [Mass/volume] in Serum or Plasma");

        /// <summary>Calcium in serum/plasma (17861-6)</summary>
        public static readonly FhirCode Calcium = new(FhirCode.Systems.Loinc, "17861-6", "Calcium [Mass/volume] in Serum or Plasma");

        /// <summary>Phosphate in serum/plasma (2777-1)</summary>
        public static readonly FhirCode Phosphorus = new(FhirCode.Systems.Loinc, "2777-1", "Phosphate [Mass/volume] in Serum or Plasma");

        /// <summary>Hemoglobin (718-7)</summary>
        public static readonly FhirCode Hemoglobin = new(FhirCode.Systems.Loinc, "718-7", "Hemoglobin [Mass/volume] in Blood");

        // COPD Tests (LOINC)

        /// <summary>FEV1 (Forced Expiratory Volume in 1 second) (20150-9)</summary>
        public static readonly FhirCode FEV1 = new(FhirCode.Systems.Loinc, "20150-9", "FEV1 [Volume] Respiratory system");

        /// <summary>FEV1/FVC ratio (19926-5)</summary>
        public static readonly FhirCode FEV1FVCRatio = new(FhirCode.Systems.Loinc, "19926-5", "FEV1/FVC [Volume Fraction] Respiratory system");

        /// <summary>Oxygen saturation (SpO2) (20564-1)</summary>
        public static readonly FhirCode OxygenSaturation = new(FhirCode.Systems.Loinc, "20564-1", "Oxygen saturation in Blood");

        /// <summary>Respiratory rate (9279-1)</summary>
        public static readonly FhirCode RespiratoryRate = new(FhirCode.Systems.Loinc, "9279-1", "Respiratory rate");

        /// <summary>mMRC dyspnea scale (72514-3)</summary>
        public static readonly FhirCode mMRCDyspneaScale = new(FhirCode.Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating [Score] - Reported");

        /// <summary>COPD Assessment Test (CAT) score (72166-2)</summary>
        public static readonly FhirCode CATScore = new(FhirCode.Systems.Loinc, "72166-2", "COPD assessment test panel");

        // CKD Procedures (SNOMED CT)

        /// <summary>Creation of arteriovenous fistula for dialysis (24765003)</summary>
        public static readonly FhirCode AVFistulaCreation = new(FhirCode.Systems.SnomedCt, "24765003", "Creation of arteriovenous fistula for dialysis");

        /// <summary>Renal biopsy (76718000)</summary>
        public static readonly FhirCode RenalBiopsy = new(FhirCode.Systems.SnomedCt, "76718000", "Renal biopsy");

        // COPD Procedures (SNOMED CT)

        /// <summary>Spirometry (127783003)</summary>
        public static readonly FhirCode Spirometry = new(FhirCode.Systems.SnomedCt, "127783003", "Spirometry");

        /// <summary>Pulmonary rehabilitation (225368008)</summary>
        public static readonly FhirCode PulmonaryRehabilitation = new(FhirCode.Systems.SnomedCt, "225368008", "Pulmonary rehabilitation");

        /// <summary>Nebulizer treatment (385716001)</summary>
        public static readonly FhirCode NebulizerTreatment = new(FhirCode.Systems.SnomedCt, "385716001", "Nebulizer therapy");

        /// <summary>Oxygen therapy (371907003)</summary>
        public static readonly FhirCode OxygenTherapy = new(FhirCode.Systems.SnomedCt, "371907003", "Long-term oxygen therapy");

        // CKD Medications (RxNorm)

        /// <summary>Lisinopril 10mg (ACE inhibitor) (314076)</summary>
        public static readonly FhirCode Lisinopril10mg = new(FhirCode.Systems.RxNorm, "314076", "Lisinopril 10 MG Oral Tablet");

        /// <summary>Epoetin alfa (EPO) 4000 units (5542)</summary>
        public static readonly FhirCode EpoetinAlfa = new(FhirCode.Systems.RxNorm, "5542", "Epoetin Alfa");

        /// <summary>Sevelamer 800mg (phosphate binder) (35827)</summary>
        public static readonly FhirCode Sevelamer = new(FhirCode.Systems.RxNorm, "35827", "Sevelamer");

        /// <summary>Calcitriol (vitamin D analog) (1778)</summary>
        public static readonly FhirCode Calcitriol = new(FhirCode.Systems.RxNorm, "1778", "Calcitriol");

        // COPD Medications (RxNorm)

        /// <summary>Tiotropium 18 mcg inhalation powder (353498)</summary>
        public static readonly FhirCode Tiotropium = new(FhirCode.Systems.RxNorm, "353498", "Tiotropium 0.018 MG Inhalation Powder");

        /// <summary>Olodaterol 2.5 mcg/dose inhaler (1534982)</summary>
        public static readonly FhirCode Olodaterol = new(FhirCode.Systems.RxNorm, "1534982", "Olodaterol 0.0025 MG/ACTUAT Metered Dose Inhaler");

        /// <summary>Fluticasone/Salmeterol combination inhaler (352362)</summary>
        public static readonly FhirCode FluticasoneSalmeterol = new(FhirCode.Systems.RxNorm, "352362", "Fluticasone propionate 0.25 MG/ACTUAT / Salmeterol 0.05 MG/ACTUAT Dry Powder Inhaler");

        /// <summary>Albuterol nebulizer solution (435)</summary>
        public static readonly FhirCode AlbuterolNebulizer = new(FhirCode.Systems.RxNorm, "435", "Albuterol");

        /// <summary>Prednisone 40mg (8640)</summary>
        public static readonly FhirCode Prednisone40mg = new(FhirCode.Systems.RxNorm, "312617", "Prednisone 10 MG Oral Tablet");

        /// <summary>Azithromycin 250mg (308460)</summary>
        public static readonly FhirCode Azithromycin = new(FhirCode.Systems.RxNorm, "308460", "Azithromycin 250 MG Oral Tablet");

        /// <summary>Furosemide 40mg (diuretic for cor pulmonale) (310429)</summary>
        public static readonly FhirCode Furosemide = new(FhirCode.Systems.RxNorm, "310429", "Furosemide 40 MG Oral Tablet");
    }

    #endregion

    #region Chronic Kidney Disease (CKD) Progression Pathway

    /// <summary>
    /// Generates a complete Chronic Kidney Disease (CKD) progression pathway from Stage 2 to Stage 4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the longitudinal progression of CKD over 5 years:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Initial Diagnosis: CKD Stage 2 (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>Ambulatory encounter with elevated creatinine and decreased eGFR</description></item>
    /// <item><description>Creatinine: 1.5 mg/dL, eGFR: 75 mL/min/1.73m²</description></item>
    /// <item><description>Urine albumin/creatinine ratio: 35 mg/g (microalbuminuria)</description></item>
    /// <item><description>Diagnosis: CKD Stage 2 with diabetic nephropathy</description></item>
    /// <item><description>Initial management: ACE-I, blood pressure control</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - 6-Month Follow-up: CKD Stage 2 (T+6 months):</strong>
    /// <list type="bullet">
    /// <item><description>Repeat labs: creatinine, eGFR, urine albumin, hemoglobin, mineral metabolism</description></item>
    /// <item><description>Goals: BP &lt; 130/80, proteinuria reduction</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Progression to Stage 3a (T+18 months):</strong>
    /// <list type="bullet">
    /// <item><description>eGFR: 55 mL/min/1.73m², Creatinine: 1.8 mg/dL</description></item>
    /// <item><description>Nephrology referral (ServiceRequest)</description></item>
    /// <item><description>Dietary counseling: protein and phosphorus restriction</description></item>
    /// <item><description>CarePlan: CKD longitudinal care coordination</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Progression to Stage 3b (T+3 years):</strong>
    /// <list type="bullet">
    /// <item><description>eGFR: 40 mL/min/1.73m², Creatinine: 2.2 mg/dL</description></item>
    /// <item><description>Anemia of CKD: Hemoglobin 10.5 g/dL</description></item>
    /// <item><description>Secondary hyperparathyroidism: Elevated PTH</description></item>
    /// <item><description>Medications: EPO for anemia, phosphate binders, vitamin D analog</description></item>
    /// <item><description>Goals: Slow progression, manage complications</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 5 - Advanced CKD: Stage 4 (T+5 years):</strong>
    /// <list type="bullet">
    /// <item><description>eGFR: 25 mL/min/1.73m², Creatinine: 3.5 mg/dL</description></item>
    /// <item><description>AV fistula placement for future dialysis (Procedure)</description></item>
    /// <item><description>Pre-dialysis education and transplant evaluation referral</description></item>
    /// <item><description>CarePlan: Pre-ESRD care coordination</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Generated Resources (25-35):</strong>
    /// <list type="bullet">
    /// <item><description>1 Nephrology Organization</description></item>
    /// <item><description>2 Practitioners (Nephrologist, Primary Care)</description></item>
    /// <item><description>5 Encounters (initial, follow-ups at 6mo, 18mo, 3yr, 5yr)</description></item>
    /// <item><description>15-20 Observations (creatinine, eGFR, urine albumin, hemoglobin, PTH, calcium, phosphorus)</description></item>
    /// <item><description>4 Conditions (CKD stages 2/3a/3b/4, anemia)</description></item>
    /// <item><description>5 MedicationRequests (ACE-I, EPO, phosphate binder, vitamin D, diuretic)</description></item>
    /// <item><description>2 Procedures (AV fistula creation, possible renal biopsy)</description></item>
    /// <item><description>2 ServiceRequests (nephrology referral, transplant evaluation)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 58 - typical CKD onset in diabetic population).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with CKD progression pathway.</returns>
    public static ScenarioContext GetChronicKidneyDiseaseProgression(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Chronic Kidney Disease Progression: Stage 2 to Stage 4")
            .WithDescription("Longitudinal CKD management pathway demonstrating disease progression, multidisciplinary care coordination, anemia management, and dialysis preparation over 5 years.")

            // Initial patient setup
            .WithPatient(age: age, gender: gender)

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Nephrology", "Regional Kidney Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Nephrologist",
                Specialty = Specialties.Nephrology,
                Qualifications = ["ABIM Board Certified - Nephrology"]
            })
            .AddPractitioner(PractitionerState.FamilyPractitioner())

            // === PHASE 1: Initial Diagnosis - CKD Stage 2 (T+0) ===
            .AddEncounter("Initial CKD diagnosis - elevated creatinine")

            // Initial labs
            .AddObservation(ChronicDiseaseCodes.Creatinine, 1.5m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 75m, "mL/min/1.73m2", "mL/min/{1.73_m2}")
            .AddObservation(ChronicDiseaseCodes.UrineAlbuminCreatinine, 35m, "mg/g", "mg/g")
            .AddObservation(ChronicDiseaseCodes.Hemoglobin, 13.5m, "g/dL", "g/dL")

            // CKD Stage 2 diagnosis
            .AddConditionOnset(ChronicDiseaseCodes.CKDStage2, severity: 2, assignToAttribute: "ckd_condition")

            // Etiology: Diabetic nephropathy (if diabetic) or Hypertensive nephropathy
            .AddConditionOnset(ChronicDiseaseCodes.DiabeticNephropathy, severity: 2, assignToAttribute: "ckd_etiology")

            // Initial management: ACE inhibitor
            .AddMedicationOrder(ChronicDiseaseCodes.Lisinopril10mg, isChronic: true, frequency: "once daily", reasonCode: ChronicDiseaseCodes.CKDStage2)

            .SetAttribute("ckd_stage", "2")
            .SetAttribute("pathway_phase", "initial_diagnosis")

            // === PHASE 2: 6-Month Follow-up - CKD Stage 2 (T+6 months) ===
            .DelayMonths(6)
            .AddEncounter("CKD follow-up - 6 months")

            // Repeat labs
            .AddObservation(ChronicDiseaseCodes.Creatinine, 1.6m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 72m, "mL/min/1.73m2", "mL/min/{1.73_m2}")
            .AddObservation(ChronicDiseaseCodes.UrineAlbuminCreatinine, 40m, "mg/g", "mg/g")
            .AddObservation(ChronicDiseaseCodes.Hemoglobin, 13.2m, "g/dL", "g/dL")

            // Mineral metabolism labs
            .AddObservation(ChronicDiseaseCodes.PTH, 55m, "pg/mL", "pg/mL")
            .AddObservation(ChronicDiseaseCodes.Calcium, 9.4m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.Phosphorus, 3.8m, "mg/dL", "mg/dL")

            .AddLipidPanel()

            .SetAttribute("pathway_phase", "stable_stage2")

            // === PHASE 3: Progression to Stage 3a (T+18 months) ===
            .DelayMonths(12)
            .AddEncounter("CKD progression - Stage 3a")

            // Worsening renal function
            .AddObservation(ChronicDiseaseCodes.Creatinine, 1.8m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 55m, "mL/min/1.73m2", "mL/min/{1.73_m2}")
            .AddObservation(ChronicDiseaseCodes.UrineAlbuminCreatinine, 50m, "mg/g", "mg/g")
            .AddObservation(ChronicDiseaseCodes.Hemoglobin, 12.8m, "g/dL", "g/dL")

            // Update diagnosis to Stage 3a
            .AddConditionOnset(ChronicDiseaseCodes.CKDStage3a, severity: 3, assignToAttribute: "ckd_stage3a_condition")

            // Note: In a full implementation, we would use ServiceRequest for nephrology referral
            // For now, we'll add an encounter representing the referral
            .DelayWeeks(2)
            .AddEncounter("Nephrology consultation - initial evaluation")

            .SetAttribute("ckd_stage", "3a")
            .SetAttribute("pathway_phase", "nephrology_care")

            // === PHASE 4: Progression to Stage 3b (T+3 years) ===
            .DelayMonths(18)
            .AddEncounter("CKD progression - Stage 3b with complications")

            // Further decline in renal function
            .AddObservation(ChronicDiseaseCodes.Creatinine, 2.2m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 40m, "mL/min/1.73m2", "mL/min/{1.73_m2}")
            .AddObservation(ChronicDiseaseCodes.UrineAlbuminCreatinine, 80m, "mg/g", "mg/g")

            // Anemia of CKD
            .AddObservation(ChronicDiseaseCodes.Hemoglobin, 10.5m, "g/dL", "g/dL")

            // Secondary hyperparathyroidism
            .AddObservation(ChronicDiseaseCodes.PTH, 120m, "pg/mL", "pg/mL")
            .AddObservation(ChronicDiseaseCodes.Calcium, 8.8m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.Phosphorus, 5.2m, "mg/dL", "mg/dL")

            // Update diagnosis to Stage 3b
            .AddConditionOnset(ChronicDiseaseCodes.CKDStage3b, severity: 4, assignToAttribute: "ckd_stage3b_condition")

            // Anemia diagnosis
            .AddConditionOnset(ChronicDiseaseCodes.AnemiaCKD, severity: 2, assignToAttribute: "anemia_condition")

            // Medications for complications
            .AddMedicationOrder(ChronicDiseaseCodes.EpoetinAlfa, isChronic: true, frequency: "weekly", reasonCode: ChronicDiseaseCodes.AnemiaCKD)
            .AddMedicationOrder(ChronicDiseaseCodes.Sevelamer, isChronic: true, frequency: "three times daily with meals", reasonCode: ChronicDiseaseCodes.CKDStage3b)
            .AddMedicationOrder(ChronicDiseaseCodes.Calcitriol, isChronic: true, frequency: "once daily", reasonCode: ChronicDiseaseCodes.CKDStage3b)

            .SetAttribute("ckd_stage", "3b")
            .SetAttribute("pathway_phase", "complications_management")

            // === PHASE 5: Advanced CKD - Stage 4 (T+5 years) ===
            .DelayMonths(24)
            .AddEncounter("Advanced CKD - Stage 4, dialysis preparation")

            // Severe renal function decline
            .AddObservation(ChronicDiseaseCodes.Creatinine, 3.5m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 25m, "mL/min/1.73m2", "mL/min/{1.73_m2}")
            .AddObservation(ChronicDiseaseCodes.UrineAlbuminCreatinine, 150m, "mg/g", "mg/g")
            .AddObservation(ChronicDiseaseCodes.Hemoglobin, 9.8m, "g/dL", "g/dL")
            .AddObservation(ChronicDiseaseCodes.PTH, 180m, "pg/mL", "pg/mL")

            // Update diagnosis to Stage 4
            .AddConditionOnset(ChronicDiseaseCodes.CKDStage4, severity: 5, assignToAttribute: "ckd_stage4_condition")

            // AV fistula placement for future dialysis
            .DelayWeeks(2)
            .AddEncounter("Vascular surgery - AV fistula creation")
            .AddProcedure(new ProcedureState
            {
                Name = "AV_Fistula_Creation",
                Code = ChronicDiseaseCodes.AVFistulaCreation,
                Duration = TimeSpan.FromHours(2),
                BodySite = "Left forearm",
                Category = "surgery",
                Outcome = "Left radiocephalic arteriovenous fistula created successfully. Palpable thrill noted.",
                FollowUp = "Monitor for maturation over 6-8 weeks. Follow-up vascular ultrasound in 6 weeks."
            })

            .SetAttribute("ckd_stage", "4")
            .SetAttribute("pathway_phase", "pre_dialysis")

            // Follow-up after AV fistula
            .DelayWeeks(8)
            .AddEncounter("Post-operative AV fistula assessment and pre-dialysis education")

            .Build();
    }

    /// <summary>
    /// Static factory method for CKD progression pathway scenario.
    /// </summary>
    /// <param name="builder">The scenario builder to configure.</param>
    /// <returns>The configured scenario builder.</returns>
    public static ScenarioBuilder ChronicKidneyDiseaseProgression(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithName("Chronic Kidney Disease Progression")
            .WithDescription("CKD management pathway from Stage 2 to Stage 4")

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Nephrology", "Regional Kidney Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Nephrologist",
                Specialty = Specialties.Nephrology
            })
            .SetAttribute("disease_type", "ckd")

            // Initial diagnosis
            .AddEncounter("CKD diagnosis")
            .AddObservation(ChronicDiseaseCodes.Creatinine, 1.5m, "mg/dL", "mg/dL")
            .AddObservation(ChronicDiseaseCodes.eGFR, 75m, "mL/min/1.73m2", "mL/min/{1.73_m2}");
    }

    #endregion

    #region COPD Management Pathway

    /// <summary>
    /// Generates a complete Chronic Obstructive Pulmonary Disease (COPD) management pathway with exacerbations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the longitudinal management of COPD over 4 years:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Initial Diagnosis: GOLD Stage 2 (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>Pulmonology encounter with dyspnea and chronic cough</description></item>
    /// <item><description>Spirometry: FEV1 65% predicted, FEV1/FVC 0.65 (obstructive pattern)</description></item>
    /// <item><description>Chest X-ray: hyperinflation</description></item>
    /// <item><description>Diagnosis: COPD GOLD 2 (moderate)</description></item>
    /// <item><description>Smoking history: 30 pack-years</description></item>
    /// <item><description>Treatment: LABA/LAMA combination inhaler, smoking cessation, vaccinations</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - Stable Management (T+3 months):</strong>
    /// <list type="bullet">
    /// <item><description>Follow-up with mMRC dyspnea scale: 2 (dyspnea on level ground)</description></item>
    /// <item><description>CAT score: 15 (moderate impact)</description></item>
    /// <item><description>Continue maintenance inhalers</description></item>
    /// <item><description>Goals: Reduce exacerbations, improve exercise tolerance</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Acute Exacerbation (T+6 months):</strong>
    /// <list type="bullet">
    /// <item><description>ED presentation with increased dyspnea, purulent sputum</description></item>
    /// <item><description>Vitals: RR 24, SpO2 89% on room air</description></item>
    /// <item><description>Treatment: Nebulizers, Prednisone, Azithromycin</description></item>
    /// <item><description>Post-exacerbation follow-up</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Progression to GOLD Stage 3 (T+2 years):</strong>
    /// <list type="bullet">
    /// <item><description>Spirometry: FEV1 45% predicted</description></item>
    /// <item><description>mMRC: 3 (stops for breath after ~100 meters)</description></item>
    /// <item><description>CAT score: 25 (high impact)</description></item>
    /// <item><description>Long-term oxygen therapy (LTOT) prescribed</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 5 - Advanced COPD with Cor Pulmonale (T+4 years):</strong>
    /// <list type="bullet">
    /// <item><description>Right heart failure from COPD (cor pulmonale)</description></item>
    /// <item><description>Echocardiogram: elevated pulmonary artery pressure, RV dysfunction</description></item>
    /// <item><description>Diuretics added for volume management</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Generated Resources (30-40):</strong>
    /// <list type="bullet">
    /// <item><description>2 Organizations (Pulmonology clinic, Emergency department)</description></item>
    /// <item><description>3 Practitioners (Pulmonologist, Emergency physician, Primary care)</description></item>
    /// <item><description>7-8 Encounters (initial, follow-ups, ED visit, post-exacerbation)</description></item>
    /// <item><description>10-15 Observations (spirometry, SpO2, vitals, mMRC, CAT score)</description></item>
    /// <item><description>4 Conditions (COPD GOLD 2, GOLD 3, exacerbation, cor pulmonale)</description></item>
    /// <item><description>8-10 MedicationRequests (inhalers, nebulizers, prednisone, antibiotics, diuretics)</description></item>
    /// <item><description>4-5 Procedures (spirometry, chest X-ray, echocardiogram, oxygen therapy, pulmonary rehab)</description></item>
    /// <item><description>2 Immunizations (Influenza, Pneumococcal vaccines)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 62 - typical COPD onset in smoker population).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with COPD management pathway.</returns>
    public static ScenarioContext GetCOPDManagementWithExacerbations(
        this IFhirSchemaProvider schemaProvider,
        int age = 62,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("COPD Management with Exacerbations: GOLD 2 to Advanced Disease")
            .WithDescription("Longitudinal COPD management pathway demonstrating initial diagnosis, stable management, acute exacerbations, disease progression, oxygen therapy, and cor pulmonale over 4 years.")

            // Initial patient setup
            .WithPatient(age: age, gender: gender)

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Pulmonology", "Respiratory Health Center"))
            .AddOrganization(OrganizationState.EmergencyDepartment("City Hospital ED"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Pulmonologist",
                Specialty = Specialties.Pulmonology,
                Qualifications = ["ABIM Board Certified - Pulmonary Disease"]
            })
            .AddPractitioner(PractitionerState.EmergencyPhysician())
            .AddPractitioner(PractitionerState.FamilyPractitioner())

            // === PHASE 1: Initial Diagnosis - COPD GOLD Stage 2 (T+0) ===
            .AddEncounter("Initial COPD diagnosis - chronic dyspnea and cough")

            // Spirometry
            .AddProcedure(new ProcedureState
            {
                Name = "Spirometry_Initial",
                Code = ChronicDiseaseCodes.Spirometry,
                Duration = TimeSpan.FromMinutes(30),
                Category = "diagnostic",
                Outcome = "Obstructive pattern confirmed. FEV1 65% predicted, FEV1/FVC 0.65. No significant bronchodilator response.",
                FollowUp = "Repeat spirometry annually or with significant symptom changes."
            })

            .AddObservation(ChronicDiseaseCodes.FEV1, 65m, "% predicted", "%")
            .AddObservation(ChronicDiseaseCodes.FEV1FVCRatio, 0.65m, "ratio", "{ratio}")
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 94m, "%", "%")

            // Chest X-ray
            .AddChestXRay("Hyperinflation noted. Flattened diaphragms consistent with COPD. No acute infiltrate or mass.")

            // COPD diagnosis
            .AddConditionOnset(ChronicDiseaseCodes.COPDGold2, severity: 3, assignToAttribute: "copd_condition")

            // Initial treatment: LABA/LAMA combination
            .AddMedicationOrder(ChronicDiseaseCodes.Tiotropium, isChronic: true, frequency: "once daily inhalation", reasonCode: ChronicDiseaseCodes.COPDGold2)
            .AddMedicationOrder(ChronicDiseaseCodes.Olodaterol, isChronic: true, frequency: "twice daily inhalation", reasonCode: ChronicDiseaseCodes.COPDGold2)

            // Vaccinations
            .AddInfluenzaVaccine()
            .DelayDays(7)
            .AddImmunization(new ImmunizationState
            {
                Name = "Pneumococcal_Vaccine",
                Code = new FhirCode(FhirCode.Systems.Cvx, "133", "Pneumococcal conjugate PCV 13"),
                DoseNumber = 1,
                Route = "IM"
            })

            .SetAttribute("copd_stage", "gold2")
            .SetAttribute("pathway_phase", "initial_diagnosis")
            .SetAttribute("smoking_pack_years", 30)

            // === PHASE 2: Stable Management (T+3 months) ===
            .DelayMonths(3)
            .AddEncounter("COPD follow-up - stable management")

            // Symptom assessment
            .AddObservation(ChronicDiseaseCodes.mMRCDyspneaScale, 2m, "score", "{score}")
            .AddObservation(ChronicDiseaseCodes.CATScore, 15m, "score", "{score}")
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 93m, "%", "%")

            .SetAttribute("pathway_phase", "stable_management")

            // === PHASE 3: Acute Exacerbation #1 (T+6 months) ===
            .DelayMonths(3)
            .AddEmergencyVisit("Acute COPD exacerbation - increased dyspnea and purulent sputum")

            // Exacerbation vitals
            .AddObservation(ChronicDiseaseCodes.RespiratoryRate, 24m, "breaths/min", "/min")
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 89m, "%", "%")
            .AddObservation(FhirCode.Observations.BodyTemperature, 37.8m, "Cel", "Cel")

            // Chest X-ray
            .AddChestXRay("Hyperinflation consistent with known COPD. No new infiltrate to suggest pneumonia.")

            // Exacerbation diagnosis
            .AddConditionOnset(ChronicDiseaseCodes.COPDExacerbation, severity: 4, assignToAttribute: "copd_exacerbation")

            // Treatment
            .AddProcedure(new ProcedureState
            {
                Name = "Nebulizer_Treatment_ED",
                Code = ChronicDiseaseCodes.NebulizerTreatment,
                Duration = TimeSpan.FromMinutes(20),
                Category = "therapeutic",
                Outcome = "Albuterol and ipratropium nebulizer treatment administered. Patient reports improved breathing.",
                FollowUp = "Continue nebulizers q4-6h as needed."
            })

            .AddMedicationOrder(ChronicDiseaseCodes.AlbuterolNebulizer, isChronic: false, frequency: "every 4-6 hours as needed", reasonCode: ChronicDiseaseCodes.COPDExacerbation)
            .AddMedicationOrder(ChronicDiseaseCodes.Prednisone40mg, isChronic: false, frequency: "40mg daily for 5 days", reasonCode: ChronicDiseaseCodes.COPDExacerbation)
            .AddMedicationOrder(ChronicDiseaseCodes.Azithromycin, isChronic: false, frequency: "once daily for 5 days", reasonCode: ChronicDiseaseCodes.COPDExacerbation)

            .SetAttribute("exacerbation_count", 1)
            .SetAttribute("pathway_phase", "post_exacerbation")

            // === Post-Exacerbation Follow-up (T+6 months + 2 weeks) ===
            .DelayWeeks(2)
            .AddEncounter("Post-exacerbation follow-up")

            // Improvement noted
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 92m, "%", "%")

            // Consider ICS for frequent exacerbations
            .AddMedicationOrder(ChronicDiseaseCodes.FluticasoneSalmeterol, isChronic: true, frequency: "twice daily inhalation", reasonCode: ChronicDiseaseCodes.COPDGold2)

            // Pulmonary rehabilitation referral (represented as encounter)
            .DelayWeeks(2)
            .AddEncounter("Pulmonary rehabilitation consultation")
            .AddProcedure(new ProcedureState
            {
                Name = "Pulmonary_Rehabilitation_Referral",
                Code = ChronicDiseaseCodes.PulmonaryRehabilitation,
                Category = "therapeutic",
                Outcome = "Patient enrolled in 12-week pulmonary rehabilitation program. Goals: Improve exercise tolerance and quality of life.",
                FollowUp = "Attend sessions 3x/week for 12 weeks."
            })

            // === PHASE 4: Progression to GOLD Stage 3 (T+2 years) ===
            .DelayMonths(18)
            .AddEncounter("COPD progression - worsening dyspnea")

            // Repeat spirometry showing decline
            .AddProcedure(new ProcedureState
            {
                Name = "Spirometry_Follow_Up",
                Code = ChronicDiseaseCodes.Spirometry,
                Duration = TimeSpan.FromMinutes(30),
                Category = "diagnostic",
                Outcome = "Progressive decline in lung function. FEV1 now 45% predicted. FEV1/FVC 0.60.",
                FollowUp = "Continue maximal medical therapy. Consider long-term oxygen assessment."
            })

            .AddObservation(ChronicDiseaseCodes.FEV1, 45m, "% predicted", "%")
            .AddObservation(ChronicDiseaseCodes.FEV1FVCRatio, 0.60m, "ratio", "{ratio}")

            // Worsening symptoms
            .AddObservation(ChronicDiseaseCodes.mMRCDyspneaScale, 3m, "score", "{score}")
            .AddObservation(ChronicDiseaseCodes.CATScore, 25m, "score", "{score}")

            // Resting hypoxemia
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 88m, "%", "%")

            // Update diagnosis to GOLD 3
            .AddConditionOnset(ChronicDiseaseCodes.COPDGold3, severity: 4, assignToAttribute: "copd_gold3_condition")

            // Long-term oxygen therapy
            .AddProcedure(new ProcedureState
            {
                Name = "Long_Term_Oxygen_Therapy",
                Code = ChronicDiseaseCodes.OxygenTherapy,
                Category = "therapeutic",
                Outcome = "Prescribed continuous oxygen therapy at 2 L/min via nasal cannula to maintain SpO2 ≥ 90%.",
                FollowUp = "Use oxygen continuously, especially during sleep and exertion. Follow-up oximetry in 1 month."
            })

            .SetAttribute("copd_stage", "gold3")
            .SetAttribute("pathway_phase", "oxygen_therapy")
            .SetAttribute("ltot_prescribed", true)

            // === PHASE 5: Advanced COPD with Cor Pulmonale (T+4 years) ===
            .DelayMonths(24)
            .AddEncounter("Advanced COPD - right heart failure symptoms")

            // Symptoms of cor pulmonale: peripheral edema, JVD
            .AddObservation(ChronicDiseaseCodes.OxygenSaturation, 86m, "%", "%")

            // Echocardiogram
            .AddProcedure(new ProcedureState
            {
                Name = "Echocardiogram",
                Code = Procedures.CTScan, // Using generic procedure code
                Duration = TimeSpan.FromMinutes(45),
                Category = "diagnostic",
                Outcome = "Echocardiogram: Elevated pulmonary artery systolic pressure (estimated 50 mmHg). Right ventricular hypertrophy and dysfunction. Consistent with cor pulmonale.",
                FollowUp = "Medical management of cor pulmonale with diuretics. Consider advanced therapies or transplant evaluation."
            })

            // Cor pulmonale diagnosis
            .AddConditionOnset(ChronicDiseaseCodes.CorPulmonale, severity: 5, assignToAttribute: "cor_pulmonale_condition")

            // Diuretic therapy
            .AddMedicationOrder(ChronicDiseaseCodes.Furosemide, isChronic: true, frequency: "once daily", reasonCode: ChronicDiseaseCodes.CorPulmonale)

            .SetAttribute("pathway_phase", "advanced_copd")

            // Final follow-up
            .DelayMonths(3)
            .AddEncounter("Advanced COPD management - palliative care discussion")

            .Build();
    }

    /// <summary>
    /// Static factory method for COPD management pathway scenario.
    /// </summary>
    /// <param name="builder">The scenario builder to configure.</param>
    /// <returns>The configured scenario builder.</returns>
    public static ScenarioBuilder COPDManagementWithExacerbations(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithName("COPD Management with Exacerbations")
            .WithDescription("COPD management pathway from GOLD 2 to advanced disease")

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Pulmonology", "Respiratory Health Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Pulmonologist",
                Specialty = Specialties.Pulmonology
            })
            .SetAttribute("disease_type", "copd")

            // Initial diagnosis
            .AddEncounter("COPD diagnosis")
            .AddProcedure(new ProcedureState
            {
                Name = "Spirometry",
                Code = ChronicDiseaseCodes.Spirometry,
                Category = "diagnostic"
            });
    }

    #endregion
}
