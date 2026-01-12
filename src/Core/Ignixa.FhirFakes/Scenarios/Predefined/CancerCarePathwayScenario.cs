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
/// Provides extension methods for generating realistic cancer care pathway scenarios.
/// Demonstrates the complete cancer care continuum from screening through diagnosis, staging, and treatment.
/// </summary>
/// <remarks>
/// <para>
/// This scenario implements two major cancer pathways based on evidence-based clinical guidelines:
/// </para>
/// <para>
/// <strong>Breast Cancer Pathway:</strong>
/// <list type="bullet">
/// <item><description>Screening mammography with 10% abnormal finding rate (BIRADS 4-5)</description></item>
/// <item><description>Diagnostic workup with biopsy for abnormal findings</description></item>
/// <item><description>Staging with CT and bone scan</description></item>
/// <item><description>Treatment with surgery, chemotherapy, and radiation</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Colorectal Cancer Pathway:</strong>
/// <list type="bullet">
/// <item><description>Screening colonoscopy with 5% polyp detection rate</description></item>
/// <item><description>Polypectomy with 20% malignancy rate for detected polyps</description></item>
/// <item><description>Staging with CT and CEA tumor marker</description></item>
/// <item><description>Treatment with colectomy and FOLFOX chemotherapy</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Timeline:</strong>
/// <list type="bullet">
/// <item><description>Screening to diagnosis: 2-4 weeks</description></item>
/// <item><description>Diagnosis to staging: 1 week</description></item>
/// <item><description>Staging to treatment: 2 weeks</description></item>
/// <item><description>Total pathway: 5-7 weeks</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Evidence Base:</strong>
/// <list type="bullet">
/// <item><description>Abnormal mammogram rate: ~10% (American Cancer Society)</description></item>
/// <item><description>Colonoscopy polyp detection rate: ~25-50%, adenoma detection ~5% (USPSTF)</description></item>
/// <item><description>High-risk adenoma malignancy rate: ~20% (SEER Database)</description></item>
/// <item><description>Stage distribution: Early (I-II) 60%, Advanced (III-IV) 40% (NCI)</description></item>
/// </list>
/// </para>
/// </remarks>
public static class CancerCarePathwayScenario
{
    #region Cancer Codes

    /// <summary>
    /// SNOMED CT and LOINC codes specific to cancer care pathways.
    /// </summary>
    public static class CancerCodes
    {
        // Cancer Diagnoses (SNOMED CT)

        /// <summary>Malignant neoplasm of breast (254837009)</summary>
        public static readonly FhirCode BreastCancer = new(FhirCode.Systems.SnomedCt, "254837009", "Malignant neoplasm of breast");

        /// <summary>Malignant neoplasm of colon (363414004)</summary>
        public static readonly FhirCode ColorectalCancer = new(FhirCode.Systems.SnomedCt, "363414004", "Malignant neoplasm of colon");

        /// <summary>Malignant neoplasm of lung (363358000)</summary>
        public static readonly FhirCode LungCancer = new(FhirCode.Systems.SnomedCt, "363358000", "Malignant neoplasm of lung");

        /// <summary>Malignant neoplasm of prostate (399068003)</summary>
        public static readonly FhirCode ProstateCancer = new(FhirCode.Systems.SnomedCt, "399068003", "Malignant neoplasm of prostate");

        // Procedures (SNOMED CT)

        /// <summary>Breast biopsy (122548005)</summary>
        public static readonly FhirCode BreastBiopsy = new(FhirCode.Systems.SnomedCt, "122548005", "Biopsy of breast");

        /// <summary>Lumpectomy (392021009)</summary>
        public static readonly FhirCode Lumpectomy = new(FhirCode.Systems.SnomedCt, "392021009", "Lumpectomy of breast");

        /// <summary>Polypectomy (65801008)</summary>
        public static readonly FhirCode Polypectomy = new(FhirCode.Systems.SnomedCt, "65801008", "Excision of polyp");

        /// <summary>External beam radiation therapy (33195004)</summary>
        public static readonly FhirCode RadiationTherapy = new(FhirCode.Systems.SnomedCt, "33195004", "External beam radiation therapy");

        /// <summary>Bone scan (418285008)</summary>
        public static readonly FhirCode BoneScan = new(FhirCode.Systems.SnomedCt, "418285008", "Nuclear medicine diagnostic procedure on skeletal system");

        /// <summary>Lung biopsy (44401000)</summary>
        public static readonly FhirCode LungBiopsy = new(FhirCode.Systems.SnomedCt, "44401000", "Biopsy of lung");

        /// <summary>Lobectomy of lung (359615001)</summary>
        public static readonly FhirCode Lobectomy = new(FhirCode.Systems.SnomedCt, "359615001", "Lobectomy of lung");

        /// <summary>Prostate biopsy (65575008)</summary>
        public static readonly FhirCode ProstateBiopsy = new(FhirCode.Systems.SnomedCt, "65575008", "Biopsy of prostate");

        /// <summary>Radical prostatectomy (176258007)</summary>
        public static readonly FhirCode RadicalProstatectomy = new(FhirCode.Systems.SnomedCt, "176258007", "Radical prostatectomy");

        // Diagnostic Reports (LOINC)

        /// <summary>Mammography - screening bilateral (24606-6)</summary>
        public static readonly FhirCode MammogramScreening = new(FhirCode.Systems.Loinc, "24606-6", "MG Breast Screening");

        /// <summary>Mammography - diagnostic bilateral (24604-1)</summary>
        public static readonly FhirCode MammogramDiagnostic = new(FhirCode.Systems.Loinc, "24604-1", "MG Breast Diagnostic");

        /// <summary>Pathology study (11526-1)</summary>
        public static readonly FhirCode PathologyReport = new(FhirCode.Systems.Loinc, "11526-1", "Pathology study");

        /// <summary>CT abdomen and pelvis with contrast (79103-8)</summary>
        public static readonly FhirCode CTAbdomenPelvis = new(FhirCode.Systems.Loinc, "79103-8", "CT Abdomen and Pelvis W contrast IV");

        /// <summary>CT chest with contrast (24627-2)</summary>
        public static readonly FhirCode CTChest = new(FhirCode.Systems.Loinc, "24627-2", "CT Chest W contrast IV");

        /// <summary>Low dose CT chest for lung cancer screening (79068-3)</summary>
        public static readonly FhirCode LDCT = new(FhirCode.Systems.Loinc, "79068-3", "CT Chest Screening");

        /// <summary>PET whole body (44139-4)</summary>
        public static readonly FhirCode PETCT = new(FhirCode.Systems.Loinc, "44139-4", "PET Whole body");

        // Tumor Markers (LOINC)

        /// <summary>Carcinoembryonic antigen (CEA) in serum (2039-6)</summary>
        public static readonly FhirCode CEA = new(FhirCode.Systems.Loinc, "2039-6", "Carcinoembryonic Ag [Mass/volume] in Serum or Plasma");

        /// <summary>CA 15-3 antigen in serum (6875-9)</summary>
        public static readonly FhirCode CA153 = new(FhirCode.Systems.Loinc, "6875-9", "Cancer Ag 15-3 [Units/volume] in Serum or Plasma");

        /// <summary>PSA (Prostate Specific Antigen) in serum (2857-1)</summary>
        public static readonly FhirCode PSA = new(FhirCode.Systems.Loinc, "2857-1", "Prostate specific Ag [Mass/volume] in Serum or Plasma");

        // Chemotherapy Medications (SNOMED CT)

        /// <summary>Doxorubicin (372817009)</summary>
        public static readonly FhirCode Doxorubicin = new(FhirCode.Systems.SnomedCt, "372817009", "Doxorubicin");

        /// <summary>Cyclophosphamide (387420009)</summary>
        public static readonly FhirCode Cyclophosphamide = new(FhirCode.Systems.SnomedCt, "387420009", "Cyclophosphamide");

        /// <summary>5-Fluorouracil (387192008)</summary>
        public static readonly FhirCode Fluorouracil = new(FhirCode.Systems.SnomedCt, "387192008", "Fluorouracil");

        /// <summary>Oxaliplatin (386906001)</summary>
        public static readonly FhirCode Oxaliplatin = new(FhirCode.Systems.SnomedCt, "386906001", "Oxaliplatin");

        /// <summary>Leucovorin (387181004)</summary>
        public static readonly FhirCode Leucovorin = new(FhirCode.Systems.SnomedCt, "387181004", "Leucovorin");

        /// <summary>Paclitaxel (387374002)</summary>
        public static readonly FhirCode Paclitaxel = new(FhirCode.Systems.SnomedCt, "387374002", "Paclitaxel");

        /// <summary>Cisplatin (387318005)</summary>
        public static readonly FhirCode Cisplatin = new(FhirCode.Systems.SnomedCt, "387318005", "Cisplatin");

        /// <summary>Carboplatin (386906000)</summary>
        public static readonly FhirCode Carboplatin = new(FhirCode.Systems.SnomedCt, "386906000", "Carboplatin");

        /// <summary>Bicalutamide (395732001)</summary>
        public static readonly FhirCode Bicalutamide = new(FhirCode.Systems.SnomedCt, "395732001", "Bicalutamide");

        /// <summary>Leuprolide (396064000)</summary>
        public static readonly FhirCode Leuprolide = new(FhirCode.Systems.SnomedCt, "396064000", "Leuprolide");

        // Stage codes (using generic staging system)
        public static readonly FhirCode StageI = new(FhirCode.Systems.SnomedCt, "258215001", "Stage I");
        public static readonly FhirCode StageII = new(FhirCode.Systems.SnomedCt, "258219007", "Stage II");
        public static readonly FhirCode StageIII = new(FhirCode.Systems.SnomedCt, "258224005", "Stage III");
        public static readonly FhirCode StageIV = new(FhirCode.Systems.SnomedCt, "258228008", "Stage IV");
    }

    #endregion

    #region Breast Cancer Pathway

    /// <summary>
    /// Generates a complete breast cancer screening to treatment pathway scenario.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the full breast cancer care continuum:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Screening (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>Screening mammogram at imaging center</description></item>
    /// <item><description>10% probability of abnormal finding (BIRADS 4-5)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - Diagnosis (T+2 weeks if abnormal):</strong>
    /// <list type="bullet">
    /// <item><description>Diagnostic mammogram for detailed imaging</description></item>
    /// <item><description>Breast biopsy procedure</description></item>
    /// <item><description>Pathology report confirming malignancy</description></item>
    /// <item><description>Breast cancer diagnosis (Condition)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Staging (T+1 week after diagnosis):</strong>
    /// <list type="bullet">
    /// <item><description>CT chest/abdomen/pelvis for metastatic workup</description></item>
    /// <item><description>Bone scan for bone metastases</description></item>
    /// <item><description>CA 15-3 tumor marker</description></item>
    /// <item><description>Stage assignment (I-IV with 60/40 early/advanced split)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Treatment (T+2 weeks after staging):</strong>
    /// <list type="bullet">
    /// <item><description>Surgery: Lumpectomy (early stage) or Mastectomy (advanced)</description></item>
    /// <item><description>Chemotherapy: AC regimen (Doxorubicin + Cyclophosphamide)</description></item>
    /// <item><description>Radiation therapy</description></item>
    /// <item><description>Follow-up oncology visit</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Generated Resources (typical positive pathway):</strong>
    /// <list type="bullet">
    /// <item><description>1 Cancer Center (Organization)</description></item>
    /// <item><description>3 Practitioners (Oncologist, Surgeon, Radiologist)</description></item>
    /// <item><description>5-6 Encounters (screening, diagnosis, staging, surgery, treatment, follow-up)</description></item>
    /// <item><description>4-5 DiagnosticReports (mammograms, pathology, CT, bone scan)</description></item>
    /// <item><description>1 Condition (breast cancer with staging)</description></item>
    /// <item><description>2-3 Procedures (biopsy, surgery, radiation)</description></item>
    /// <item><description>2-3 MedicationRequests (chemotherapy agents)</description></item>
    /// <item><description>2-4 Observations (tumor markers, vital signs)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 55 - typical screening age).</param>
    /// <param name="gender">Patient gender (default: "female").</param>
    /// <returns>A complete scenario context with breast cancer care pathway.</returns>
    public static ScenarioContext GetBreastCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 55,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Breast Cancer Screening to Treatment Pathway")
            .WithDescription("Complete breast cancer care continuum from screening mammography through diagnosis, staging, and treatment including surgery, chemotherapy, and radiation therapy.")

            // Initial patient setup
            .WithPatient(age: age, gender: gender)

            // === PHASE 0: Healthcare Infrastructure ===
            .AddOrganization(OrganizationState.SpecialtyClinic("Oncology", "Regional Cancer Center"))
            .AddOrganization(OrganizationState.ImagingCenter("Breast Imaging Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Oncologist",
                Specialty = Specialties.Oncology,
                Qualifications = ["ABIM Board Certified - Medical Oncology"]
            })
            .AddPractitioner(PractitionerState.Surgeon())
            .AddPractitioner(new PractitionerState
            {
                Name = "Radiologist",
                Specialty = Specialties.Radiology,
                Qualifications = ["ABR Board Certified - Diagnostic Radiology"]
            })

            // === PHASE 1: Screening (T+0) ===
            .AddEncounter("Annual breast cancer screening mammogram")

            // Store initial state
            .SetAttribute("cancer_type", "breast")
            .SetAttribute("pathway_phase", "screening")

            // Screening mammogram
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = CancerCodes.MammogramScreening,
                Conclusion = "Screening mammography completed. Results pending radiologist review."
            })

            // PROBABILISTIC BRANCH: 10% abnormal finding
            .AddProbabilisticBranch(
                0.10, // 10% abnormal finding rate

                // TRUE PATH: Abnormal mammogram - continue to diagnosis
                new CompositeState
                {
                    Name = "Abnormal_Mammogram_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "abnormal"),
                        SetAttributeState.Set("birads_category", "4"),

                        // === PHASE 2: Diagnosis (T+2 weeks) ===
                        DelayState.Weeks(2),

                        new EncounterState { Name = "Diagnostic_Workup_Encounter", Reason = "Diagnostic breast imaging and biopsy" },

                        // Diagnostic mammogram
                        new DiagnosticReportState
                        {
                            Name = "Diagnostic_Mammogram",
                            Code = CancerCodes.MammogramDiagnostic,
                            Conclusion = "BIRADS 4B: Suspicious abnormality. Biopsy recommended. 2.5cm mass identified in upper outer quadrant of right breast."
                        },

                        // Breast biopsy
                        new ProcedureState
                        {
                            Name = "Breast_Biopsy",
                            Code = CancerCodes.BreastBiopsy,
                            Duration = TimeSpan.FromMinutes(45),
                            BodySite = "Right breast",
                            Category = "diagnostic",
                            Outcome = "Core needle biopsy specimen obtained. Pathology results pending.",
                            FollowUp = "Pathology results expected in 3-5 days."
                        },

                        // Pathology report confirming cancer
                        DelayState.Days(5),
                        new DiagnosticReportState
                        {
                            Name = "Pathology_Report",
                            Code = CancerCodes.PathologyReport,
                            Category = "PATH",
                            Conclusion = "Invasive ductal carcinoma. Grade 2 (moderately differentiated). ER+/PR+/HER2-. Tumor size 2.5cm."
                        },

                        // Cancer diagnosis
                        new ConditionOnsetState
                        {
                            Name = "Breast_Cancer_Diagnosis",
                            Code = CancerCodes.BreastCancer,
                            Severity = 3,
                            AssignToAttribute = "breast_cancer_condition",
                            Category = "encounter-diagnosis"
                        },

                        SetAttributeState.Set("pathway_phase", "staging"),
                        SetAttributeState.Set("tumor_size_cm", 2.5m),
                        SetAttributeState.Set("hormone_receptor_positive", true),

                        // === PHASE 3: Staging (T+1 week after diagnosis) ===
                        DelayState.Weeks(1),

                        new EncounterState { Name = "Staging_Workup_Encounter", Reason = "Cancer staging workup" },

                        // CT chest/abdomen/pelvis
                        new DiagnosticReportState
                        {
                            Name = "CT_Staging",
                            Code = CancerCodes.CTAbdomenPelvis,
                            IsImagingReport = true,
                            Conclusion = "CT Chest/Abdomen/Pelvis: No evidence of distant metastatic disease. Liver, lungs, and adrenal glands appear normal."
                        },

                        // Bone scan
                        new ProcedureState
                        {
                            Name = "Bone_Scan",
                            Code = CancerCodes.BoneScan,
                            Duration = TimeSpan.FromHours(3),
                            Category = "diagnostic",
                            Outcome = "Whole body bone scan: No evidence of skeletal metastases."
                        },

                        // CA 15-3 tumor marker
                        new ObservationState
                        {
                            Name = "CA153_Tumor_Marker",
                            Code = CancerCodes.CA153,
                            Value = 28m,
                            Unit = "U/mL",
                            UnitCode = "U/mL"
                        },

                        // Stage assignment (60% early stage, 40% advanced)
                        ProbabilisticBranchState.Binary(
                            0.60, // 60% early stage

                            // Early stage (I-II)
                            new CompositeState
                            {
                                Name = "Early_Stage_Assignment",
                                States =
                                [
                                    SetAttributeState.Set("cancer_stage", "II"),
                                    SetAttributeState.Set("lymph_nodes_positive", 1),

                                    // === PHASE 4: Treatment - Lumpectomy for early stage ===
                                    DelayState.Weeks(2),
                                    new EncounterState { Name = "Surgery_Encounter", Reason = "Breast cancer surgery - Lumpectomy", DurationMinutes = 180 },

                                    new ProcedureState
                                    {
                                        Name = "Lumpectomy_Procedure",
                                        Code = CancerCodes.Lumpectomy,
                                        Duration = TimeSpan.FromHours(2),
                                        BodySite = "Right breast",
                                        Category = "surgery",
                                        Outcome = "Lumpectomy with sentinel lymph node biopsy completed. Margins clear. 1 of 3 sentinel nodes positive.",
                                        FollowUp = "Post-operative follow-up in 1 week. Oncology consultation for adjuvant therapy."
                                    },

                                    // Chemotherapy - AC regimen
                                    DelayState.Weeks(3),
                                    new EncounterState { Name = "Chemo_Consultation", Reason = "Chemotherapy treatment planning" },

                                    new MedicationOrderState
                                    {
                                        Name = "Doxorubicin_Order",
                                        Code = CancerCodes.Doxorubicin,
                                        IsChronic = false,
                                        DurationDays = 84,
                                        DosageInstructions = "60 mg/m2 IV every 21 days for 4 cycles"
                                    },

                                    new MedicationOrderState
                                    {
                                        Name = "Cyclophosphamide_Order",
                                        Code = CancerCodes.Cyclophosphamide,
                                        IsChronic = false,
                                        DurationDays = 84,
                                        DosageInstructions = "600 mg/m2 IV every 21 days for 4 cycles"
                                    },

                                    // Radiation therapy
                                    DelayState.Weeks(12),
                                    new EncounterState { Name = "Radiation_Planning", Reason = "Radiation therapy consultation" },

                                    new ProcedureState
                                    {
                                        Name = "Radiation_Therapy",
                                        Code = CancerCodes.RadiationTherapy,
                                        Duration = TimeSpan.FromMinutes(15),
                                        BodySite = "Right breast",
                                        Category = "therapeutic",
                                        Outcome = "Whole breast radiation therapy - first of 25 fractions delivered.",
                                        Note = "Treatment plan: 50 Gy in 25 fractions over 5 weeks"
                                    },

                                    SetAttributeState.Set("pathway_phase", "treatment_complete")
                                ]
                            },

                            // Advanced stage (III-IV)
                            new CompositeState
                            {
                                Name = "Advanced_Stage_Assignment",
                                States =
                                [
                                    SetAttributeState.Set("cancer_stage", "III"),
                                    SetAttributeState.Set("lymph_nodes_positive", 4),

                                    // === PHASE 4: Treatment - Mastectomy for advanced stage ===
                                    DelayState.Weeks(2),
                                    new EncounterState { Name = "Surgery_Encounter", Reason = "Breast cancer surgery - Mastectomy", DurationMinutes = 240 },

                                    new ProcedureState
                                    {
                                        Name = "Mastectomy_Procedure",
                                        Code = Procedures.Mastectomy,
                                        Duration = TimeSpan.FromHours(3),
                                        BodySite = "Right breast",
                                        Category = "surgery",
                                        Outcome = "Modified radical mastectomy with axillary lymph node dissection completed. 4 of 12 nodes positive.",
                                        FollowUp = "Post-operative follow-up in 1 week. Oncology consultation for neoadjuvant/adjuvant therapy."
                                    },

                                    // More aggressive chemotherapy regimen
                                    DelayState.Weeks(3),
                                    new EncounterState { Name = "Chemo_Consultation", Reason = "Chemotherapy treatment planning" },

                                    new MedicationOrderState
                                    {
                                        Name = "Doxorubicin_Order",
                                        Code = CancerCodes.Doxorubicin,
                                        IsChronic = false,
                                        DurationDays = 84,
                                        DosageInstructions = "60 mg/m2 IV every 21 days for 4 cycles"
                                    },

                                    new MedicationOrderState
                                    {
                                        Name = "Cyclophosphamide_Order",
                                        Code = CancerCodes.Cyclophosphamide,
                                        IsChronic = false,
                                        DurationDays = 84,
                                        DosageInstructions = "600 mg/m2 IV every 21 days for 4 cycles"
                                    },

                                    new MedicationOrderState
                                    {
                                        Name = "Paclitaxel_Order",
                                        Code = CancerCodes.Paclitaxel,
                                        IsChronic = false,
                                        DurationDays = 84,
                                        DosageInstructions = "80 mg/m2 IV weekly for 12 weeks"
                                    },

                                    // Radiation therapy
                                    DelayState.Weeks(16),
                                    new EncounterState { Name = "Radiation_Planning", Reason = "Radiation therapy consultation" },

                                    new ProcedureState
                                    {
                                        Name = "Radiation_Therapy",
                                        Code = CancerCodes.RadiationTherapy,
                                        Duration = TimeSpan.FromMinutes(15),
                                        BodySite = "Right chest wall",
                                        Category = "therapeutic",
                                        Outcome = "Post-mastectomy radiation therapy - first of 28 fractions delivered.",
                                        Note = "Treatment plan: 50 Gy to chest wall + 16 Gy boost to scar"
                                    },

                                    SetAttributeState.Set("pathway_phase", "treatment_complete")
                                ]
                            }
                        ),

                        // === Follow-up Visit ===
                        DelayState.Weeks(6),
                        new EncounterState { Name = "Follow_Up_Encounter", Reason = "Cancer treatment follow-up" },

                        // Repeat tumor marker
                        new ObservationState
                        {
                            Name = "CA153_Follow_Up",
                            Code = CancerCodes.CA153,
                            Value = 18m,
                            Unit = "U/mL",
                            UnitCode = "U/mL"
                        }
                    ]
                },

                // FALSE PATH: Normal mammogram
                new CompositeState
                {
                    Name = "Normal_Mammogram_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "normal"),
                        SetAttributeState.Set("birads_category", "1"),
                        new DiagnosticReportState
                        {
                            Name = "Normal_Mammogram_Report",
                            Code = CancerCodes.MammogramScreening,
                            Conclusion = "BIRADS 1: Negative. No mammographic evidence of malignancy. Routine annual screening recommended."
                        },
                        SetAttributeState.Set("pathway_phase", "screening_complete")
                    ]
                }
            )

            .Build();
    }

    /// <summary>
    /// Static factory method for breast cancer pathway scenario.
    /// </summary>
    /// <param name="builder">The scenario builder to configure.</param>
    /// <returns>The configured scenario builder.</returns>
    public static ScenarioBuilder BreastCancerScreeningToTreatment(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithName("Breast Cancer Screening to Treatment")
            .WithDescription("Breast cancer care pathway from screening to treatment")

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Oncology", "Regional Cancer Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Oncologist",
                Specialty = Specialties.Oncology,
                Qualifications = ["ABIM Board Certified - Medical Oncology"]
            })
            .SetAttribute("cancer_type", "breast")

            // Screening phase
            .AddEncounter("Breast cancer screening mammogram")
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = CancerCodes.MammogramScreening,
                Conclusion = "Screening mammography completed."
            });
    }

    #endregion

    #region Colorectal Cancer Pathway

    /// <summary>
    /// Generates a complete colorectal cancer screening to treatment pathway scenario.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the full colorectal cancer care continuum:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Screening (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>Screening colonoscopy</description></item>
    /// <item><description>5% probability of polyp detection</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - Diagnosis (if polyp found):</strong>
    /// <list type="bullet">
    /// <item><description>Polypectomy during colonoscopy</description></item>
    /// <item><description>Pathology report</description></item>
    /// <item><description>20% of polyps are malignant</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Staging (T+1 week if malignant):</strong>
    /// <list type="bullet">
    /// <item><description>CT chest/abdomen/pelvis</description></item>
    /// <item><description>CEA tumor marker</description></item>
    /// <item><description>Stage assignment (I-IV)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Treatment (T+2 weeks after staging):</strong>
    /// <list type="bullet">
    /// <item><description>Colectomy surgery</description></item>
    /// <item><description>FOLFOX chemotherapy (Fluorouracil, Leucovorin, Oxaliplatin)</description></item>
    /// <item><description>Follow-up oncology visits</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 55 - typical screening age).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with colorectal cancer care pathway.</returns>
    public static ScenarioContext GetColorectalCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 55,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Colorectal Cancer Screening to Treatment Pathway")
            .WithDescription("Complete colorectal cancer care continuum from screening colonoscopy through diagnosis, staging, and treatment including surgery and FOLFOX chemotherapy.")

            // Initial patient setup
            .WithPatient(age: age, gender: gender)

            // === PHASE 0: Healthcare Infrastructure ===
            .AddOrganization(OrganizationState.SpecialtyClinic("Gastroenterology", "Digestive Health Center"))
            .AddOrganization(OrganizationState.SpecialtyClinic("Oncology", "Regional Cancer Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Gastroenterologist",
                Specialty = Specialties.Gastroenterology
            })
            .AddPractitioner(new PractitionerState
            {
                Name = "Oncologist",
                Specialty = Specialties.Oncology,
                Qualifications = ["ABIM Board Certified - Medical Oncology"]
            })
            .AddPractitioner(PractitionerState.Surgeon())

            // === PHASE 1: Screening (T+0) ===
            .AddEncounter("Colorectal cancer screening colonoscopy")

            // Store initial state
            .SetAttribute("cancer_type", "colorectal")
            .SetAttribute("pathway_phase", "screening")

            // PROBABILISTIC BRANCH: 5% polyp found
            .AddProbabilisticBranch(
                0.05, // 5% polyp detection rate for adenomas

                // TRUE PATH: Polyp found
                new CompositeState
                {
                    Name = "Polyp_Found_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "polyp_found"),

                        // Colonoscopy with polyp finding
                        new ProcedureState
                        {
                            Name = "Colonoscopy_With_Polyp",
                            Code = Procedures.Colonoscopy,
                            Duration = TimeSpan.FromMinutes(60),
                            BodySite = "Entire colon",
                            Category = "diagnostic",
                            Outcome = "Colonoscopy revealed a 2.5cm sessile polyp in the sigmoid colon. Polypectomy performed.",
                            FollowUp = "Pathology results pending. Follow-up in 1 week."
                        },

                        // Polypectomy
                        new ProcedureState
                        {
                            Name = "Polypectomy_Procedure",
                            Code = CancerCodes.Polypectomy,
                            Duration = TimeSpan.FromMinutes(15),
                            BodySite = "Sigmoid colon",
                            Category = "therapeutic",
                            Outcome = "En bloc polypectomy of 2.5cm sessile polyp. Specimen sent to pathology."
                        },

                        SetAttributeState.Set("polyp_size_cm", 2.5m),
                        SetAttributeState.Set("polyp_location", "sigmoid colon"),

                        // Wait for pathology
                        DelayState.Days(7),

                        // NESTED BRANCH: 20% malignant
                        ProbabilisticBranchState.Binary(
                            0.20, // 20% malignancy rate

                            // MALIGNANT PATH
                            new CompositeState
                            {
                                Name = "Malignant_Polyp_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "malignant"),

                                    new EncounterState { Name = "Pathology_Review_Encounter", Reason = "Pathology results review - Malignant polyp" },

                                    // Pathology report
                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Malignant",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Invasive adenocarcinoma arising in tubulovillous adenoma. Moderately differentiated. Submucosal invasion present. Margins involved - recommend surgical resection."
                                    },

                                    // Cancer diagnosis
                                    new ConditionOnsetState
                                    {
                                        Name = "Colorectal_Cancer_Diagnosis",
                                        Code = CancerCodes.ColorectalCancer,
                                        Severity = 3,
                                        AssignToAttribute = "colorectal_cancer_condition",
                                        Category = "encounter-diagnosis"
                                    },

                                    SetAttributeState.Set("pathway_phase", "staging"),

                                    // === PHASE 3: Staging (T+1 week) ===
                                    DelayState.Weeks(1),

                                    new EncounterState { Name = "Staging_Workup", Reason = "Colorectal cancer staging workup" },

                                    // CT chest/abdomen/pelvis
                                    new DiagnosticReportState
                                    {
                                        Name = "CT_Staging",
                                        Code = CancerCodes.CTAbdomenPelvis,
                                        IsImagingReport = true,
                                        Conclusion = "CT staging: Primary sigmoid colon mass with possible lymph node involvement. No distant metastases identified."
                                    },

                                    // CEA tumor marker
                                    new ObservationState
                                    {
                                        Name = "CEA_Tumor_Marker",
                                        Code = CancerCodes.CEA,
                                        Value = 8.5m,
                                        Unit = "ng/mL",
                                        UnitCode = "ng/mL"
                                    },

                                    // Stage assignment
                                    ProbabilisticBranchState.Binary(
                                        0.60, // 60% early stage

                                        // Early stage (I-II)
                                        new CompositeState
                                        {
                                            Name = "Early_Stage_CRC",
                                            States =
                                            [
                                                SetAttributeState.Set("cancer_stage", "II"),
                                                SetAttributeState.Set("lymph_nodes_positive", 0),

                                                // === PHASE 4: Treatment ===
                                                DelayState.Weeks(2),
                                                new EncounterState { Name = "Surgery_Consultation", Reason = "Colorectal surgery consultation" },

                                                new ProcedureState
                                                {
                                                    Name = "Colectomy_Procedure",
                                                    Code = Procedures.Colectomy,
                                                    Duration = TimeSpan.FromHours(3),
                                                    BodySite = "Sigmoid colon",
                                                    Category = "surgery",
                                                    Outcome = "Laparoscopic sigmoid colectomy with primary anastomosis. Margins clear. 0 of 15 lymph nodes positive.",
                                                    FollowUp = "Post-operative follow-up in 2 weeks. Consider surveillance only for Stage II."
                                                },

                                                SetAttributeState.Set("pathway_phase", "post_surgery"),

                                                // Follow-up
                                                DelayState.Weeks(4),
                                                new EncounterState { Name = "Post_Op_Follow_Up", Reason = "Post-operative follow-up" },

                                                // Post-surgery CEA
                                                new ObservationState
                                                {
                                                    Name = "CEA_Post_Surgery",
                                                    Code = CancerCodes.CEA,
                                                    Value = 2.1m,
                                                    Unit = "ng/mL",
                                                    UnitCode = "ng/mL"
                                                },

                                                SetAttributeState.Set("pathway_phase", "surveillance")
                                            ]
                                        },

                                        // Advanced stage (III-IV)
                                        new CompositeState
                                        {
                                            Name = "Advanced_Stage_CRC",
                                            States =
                                            [
                                                SetAttributeState.Set("cancer_stage", "III"),
                                                SetAttributeState.Set("lymph_nodes_positive", 3),

                                                // === PHASE 4: Treatment ===
                                                DelayState.Weeks(2),
                                                new EncounterState { Name = "Surgery_Consultation", Reason = "Colorectal surgery consultation" },

                                                new ProcedureState
                                                {
                                                    Name = "Colectomy_Procedure",
                                                    Code = Procedures.Colectomy,
                                                    Duration = TimeSpan.FromHours(4),
                                                    BodySite = "Sigmoid colon",
                                                    Category = "surgery",
                                                    Outcome = "Open sigmoid colectomy with extended lymphadenectomy. Margins clear. 3 of 18 lymph nodes positive.",
                                                    FollowUp = "Post-operative follow-up in 2 weeks. Adjuvant chemotherapy recommended."
                                                },

                                                SetAttributeState.Set("pathway_phase", "chemotherapy"),

                                                // Recovery then chemotherapy
                                                DelayState.Weeks(4),
                                                new EncounterState { Name = "Chemo_Consultation", Reason = "Adjuvant chemotherapy consultation" },

                                                // FOLFOX regimen
                                                new MedicationOrderState
                                                {
                                                    Name = "Fluorouracil_Order",
                                                    Code = CancerCodes.Fluorouracil,
                                                    IsChronic = false,
                                                    DurationDays = 168,
                                                    DosageInstructions = "400 mg/m2 bolus then 2400 mg/m2 over 46 hours, every 2 weeks for 12 cycles"
                                                },

                                                new MedicationOrderState
                                                {
                                                    Name = "Leucovorin_Order",
                                                    Code = CancerCodes.Leucovorin,
                                                    IsChronic = false,
                                                    DurationDays = 168,
                                                    DosageInstructions = "400 mg/m2 IV every 2 weeks for 12 cycles"
                                                },

                                                new MedicationOrderState
                                                {
                                                    Name = "Oxaliplatin_Order",
                                                    Code = CancerCodes.Oxaliplatin,
                                                    IsChronic = false,
                                                    DurationDays = 168,
                                                    DosageInstructions = "85 mg/m2 IV every 2 weeks for 12 cycles"
                                                },

                                                // Mid-treatment follow-up
                                                DelayState.Weeks(12),
                                                new EncounterState { Name = "Chemo_Mid_Treatment", Reason = "Mid-chemotherapy assessment" },

                                                // CEA monitoring
                                                new ObservationState
                                                {
                                                    Name = "CEA_Mid_Chemo",
                                                    Code = CancerCodes.CEA,
                                                    Value = 3.2m,
                                                    Unit = "ng/mL",
                                                    UnitCode = "ng/mL"
                                                },

                                                SetAttributeState.Set("pathway_phase", "treatment_ongoing")
                                            ]
                                        }
                                    )
                                ]
                            },

                            // BENIGN PATH
                            new CompositeState
                            {
                                Name = "Benign_Polyp_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "benign"),

                                    new EncounterState { Name = "Pathology_Review_Encounter", Reason = "Pathology results review - Benign polyp" },

                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Benign",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Tubulovillous adenoma with low-grade dysplasia. Completely excised. No evidence of invasive carcinoma."
                                    },

                                    SetAttributeState.Set("pathway_phase", "surveillance"),
                                    SetAttributeState.Set("next_colonoscopy_years", 3)
                                ]
                            }
                        )
                    ]
                },

                // FALSE PATH: No polyps found
                new CompositeState
                {
                    Name = "Normal_Colonoscopy_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "normal"),

                        ProcedureState.Colonoscopy("Normal colonoscopy. No polyps or masses identified. Colon examined to cecum."),

                        SetAttributeState.Set("pathway_phase", "screening_complete"),
                        SetAttributeState.Set("next_colonoscopy_years", 10)
                    ]
                }
            )

            .Build();
    }

    /// <summary>
    /// Static factory method for colorectal cancer pathway scenario.
    /// </summary>
    /// <param name="builder">The scenario builder to configure.</param>
    /// <returns>The configured scenario builder.</returns>
    public static ScenarioBuilder ColorectalCancerScreeningToTreatment(ScenarioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithName("Colorectal Cancer Screening to Treatment")
            .WithDescription("Colorectal cancer care pathway from screening to treatment")

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.SpecialtyClinic("Gastroenterology", "Digestive Health Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Gastroenterologist",
                Specialty = Specialties.Gastroenterology
            })
            .SetAttribute("cancer_type", "colorectal")

            // Screening phase
            .AddEncounter("Colorectal cancer screening colonoscopy")
            .AddColonoscopy();
    }

    #endregion

    #region Lung Cancer Pathway

    /// <summary>
    /// Generates a complete lung cancer screening to treatment pathway scenario.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the full lung cancer care continuum for high-risk patients:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Screening (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>Low-dose CT (LDCT) screening for high-risk patients</description></item>
    /// <item><description>6% probability of suspicious nodule detection</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - Diagnosis (if nodule found):</strong>
    /// <list type="bullet">
    /// <item><description>CT-guided lung biopsy</description></item>
    /// <item><description>Pathology report</description></item>
    /// <item><description>70% of biopsied nodules are malignant</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Staging (T+1 week if malignant):</strong>
    /// <list type="bullet">
    /// <item><description>PET/CT whole body for metastatic workup</description></item>
    /// <item><description>Brain MRI for brain metastases</description></item>
    /// <item><description>Stage assignment (I-IV)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Treatment (T+2 weeks after staging):</strong>
    /// <list type="bullet">
    /// <item><description>Surgery: Lobectomy for early stage</description></item>
    /// <item><description>Chemotherapy: Cisplatin/Carboplatin + Paclitaxel for advanced stage</description></item>
    /// <item><description>Radiation therapy</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Evidence Base:</strong>
    /// <list type="bullet">
    /// <item><description>LDCT nodule detection rate: ~6% (NLST trial)</description></item>
    /// <item><description>Malignancy rate in biopsied nodules: ~70% (NCI data)</description></item>
    /// <item><description>Stage distribution: Early (I-II) 30%, Advanced (III-IV) 70%</description></item>
    /// <item><description>Eligible population: Age 50-80, 20+ pack-year smoking history</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 65).</param>
    /// <param name="gender">Patient gender.</param>
    /// <returns>A complete scenario context with lung cancer care pathway.</returns>
    public static ScenarioContext GetLungCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 65,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Lung Cancer Screening to Treatment Pathway")
            .WithDescription("Complete lung cancer care continuum from LDCT screening through diagnosis, staging, and treatment including surgery, chemotherapy, and radiation therapy.")

            // Initial patient setup (high-risk smoker)
            .WithPatient(age: age, gender: gender)

            // === PHASE 0: Healthcare Infrastructure ===
            .AddOrganization(OrganizationState.SpecialtyClinic("Pulmonology", "Pulmonary & Thoracic Oncology Center"))
            .AddOrganization(OrganizationState.ImagingCenter("Thoracic Imaging Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Pulmonologist",
                Specialty = Specialties.Pulmonology
            })
            .AddPractitioner(new PractitionerState
            {
                Name = "Oncologist",
                Specialty = Specialties.Oncology,
                Qualifications = ["ABIM Board Certified - Medical Oncology"]
            })
            .AddPractitioner(new PractitionerState
            {
                Name = "Thoracic Surgeon",
                Specialty = Specialties.GeneralSurgery,
                Qualifications = ["ABS Board Certified - Thoracic Surgery"]
            })

            // === PHASE 1: Screening (T+0) ===
            .AddEncounter("Annual lung cancer screening - LDCT")

            // Store initial state
            .SetAttribute("cancer_type", "lung")
            .SetAttribute("pathway_phase", "screening")
            .SetAttribute("smoking_pack_years", 30)

            // LDCT screening
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = CancerCodes.LDCT,
                IsImagingReport = true,
                Conclusion = "Low-dose CT chest completed. Images reviewed for pulmonary nodules."
            })

            // PROBABILISTIC BRANCH: 6% suspicious nodule
            .AddProbabilisticBranch(
                0.06, // 6% nodule detection rate (NLST trial)

                // TRUE PATH: Suspicious nodule - continue to diagnosis
                new CompositeState
                {
                    Name = "Suspicious_Nodule_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "suspicious_nodule"),
                        SetAttributeState.Set("nodule_size_mm", 18),

                        // Update LDCT conclusion
                        new DiagnosticReportState
                        {
                            Name = "LDCT_With_Nodule",
                            Code = CancerCodes.LDCT,
                            IsImagingReport = true,
                            Conclusion = "18mm spiculated nodule identified in right upper lobe. Lung-RADS 4B. Recommend tissue diagnosis."
                        },

                        // === PHASE 2: Diagnosis (T+2 weeks) ===
                        DelayState.Weeks(2),

                        new EncounterState { Name = "Diagnostic_Workup", Reason = "CT-guided lung biopsy" },

                        // CT-guided lung biopsy
                        new ProcedureState
                        {
                            Name = "Lung_Biopsy",
                            Code = CancerCodes.LungBiopsy,
                            Duration = TimeSpan.FromMinutes(45),
                            BodySite = "Right upper lobe of lung",
                            Category = "diagnostic",
                            Outcome = "CT-guided core needle biopsy of right upper lobe nodule completed. Specimen sent to pathology.",
                            FollowUp = "Pathology results expected in 3-5 days."
                        },

                        // Wait for pathology
                        DelayState.Days(5),

                        // NESTED BRANCH: 70% malignant
                        ProbabilisticBranchState.Binary(
                            0.70, // 70% malignancy rate in biopsied nodules

                            // MALIGNANT PATH
                            new CompositeState
                            {
                                Name = "Malignant_Nodule_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "malignant"),

                                    new EncounterState { Name = "Pathology_Review", Reason = "Lung cancer diagnosis discussion" },

                                    // Pathology report
                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Malignant",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Adenocarcinoma of lung, moderately differentiated. No necrosis. Recommend staging workup and oncology consultation."
                                    },

                                    // Cancer diagnosis
                                    new ConditionOnsetState
                                    {
                                        Name = "Lung_Cancer_Diagnosis",
                                        Code = CancerCodes.LungCancer,
                                        Severity = 3,
                                        AssignToAttribute = "lung_cancer_condition",
                                        Category = "encounter-diagnosis"
                                    },

                                    SetAttributeState.Set("pathway_phase", "staging"),
                                    SetAttributeState.Set("histology", "adenocarcinoma"),

                                    // === PHASE 3: Staging (T+1 week) ===
                                    DelayState.Weeks(1),

                                    new EncounterState { Name = "Staging_Workup", Reason = "Lung cancer staging" },

                                    // PET/CT whole body
                                    new DiagnosticReportState
                                    {
                                        Name = "PET_CT_Staging",
                                        Code = CancerCodes.PETCT,
                                        IsImagingReport = true,
                                        Conclusion = "PET/CT: Hypermetabolic nodule in right upper lobe (SUV 8.5). Ipsilateral hilar lymph node uptake noted (SUV 4.2). No distant metastases."
                                    },

                                    // Brain MRI (standard for lung cancer staging)
                                    new DiagnosticReportState
                                    {
                                        Name = "Brain_MRI",
                                        Code = new FhirCode(FhirCode.Systems.Loinc, "24556-7", "MRI Brain"),
                                        IsImagingReport = true,
                                        Conclusion = "MRI Brain: No evidence of intracranial metastatic disease."
                                    },

                                    // Stage assignment (30% early, 70% advanced)
                                    ProbabilisticBranchState.Binary(
                                        0.30, // 30% early stage (I-II)

                                        // Early stage (I-II) - Surgery
                                        new CompositeState
                                        {
                                            Name = "Early_Stage_Lung_Cancer",
                                            States =
                                            [
                                                SetAttributeState.Set("cancer_stage", "IB"),
                                                SetAttributeState.Set("lymph_nodes_positive", 0),

                                                // === PHASE 4: Treatment - Lobectomy ===
                                                DelayState.Weeks(2),
                                                new EncounterState { Name = "Surgery_Encounter", Reason = "Video-assisted thoracoscopic surgery (VATS) lobectomy", DurationMinutes = 240 },

                                                new ProcedureState
                                                {
                                                    Name = "Lobectomy_Procedure",
                                                    Code = CancerCodes.Lobectomy,
                                                    Duration = TimeSpan.FromHours(3),
                                                    BodySite = "Right upper lobe of lung",
                                                    Category = "surgery",
                                                    Outcome = "VATS right upper lobectomy with mediastinal lymph node dissection completed. Margins clear. Pathologic stage IB (T2aN0M0).",
                                                    FollowUp = "Post-operative follow-up in 2 weeks. Adjuvant therapy evaluation."
                                                },

                                                // Recovery and follow-up
                                                DelayState.Weeks(4),
                                                new EncounterState { Name = "Post_Op_Follow_Up", Reason = "Post-surgical evaluation" },

                                                SetAttributeState.Set("pathway_phase", "surveillance")
                                            ]
                                        },

                                        // Advanced stage (III-IV) - Chemotherapy + Radiation
                                        new CompositeState
                                        {
                                            Name = "Advanced_Stage_Lung_Cancer",
                                            States =
                                            [
                                                SetAttributeState.Set("cancer_stage", "IIIB"),
                                                SetAttributeState.Set("lymph_nodes_positive", 3),

                                                // === PHASE 4: Treatment - Chemotherapy ===
                                                DelayState.Weeks(2),
                                                new EncounterState { Name = "Chemo_Consultation", Reason = "Chemotherapy treatment planning" },

                                                // Cisplatin + Paclitaxel regimen
                                                new MedicationOrderState
                                                {
                                                    Name = "Cisplatin_Order",
                                                    Code = CancerCodes.Cisplatin,
                                                    IsChronic = false,
                                                    DurationDays = 84,
                                                    DosageInstructions = "75 mg/m2 IV day 1, every 21 days for 4 cycles"
                                                },

                                                new MedicationOrderState
                                                {
                                                    Name = "Paclitaxel_Order",
                                                    Code = CancerCodes.Paclitaxel,
                                                    IsChronic = false,
                                                    DurationDays = 84,
                                                    DosageInstructions = "175 mg/m2 IV day 1, every 21 days for 4 cycles"
                                                },

                                                // Concurrent radiation therapy
                                                DelayState.Weeks(1),
                                                new EncounterState { Name = "Radiation_Planning", Reason = "Radiation therapy consultation" },

                                                new ProcedureState
                                                {
                                                    Name = "Radiation_Therapy",
                                                    Code = CancerCodes.RadiationTherapy,
                                                    Duration = TimeSpan.FromMinutes(15),
                                                    BodySite = "Right upper lobe and mediastinum",
                                                    Category = "therapeutic",
                                                    Outcome = "Concurrent chemoradiation initiated. Treatment plan: 60 Gy in 30 fractions over 6 weeks.",
                                                    Note = "Concurrent with chemotherapy"
                                                },

                                                // Follow-up during treatment
                                                DelayState.Weeks(12),
                                                new EncounterState { Name = "Mid_Treatment_Assessment", Reason = "Treatment response evaluation" },

                                                new DiagnosticReportState
                                                {
                                                    Name = "Response_Assessment_CT",
                                                    Code = CancerCodes.CTChest,
                                                    IsImagingReport = true,
                                                    Conclusion = "CT Chest: Partial response to therapy. Primary tumor decreased in size. No new lesions."
                                                },

                                                SetAttributeState.Set("pathway_phase", "active_treatment")
                                            ]
                                        }
                                    )
                                ]
                            },

                            // BENIGN PATH
                            new CompositeState
                            {
                                Name = "Benign_Nodule_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "benign"),

                                    new EncounterState { Name = "Pathology_Review", Reason = "Benign nodule results discussion" },

                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Benign",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Benign inflammatory nodule. No evidence of malignancy. Recommend CT surveillance in 3 months."
                                    },

                                    SetAttributeState.Set("pathway_phase", "surveillance"),
                                    SetAttributeState.Set("next_ct_months", 3)
                                ]
                            }
                        )
                    ]
                },

                // FALSE PATH: No nodule detected
                new CompositeState
                {
                    Name = "Normal_LDCT_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "normal"),

                        new DiagnosticReportState
                        {
                            Name = "Normal_LDCT_Report",
                            Code = CancerCodes.LDCT,
                            IsImagingReport = true,
                            Conclusion = "Lung-RADS 1: Negative. No pulmonary nodules detected. Continue annual LDCT screening."
                        },

                        SetAttributeState.Set("pathway_phase", "screening_complete"),
                        SetAttributeState.Set("next_ldct_months", 12)
                    ]
                }
            )

            .Build();
    }

    #endregion

    #region Prostate Cancer Pathway

    /// <summary>
    /// Generates a complete prostate cancer screening to treatment pathway scenario.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This scenario models the full prostate cancer care continuum:
    /// </para>
    /// <para>
    /// <strong>Phase 1 - Screening (T+0):</strong>
    /// <list type="bullet">
    /// <item><description>PSA blood test</description></item>
    /// <item><description>15% probability of elevated PSA (&gt;4.0 ng/mL)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 2 - Diagnosis (if elevated PSA):</strong>
    /// <list type="bullet">
    /// <item><description>Prostate MRI</description></item>
    /// <item><description>Transrectal ultrasound-guided biopsy (TRUS)</description></item>
    /// <item><description>30% of biopsies are positive for cancer</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 3 - Staging (T+1 week if malignant):</strong>
    /// <list type="bullet">
    /// <item><description>Bone scan for metastases</description></item>
    /// <item><description>CT abdomen/pelvis</description></item>
    /// <item><description>Gleason score and stage assignment</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Phase 4 - Treatment based on risk:</strong>
    /// <list type="bullet">
    /// <item><description>Low-risk: Active surveillance</description></item>
    /// <item><description>Intermediate-risk: Radical prostatectomy or radiation</description></item>
    /// <item><description>High-risk: Surgery/radiation + hormone therapy</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Evidence Base:</strong>
    /// <list type="bullet">
    /// <item><description>Elevated PSA rate: ~15% in screening population (USPSTF)</description></item>
    /// <item><description>Biopsy cancer detection rate: ~30% (AUA guidelines)</description></item>
    /// <item><description>Risk stratification: Low 40%, Intermediate 35%, High 25% (NCCN)</description></item>
    /// <item><description>Screening age: 55-70 years (shared decision-making)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 62).</param>
    /// <returns>A complete scenario context with prostate cancer care pathway.</returns>
    public static ScenarioContext GetProstateCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 62)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        return new ScenarioBuilder(schemaProvider)
            .WithName("Prostate Cancer Screening to Treatment Pathway")
            .WithDescription("Complete prostate cancer care continuum from PSA screening through diagnosis, staging, and treatment including surgery, radiation, and hormone therapy.")

            // Initial patient setup (male only)
            .WithPatient(age: age, gender: "male")

            // === PHASE 0: Healthcare Infrastructure ===
            .AddOrganization(OrganizationState.SpecialtyClinic("Urology", "Urologic Oncology Center"))
            .AddPractitioner(new PractitionerState
            {
                Name = "Urologist",
                Specialty = Specialties.Urology
            })
            .AddPractitioner(new PractitionerState
            {
                Name = "Oncologist",
                Specialty = Specialties.Oncology,
                Qualifications = ["ABIM Board Certified - Medical Oncology"]
            })

            // === PHASE 1: Screening (T+0) ===
            .AddEncounter("Annual prostate cancer screening")

            // Store initial state
            .SetAttribute("cancer_type", "prostate")
            .SetAttribute("pathway_phase", "screening")

            // PSA test
            .AddObservation(new ObservationState
            {
                Name = "PSA_Screening",
                Code = CancerCodes.PSA,
                Value = 3.2m,
                Unit = "ng/mL",
                UnitCode = "ng/mL"
            })

            // PROBABILISTIC BRANCH: 15% elevated PSA
            .AddProbabilisticBranch(
                0.15, // 15% elevated PSA rate

                // TRUE PATH: Elevated PSA
                new CompositeState
                {
                    Name = "Elevated_PSA_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "elevated_psa"),

                        // Update PSA to elevated value
                        new ObservationState
                        {
                            Name = "PSA_Elevated",
                            Code = CancerCodes.PSA,
                            Value = 6.8m,
                            Unit = "ng/mL",
                            UnitCode = "ng/mL"
                        },

                        SetAttributeState.Set("psa_value", 6.8m),

                        // === PHASE 2: Diagnosis (T+3 weeks) ===
                        DelayState.Weeks(3),

                        new EncounterState { Name = "Elevated_PSA_Followup", Reason = "Elevated PSA workup" },

                        // Prostate MRI
                        new DiagnosticReportState
                        {
                            Name = "Prostate_MRI",
                            Code = new FhirCode(FhirCode.Systems.Loinc, "79103-8", "MRI Prostate"),
                            IsImagingReport = true,
                            Conclusion = "Multiparametric MRI prostate: 12mm lesion in right peripheral zone. PI-RADS 4. Recommend biopsy."
                        },

                        // TRUS-guided biopsy
                        DelayState.Weeks(2),
                        new EncounterState { Name = "Prostate_Biopsy_Encounter", Reason = "Transrectal prostate biopsy" },

                        new ProcedureState
                        {
                            Name = "Prostate_Biopsy",
                            Code = CancerCodes.ProstateBiopsy,
                            Duration = TimeSpan.FromMinutes(30),
                            BodySite = "Prostate",
                            Category = "diagnostic",
                            Outcome = "12-core systematic TRUS-guided prostate biopsy performed. Specimens sent to pathology.",
                            FollowUp = "Pathology results in 5-7 days."
                        },

                        // Wait for pathology
                        DelayState.Days(7),

                        // NESTED BRANCH: 30% malignant
                        ProbabilisticBranchState.Binary(
                            0.30, // 30% cancer detection rate in biopsies

                            // MALIGNANT PATH
                            new CompositeState
                            {
                                Name = "Malignant_Biopsy_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "malignant"),

                                    new EncounterState { Name = "Pathology_Review", Reason = "Prostate cancer diagnosis discussion" },

                                    // Pathology report
                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Malignant",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Adenocarcinoma of prostate. Gleason score 7 (3+4). 4 of 12 cores positive. Recommend staging workup."
                                    },

                                    // Cancer diagnosis
                                    new ConditionOnsetState
                                    {
                                        Name = "Prostate_Cancer_Diagnosis",
                                        Code = CancerCodes.ProstateCancer,
                                        Severity = 2,
                                        AssignToAttribute = "prostate_cancer_condition",
                                        Category = "encounter-diagnosis"
                                    },

                                    SetAttributeState.Set("pathway_phase", "staging"),
                                    SetAttributeState.Set("gleason_score", 7),

                                    // === PHASE 3: Staging (T+1 week) ===
                                    DelayState.Weeks(1),

                                    new EncounterState { Name = "Staging_Workup", Reason = "Prostate cancer staging" },

                                    // Bone scan
                                    new ProcedureState
                                    {
                                        Name = "Bone_Scan",
                                        Code = CancerCodes.BoneScan,
                                        Duration = TimeSpan.FromHours(3),
                                        Category = "diagnostic",
                                        Outcome = "Whole body bone scan: No evidence of osseous metastatic disease."
                                    },

                                    // CT abdomen/pelvis
                                    new DiagnosticReportState
                                    {
                                        Name = "CT_Staging",
                                        Code = CancerCodes.CTAbdomenPelvis,
                                        IsImagingReport = true,
                                        Conclusion = "CT staging: No evidence of lymphadenopathy or distant metastases. Prostate enlargement noted."
                                    },

                                    // Risk stratification (Low 40%, Intermediate 35%, High 25%)
                                    new ProbabilisticBranchState
                                    {
                                        Name = "Risk_Stratification",
                                        Branches =
                                        [
                                            (0.40, new CompositeState
                                            {
                                                Name = "Low_Risk_Prostate_Cancer",
                                                States =
                                                [
                                                    SetAttributeState.Set("risk_category", "low"),
                                                    SetAttributeState.Set("cancer_stage", "T1c"),

                                                    // === PHASE 4: Treatment - Active Surveillance ===
                                                    DelayState.Weeks(2),
                                                    new EncounterState { Name = "Treatment_Planning", Reason = "Active surveillance counseling" },

                                                    SetAttributeState.Set("treatment_plan", "active_surveillance"),

                                                    // First surveillance PSA (3 months)
                                                    DelayState.Months(3),
                                                    new EncounterState { Name = "Surveillance_Visit", Reason = "Active surveillance follow-up" },

                                                    new ObservationState
                                                    {
                                                        Name = "PSA_Surveillance",
                                                        Code = CancerCodes.PSA,
                                                        Value = 6.5m,
                                                        Unit = "ng/mL",
                                                        UnitCode = "ng/mL"
                                                    },

                                                    SetAttributeState.Set("pathway_phase", "active_surveillance")
                                                ]
                                            }),
                                            (0.35, new CompositeState
                                            {
                                                Name = "Intermediate_Risk_Prostate_Cancer",
                                                States =
                                                [
                                                    SetAttributeState.Set("risk_category", "intermediate"),
                                                    SetAttributeState.Set("cancer_stage", "T2b"),

                                                    // === PHASE 4: Treatment - Radical Prostatectomy ===
                                                    DelayState.Weeks(3),
                                                    new EncounterState { Name = "Surgery_Encounter", Reason = "Robot-assisted radical prostatectomy", DurationMinutes = 240 },

                                                    new ProcedureState
                                                    {
                                                        Name = "Radical_Prostatectomy",
                                                        Code = CancerCodes.RadicalProstatectomy,
                                                        Duration = TimeSpan.FromHours(3),
                                                        BodySite = "Prostate",
                                                        Category = "surgery",
                                                        Outcome = "Robot-assisted laparoscopic radical prostatectomy with pelvic lymph node dissection. Margins negative. Pathologic stage T2c.",
                                                        FollowUp = "Post-operative PSA in 6 weeks."
                                                    },

                                                    // Post-op follow-up
                                                    DelayState.Weeks(6),
                                                    new EncounterState { Name = "Post_Op_Follow_Up", Reason = "Post-surgical PSA check" },

                                                    new ObservationState
                                                    {
                                                        Name = "PSA_Post_Surgery",
                                                        Code = CancerCodes.PSA,
                                                        Value = 0.1m,
                                                        Unit = "ng/mL",
                                                        UnitCode = "ng/mL"
                                                    },

                                                    SetAttributeState.Set("pathway_phase", "post_surgery_surveillance")
                                                ]
                                            }),
                                            (0.25, new CompositeState
                                            {
                                                Name = "High_Risk_Prostate_Cancer",
                                                States =
                                                [
                                                    SetAttributeState.Set("risk_category", "high"),
                                                    SetAttributeState.Set("cancer_stage", "T3a"),
                                                    SetAttributeState.Set("gleason_score", 9),

                                                    // === PHASE 4: Treatment - Radiation + Hormone Therapy ===
                                                    DelayState.Weeks(2),
                                                    new EncounterState { Name = "Treatment_Planning", Reason = "Radiation and hormone therapy consultation" },

                                                    // Androgen deprivation therapy (ADT)
                                                    new MedicationOrderState
                                                    {
                                                        Name = "Leuprolide_Order",
                                                        Code = CancerCodes.Leuprolide,
                                                        IsChronic = true,
                                                        DurationDays = 730,
                                                        DosageInstructions = "7.5 mg IM monthly for 24 months"
                                                    },

                                                    new MedicationOrderState
                                                    {
                                                        Name = "Bicalutamide_Order",
                                                        Code = CancerCodes.Bicalutamide,
                                                        IsChronic = true,
                                                        DurationDays = 730,
                                                        DosageInstructions = "50 mg PO daily for 24 months"
                                                    },

                                                    // Radiation therapy
                                                    DelayState.Weeks(4),
                                                    new EncounterState { Name = "Radiation_Planning", Reason = "External beam radiation therapy consultation" },

                                                    new ProcedureState
                                                    {
                                                        Name = "Radiation_Therapy",
                                                        Code = CancerCodes.RadiationTherapy,
                                                        Duration = TimeSpan.FromMinutes(15),
                                                        BodySite = "Prostate and pelvic lymph nodes",
                                                        Category = "therapeutic",
                                                        Outcome = "IMRT initiated. Treatment plan: 78 Gy to prostate in 39 fractions over 8 weeks.",
                                                        Note = "Concurrent with hormone therapy"
                                                    },

                                                    // Follow-up during treatment
                                                    DelayState.Weeks(12),
                                                    new EncounterState { Name = "Mid_Treatment_Assessment", Reason = "Treatment response evaluation" },

                                                    new ObservationState
                                                    {
                                                        Name = "PSA_During_Treatment",
                                                        Code = CancerCodes.PSA,
                                                        Value = 0.8m,
                                                        Unit = "ng/mL",
                                                        UnitCode = "ng/mL"
                                                    },

                                                    SetAttributeState.Set("pathway_phase", "active_treatment")
                                                ]
                                            })
                                        ]
                                    }
                                ]
                            },

                            // BENIGN PATH
                            new CompositeState
                            {
                                Name = "Benign_Biopsy_Path",
                                States =
                                [
                                    SetAttributeState.Set("pathology_result", "benign"),

                                    new EncounterState { Name = "Pathology_Review", Reason = "Benign biopsy results discussion" },

                                    new DiagnosticReportState
                                    {
                                        Name = "Pathology_Benign",
                                        Code = CancerCodes.PathologyReport,
                                        Category = "PATH",
                                        Conclusion = "Benign prostatic hyperplasia. No evidence of malignancy. Recommend PSA surveillance annually."
                                    },

                                    SetAttributeState.Set("pathway_phase", "psa_surveillance"),
                                    SetAttributeState.Set("next_psa_months", 12)
                                ]
                            }
                        )
                    ]
                },

                // FALSE PATH: Normal PSA
                new CompositeState
                {
                    Name = "Normal_PSA_Path",
                    States =
                    [
                        SetAttributeState.Set("screening_result", "normal_psa"),
                        SetAttributeState.Set("pathway_phase", "screening_complete"),
                        SetAttributeState.Set("next_psa_years", 2)
                    ]
                }
            )

            .Build();
    }

    #endregion

    #region Combined Cancer Screening Scenario

    /// <summary>
    /// Generates a multi-cancer screening scenario that includes both breast and colorectal cancer screenings.
    /// Useful for demonstrating comprehensive cancer screening in appropriate patient populations.
    /// </summary>
    /// <remarks>
    /// This scenario is appropriate for patients aged 50-75 who are eligible for both breast
    /// cancer screening (for female patients) and colorectal cancer screening (for all patients).
    /// </remarks>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (default: 55).</param>
    /// <param name="gender">Patient gender (affects which screenings are performed).</param>
    /// <returns>A complete scenario context with multi-cancer screening pathway.</returns>
    public static ScenarioContext GetComprehensiveCancerScreening(
        this IFhirSchemaProvider schemaProvider,
        int age = 55,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Comprehensive Cancer Screening")
            .WithDescription("Multi-cancer screening pathway including breast and colorectal cancer screenings as appropriate for patient demographics.")

            // Initial patient setup
            .WithPatient(age: age, gender: gender)

            // Healthcare infrastructure
            .AddOrganization(OrganizationState.Hospital("Comprehensive Cancer Screening Center"))
            .AddPractitioner(PractitionerState.FamilyPractitioner())
            .AddPractitioner(new PractitionerState
            {
                Name = "Gastroenterologist",
                Specialty = Specialties.Gastroenterology
            })

            // Initial wellness visit for cancer screening discussion
            .AddWellnessVisit("Cancer screening consultation")

            // Colorectal cancer screening (for all patients 45-75)
            .DelayWeeks(1)
            .AddEncounter("Colorectal cancer screening")
            .AddColonoscopy();

        // Add breast cancer screening for female patients
        if (gender.Equals("female", StringComparison.OrdinalIgnoreCase))
        {
            builder
                .AddPractitioner(new PractitionerState
                {
                    Name = "Radiologist",
                    Specialty = Specialties.Radiology,
                    Qualifications = ["ABR Board Certified - Diagnostic Radiology"]
                })
                .DelayWeeks(2)
                .AddEncounter("Breast cancer screening")
                .AddDiagnosticReport(new DiagnosticReportState
                {
                    Code = CancerCodes.MammogramScreening,
                    Conclusion = "BIRADS 1: Negative. Routine annual screening recommended."
                });
        }

        return builder
            .SetAttribute("screening_complete", true)
            .Build();
    }

    #endregion
}

