// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common service request codes for laboratory orders, imaging studies, and specialist referrals.
/// Organized by category to facilitate scenario building and testing.
/// </summary>
public static class ServiceRequestCodes
{
    /// <summary>
    /// Laboratory order codes (LOINC).
    /// These codes represent common laboratory tests ordered in clinical practice.
    /// </summary>
    public static class Laboratory
    {
        /// <summary>
        /// Complete Blood Count with differential - Measures blood cell counts and types.
        /// LOINC code: 58410-2
        /// </summary>
        public static FhirCode CBCWithDifferential { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "58410-2",
            Display: "CBC with differential");

        /// <summary>
        /// Comprehensive Metabolic Panel - Measures glucose, electrolytes, kidney and liver function.
        /// LOINC code: 24323-8
        /// </summary>
        public static FhirCode ComprehensiveMetabolicPanel { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "24323-8",
            Display: "Comprehensive metabolic panel");

        /// <summary>
        /// Lipid Panel - Measures cholesterol and triglyceride levels.
        /// LOINC code: 57698-3
        /// </summary>
        public static FhirCode LipidPanel { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "57698-3",
            Display: "Lipid panel");

        /// <summary>
        /// Hemoglobin A1c - Measures average blood glucose over 2-3 months.
        /// LOINC code: 4548-4
        /// </summary>
        public static FhirCode HemoglobinA1c { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "4548-4",
            Display: "Hemoglobin A1c");

        /// <summary>
        /// Thyroid Stimulating Hormone - Screens for thyroid disorders.
        /// LOINC code: 3016-3
        /// </summary>
        public static FhirCode TSH { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "3016-3",
            Display: "TSH");

        /// <summary>
        /// Prostate Specific Antigen - Prostate cancer screening marker.
        /// LOINC code: 2857-1
        /// </summary>
        public static FhirCode PSA { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "2857-1",
            Display: "PSA");

        /// <summary>
        /// Urinalysis - Tests urine for infection, kidney disease, and diabetes.
        /// LOINC code: 24356-8
        /// </summary>
        public static FhirCode Urinalysis { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "24356-8",
            Display: "Urinalysis");

        /// <summary>
        /// Basic Metabolic Panel - Measures glucose, electrolytes, and kidney function.
        /// LOINC code: 51990-0
        /// </summary>
        public static FhirCode BasicMetabolicPanel { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "51990-0",
            Display: "Basic metabolic panel");

        /// <summary>
        /// Liver Function Tests - Measures liver enzymes and function.
        /// LOINC code: 24325-3
        /// </summary>
        public static FhirCode LiverFunctionPanel { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "24325-3",
            Display: "Hepatic function panel");

        /// <summary>
        /// Prothrombin Time / INR - Measures blood clotting time.
        /// LOINC code: 5902-2
        /// </summary>
        public static FhirCode PTINR { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "5902-2",
            Display: "PT/INR");

        /// <summary>
        /// Blood Type and Screen - Determines blood type and antibodies.
        /// LOINC code: 882-1
        /// </summary>
        public static FhirCode BloodTypeAndScreen { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "882-1",
            Display: "Blood type and screen");

        /// <summary>
        /// Vitamin D level - Measures 25-hydroxyvitamin D.
        /// LOINC code: 1989-3
        /// </summary>
        public static FhirCode VitaminD { get; } = new(
            System: FhirCode.Systems.Loinc,
            Code: "1989-3",
            Display: "Vitamin D level");
    }

    /// <summary>
    /// Imaging order codes (SNOMED CT).
    /// These codes represent common imaging studies ordered in clinical practice.
    /// </summary>
    public static class ImagingStudies
    {
        /// <summary>
        /// Chest X-ray - Standard chest radiograph.
        /// SNOMED CT code: 399208008
        /// </summary>
        public static FhirCode ChestXRay { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "399208008",
            Display: "Chest X-ray");

        /// <summary>
        /// CT Chest - Computed tomography of chest.
        /// SNOMED CT code: 241540006
        /// </summary>
        public static FhirCode CTChest { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "241540006",
            Display: "CT of chest");

        /// <summary>
        /// MRI Brain - Magnetic resonance imaging of brain.
        /// SNOMED CT code: 241684001
        /// </summary>
        public static FhirCode MRIBrain { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "241684001",
            Display: "MRI of brain");

        /// <summary>
        /// Ultrasound Abdomen - Abdominal ultrasonography.
        /// SNOMED CT code: 241490004
        /// </summary>
        public static FhirCode UltrasoundAbdomen { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "241490004",
            Display: "Ultrasound of abdomen");

        /// <summary>
        /// Mammogram - Breast cancer screening imaging.
        /// SNOMED CT code: 71651007
        /// </summary>
        public static FhirCode Mammogram { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "71651007",
            Display: "Mammography");

        /// <summary>
        /// Bone Density Scan (DEXA) - Dual-energy X-ray absorptiometry.
        /// SNOMED CT code: 312681000
        /// </summary>
        public static FhirCode BoneDensityScan { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "312681000",
            Display: "Bone density scan");

        /// <summary>
        /// CT Abdomen - Computed tomography of abdomen.
        /// SNOMED CT code: 241485003
        /// </summary>
        public static FhirCode CTAbdomen { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "241485003",
            Display: "CT of abdomen");

        /// <summary>
        /// MRI Spine - Magnetic resonance imaging of spine.
        /// SNOMED CT code: 241635003
        /// </summary>
        public static FhirCode MRISpine { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "241635003",
            Display: "MRI of spine");

        /// <summary>
        /// Echocardiogram - Ultrasound of the heart.
        /// SNOMED CT code: 40701008
        /// </summary>
        public static FhirCode Echocardiogram { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "40701008",
            Display: "Echocardiography");

        /// <summary>
        /// Nuclear stress test - Myocardial perfusion imaging.
        /// SNOMED CT code: 252416005
        /// </summary>
        public static FhirCode NuclearStressTest { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "252416005",
            Display: "Myocardial perfusion imaging");
    }

    /// <summary>
    /// Specialist referral and consultation codes (SNOMED CT).
    /// These codes represent common referrals to medical specialists.
    /// </summary>
    public static class Referrals
    {
        /// <summary>
        /// Cardiology consultation - Referral to heart specialist.
        /// SNOMED CT code: 183524002
        /// </summary>
        public static FhirCode CardiologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183524002",
            Display: "Cardiology consultation");

        /// <summary>
        /// Orthopedic consultation - Referral to bone/joint specialist.
        /// SNOMED CT code: 183516009
        /// </summary>
        public static FhirCode OrthopedicConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183516009",
            Display: "Orthopedic consultation");

        /// <summary>
        /// Psychiatry consultation - Referral to mental health specialist.
        /// SNOMED CT code: 183521005
        /// </summary>
        public static FhirCode PsychiatryConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183521005",
            Display: "Psychiatry consultation");

        /// <summary>
        /// Physical therapy referral - Referral for rehabilitation services.
        /// SNOMED CT code: 183523008
        /// </summary>
        public static FhirCode PhysicalTherapy { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183523008",
            Display: "Physical therapy referral");

        /// <summary>
        /// Endocrinology consultation - Referral to hormone specialist.
        /// SNOMED CT code: 183515008
        /// </summary>
        public static FhirCode EndocrinologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183515008",
            Display: "Endocrinology consultation");

        /// <summary>
        /// Gastroenterology consultation - Referral to digestive specialist.
        /// SNOMED CT code: 183522003
        /// </summary>
        public static FhirCode GastroenterologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183522003",
            Display: "Gastroenterology consultation");

        /// <summary>
        /// Neurology consultation - Referral to brain/nerve specialist.
        /// SNOMED CT code: 183519007
        /// </summary>
        public static FhirCode NeurologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183519007",
            Display: "Neurology consultation");

        /// <summary>
        /// Pulmonology consultation - Referral to lung specialist.
        /// SNOMED CT code: 183517000
        /// </summary>
        public static FhirCode PulmonologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183517000",
            Display: "Pulmonology consultation");

        /// <summary>
        /// Dermatology consultation - Referral to skin specialist.
        /// SNOMED CT code: 183520001
        /// </summary>
        public static FhirCode DermatologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183520001",
            Display: "Dermatology consultation");

        /// <summary>
        /// Ophthalmology consultation - Referral to eye specialist.
        /// SNOMED CT code: 183518005
        /// </summary>
        public static FhirCode OphthalmologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183518005",
            Display: "Ophthalmology consultation");

        /// <summary>
        /// Oncology consultation - Referral to cancer specialist.
        /// SNOMED CT code: 183525001
        /// </summary>
        public static FhirCode OncologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183525001",
            Display: "Oncology consultation");

        /// <summary>
        /// Nephrology consultation - Referral to kidney specialist.
        /// SNOMED CT code: 183514007
        /// </summary>
        public static FhirCode NephrologyConsult { get; } = new(
            System: FhirCode.Systems.SnomedCt,
            Code: "183514007",
            Display: "Nephrology consultation");
    }
}
