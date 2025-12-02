// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common diagnostic report type codes (LOINC).
/// These codes identify laboratory panels, imaging reports, and other diagnostic studies.
/// </summary>
public static class DiagnosticReports
{
    // Laboratory Panels

    /// <summary>Comprehensive Metabolic Panel (CMP) - 14 chemistry tests</summary>
    public static FhirCode ComprehensiveMetabolicPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24323-8",
        Display: "Comprehensive metabolic 2000 panel - Serum or Plasma");

    /// <summary>Basic Metabolic Panel (BMP) - 8 chemistry tests</summary>
    public static FhirCode BasicMetabolicPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "51990-0",
        Display: "Basic metabolic panel - Blood");

    /// <summary>Complete Blood Count (CBC) with differential</summary>
    public static FhirCode CompleteBloodCount { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "58410-2",
        Display: "Complete blood count (CBC) panel - Blood by Automated count");

    /// <summary>Lipid Panel - Cholesterol and triglycerides</summary>
    public static FhirCode LipidPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24331-1",
        Display: "Lipid 1996 panel - Serum or Plasma");

    /// <summary>Liver Function Panel - Hepatic enzymes and proteins</summary>
    public static FhirCode LiverFunctionPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24325-3",
        Display: "Hepatic function panel - Serum or Plasma");

    /// <summary>Renal Function Panel - Kidney function tests</summary>
    public static FhirCode RenalFunctionPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24362-6",
        Display: "Renal function 2000 panel - Serum or Plasma");

    /// <summary>Thyroid Function Panel - TSH, T3, T4</summary>
    public static FhirCode ThyroidFunctionPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24348-5",
        Display: "Thyroid gland panel - Serum or Plasma");

    /// <summary>Coagulation Panel - PT, PTT, INR</summary>
    public static FhirCode CoagulationPanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "34714-6",
        Display: "Coagulation studies panel - Blood");

    /// <summary>Urinalysis Panel - Complete urine analysis</summary>
    public static FhirCode Urinalysis { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24356-8",
        Display: "Urinalysis complete panel - Urine");

    /// <summary>Electrolyte Panel - Sodium, potassium, chloride, CO2</summary>
    public static FhirCode ElectrolytePanel { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24326-1",
        Display: "Electrolytes 1998 panel - Serum or Plasma");

    // Imaging Reports

    /// <summary>Chest X-ray (2 views)</summary>
    public static FhirCode ChestXRay { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "30746-2",
        Display: "Chest 2 Views X-ray");

    /// <summary>CT scan of head without contrast</summary>
    public static FhirCode CTHeadWoContrast { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24727-0",
        Display: "CT Head WO contrast");

    /// <summary>CT scan of chest with contrast</summary>
    public static FhirCode CTChestWContrast { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24627-2",
        Display: "CT Chest W contrast IV");

    /// <summary>CT scan of abdomen and pelvis with contrast</summary>
    public static FhirCode CTAbdomenPelvisWContrast { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "79103-8",
        Display: "CT Abdomen and Pelvis W contrast IV");

    /// <summary>MRI of brain without contrast</summary>
    public static FhirCode MRIBrainWoContrast { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24553-0",
        Display: "MRI Brain WO contrast");

    /// <summary>MRI of spine (lumbar) without contrast</summary>
    public static FhirCode MRISpineLumbarWoContrast { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "36139-5",
        Display: "MRI Lumbar spine WO contrast");

    /// <summary>Ultrasound of abdomen</summary>
    public static FhirCode UltrasoundAbdomen { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24610-8",
        Display: "US Abdomen");

    /// <summary>Ultrasound of obstetric (pregnancy)</summary>
    public static FhirCode UltrasoundObstetric { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "11525-3",
        Display: "US Obstetric");

    /// <summary>Mammography - diagnostic bilateral</summary>
    public static FhirCode MammographyDiagnostic { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24604-1",
        Display: "MG Breast Diagnostic");

    /// <summary>Mammography - screening bilateral</summary>
    public static FhirCode MammographyScreening { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "24606-6",
        Display: "MG Breast Screening");

    /// <summary>Electrocardiogram (ECG/EKG) - 12 lead</summary>
    public static FhirCode ECG12Lead { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "11524-6",
        Display: "EKG study");

    /// <summary>Echocardiography - transthoracic</summary>
    public static FhirCode EchocardiographyTTE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "34552-0",
        Display: "Ultrasound Heart");

    // Specialized Tests

    /// <summary>Pathology report - surgical specimen</summary>
    public static FhirCode PathologySurgical { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "11526-1",
        Display: "Pathology study");

    /// <summary>Microbiology culture - blood</summary>
    public static FhirCode BloodCulture { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "600-7",
        Display: "Bacteria identified in Blood by Culture");

    /// <summary>Microbiology culture - urine</summary>
    public static FhirCode UrineCulture { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "630-4",
        Display: "Bacteria identified in Urine by Culture");

    /// <summary>COVID-19 PCR test</summary>
    public static FhirCode Covid19PCR { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "94500-6",
        Display: "SARS-CoV-2 (COVID-19) RNA [Presence] in Respiratory specimen by NAA with probe detection");

    /// <summary>COVID-19 antigen test</summary>
    public static FhirCode Covid19Antigen { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "94558-4",
        Display: "SARS-CoV-2 (COVID-19) Ag [Presence] in Respiratory specimen by Rapid immunoassay");

    /// <summary>Influenza A and B test</summary>
    public static FhirCode InfluenzaAB { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "80382-5",
        Display: "Influenza virus A and B and SARS-CoV-2 (COVID-19) identified in Respiratory specimen by NAA with probe detection");
}
