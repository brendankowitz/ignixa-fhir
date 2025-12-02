// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Common laboratory observation codes (LOINC).
/// Organized by test panels: Comprehensive Metabolic Panel (CMP), Complete Blood Count (CBC),
/// Lipid Panel, and other common tests.
/// </summary>
public static class LabObservations
{
    // Comprehensive Metabolic Panel (CMP) - Basic Chemistry

    /// <summary>Glucose - Serum or plasma (fasting: 70-100 mg/dL)</summary>
    public static FhirCode Glucose { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2339-0",
        Display: "Glucose [Mass/volume] in Blood");

    /// <summary>Sodium - Serum or plasma (135-145 mmol/L)</summary>
    public static FhirCode Sodium { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2951-2",
        Display: "Sodium [Moles/volume] in Serum or Plasma");

    /// <summary>Potassium - Serum or plasma (3.5-5.0 mmol/L)</summary>
    public static FhirCode Potassium { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2823-3",
        Display: "Potassium [Moles/volume] in Serum or Plasma");

    /// <summary>Chloride - Serum or plasma (96-106 mmol/L)</summary>
    public static FhirCode Chloride { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2075-0",
        Display: "Chloride [Moles/volume] in Serum or Plasma");

    /// <summary>Carbon dioxide (CO2) - Serum or plasma (23-29 mmol/L)</summary>
    public static FhirCode CarbonDioxide { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2028-9",
        Display: "Carbon dioxide, total [Moles/volume] in Serum or Plasma");

    /// <summary>Blood urea nitrogen (BUN) - Serum or plasma (7-20 mg/dL)</summary>
    public static FhirCode BUN { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "3094-0",
        Display: "Urea nitrogen [Mass/volume] in Serum or Plasma");

    /// <summary>Creatinine - Serum or plasma (0.6-1.2 mg/dL)</summary>
    public static FhirCode Creatinine { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2160-0",
        Display: "Creatinine [Mass/volume] in Serum or Plasma");

    /// <summary>Calcium - Serum or plasma (8.5-10.5 mg/dL)</summary>
    public static FhirCode Calcium { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "17861-6",
        Display: "Calcium [Mass/volume] in Serum or Plasma");

    /// <summary>Total protein - Serum or plasma (6.0-8.3 g/dL)</summary>
    public static FhirCode TotalProtein { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2885-2",
        Display: "Protein [Mass/volume] in Serum or Plasma");

    /// <summary>Albumin - Serum or plasma (3.5-5.5 g/dL)</summary>
    public static FhirCode Albumin { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1751-7",
        Display: "Albumin [Mass/volume] in Serum or Plasma");

    /// <summary>Bilirubin, total - Serum or plasma (0.1-1.2 mg/dL)</summary>
    public static FhirCode BilirubinTotal { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1975-2",
        Display: "Bilirubin.total [Mass/volume] in Serum or Plasma");

    /// <summary>Alkaline phosphatase (ALP) - Serum or plasma (44-147 U/L)</summary>
    public static FhirCode AlkalinePhosphatase { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6768-6",
        Display: "Alkaline phosphatase [Enzymatic activity/volume] in Serum or Plasma");

    /// <summary>Alanine aminotransferase (ALT/SGPT) - Serum or plasma (7-56 U/L)</summary>
    public static FhirCode ALT { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1742-6",
        Display: "Alanine aminotransferase [Enzymatic activity/volume] in Serum or Plasma");

    /// <summary>Aspartate aminotransferase (AST/SGOT) - Serum or plasma (10-40 U/L)</summary>
    public static FhirCode AST { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1920-8",
        Display: "Aspartate aminotransferase [Enzymatic activity/volume] in Serum or Plasma");

    // Complete Blood Count (CBC)

    /// <summary>Hemoglobin - Blood (12-18 g/dL)</summary>
    public static FhirCode Hemoglobin { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "718-7",
        Display: "Hemoglobin [Mass/volume] in Blood");

    /// <summary>Hematocrit - Blood (37-52%)</summary>
    public static FhirCode Hematocrit { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "4544-3",
        Display: "Hematocrit [Volume Fraction] of Blood");

    /// <summary>White blood cell (WBC) count - Blood (4.5-11.0 x10³/µL)</summary>
    public static FhirCode WBC { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6690-2",
        Display: "Leukocytes [#/volume] in Blood");

    /// <summary>Red blood cell (RBC) count - Blood (4.2-6.1 x10⁶/µL)</summary>
    public static FhirCode RBC { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "789-8",
        Display: "Erythrocytes [#/volume] in Blood");

    /// <summary>Platelet count - Blood (150-400 x10³/µL)</summary>
    public static FhirCode Platelets { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "777-3",
        Display: "Platelets [#/volume] in Blood");

    /// <summary>Mean corpuscular volume (MCV) - Blood (80-100 fL)</summary>
    public static FhirCode MCV { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "787-2",
        Display: "Mean corpuscular volume [Entitic volume]");

    /// <summary>Mean corpuscular hemoglobin (MCH) - Blood (27-31 pg)</summary>
    public static FhirCode MCH { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "785-6",
        Display: "Mean corpuscular hemoglobin [Entitic mass]");

    /// <summary>Mean corpuscular hemoglobin concentration (MCHC) - Blood (32-36 g/dL)</summary>
    public static FhirCode MCHC { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "786-4",
        Display: "Mean corpuscular hemoglobin concentration [Mass/volume]");

    // Lipid Panel

    /// <summary>Total cholesterol - Serum or plasma (&lt;200 mg/dL desirable)</summary>
    public static FhirCode TotalCholesterol { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2093-3",
        Display: "Cholesterol [Mass/volume] in Serum or Plasma");

    /// <summary>HDL cholesterol (good cholesterol) - Serum or plasma (&gt;40 mg/dL desirable)</summary>
    public static FhirCode HDLCholesterol { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2085-9",
        Display: "Cholesterol in HDL [Mass/volume] in Serum or Plasma");

    /// <summary>LDL cholesterol (bad cholesterol) - Serum or plasma (&lt;100 mg/dL optimal)</summary>
    public static FhirCode LDLCholesterol { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2089-1",
        Display: "Cholesterol in LDL [Mass/volume] in Serum or Plasma");

    /// <summary>Triglycerides - Serum or plasma (&lt;150 mg/dL normal)</summary>
    public static FhirCode Triglycerides { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2571-8",
        Display: "Triglyceride [Mass/volume] in Serum or Plasma");

    // Other Common Tests

    /// <summary>Hemoglobin A1c (HbA1c) - Blood (&lt;5.7% normal, 5.7-6.4% prediabetic, ≥6.5% diabetic)</summary>
    public static FhirCode HemoglobinA1c { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "4548-4",
        Display: "Hemoglobin A1c/Hemoglobin.total in Blood");

    /// <summary>Thyroid stimulating hormone (TSH) - Serum or plasma (0.4-4.0 mIU/L)</summary>
    public static FhirCode TSH { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "3016-3",
        Display: "Thyrotropin [Units/volume] in Serum or Plasma");

    /// <summary>Prostate specific antigen (PSA) - Serum or plasma (&lt;4.0 ng/mL normal)</summary>
    public static FhirCode PSA { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "2857-1",
        Display: "Prostate specific Ag [Mass/volume] in Serum or Plasma");

    /// <summary>Glomerular filtration rate (eGFR) - Serum or plasma (&gt;60 mL/min/1.73m² normal)</summary>
    public static FhirCode eGFR { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "33914-3",
        Display: "Glomerular filtration rate/1.73 sq M.predicted [Volume Rate/Area] in Serum or Plasma by Creatinine-based formula (MDRD)");

    /// <summary>C-reactive protein (CRP) - Serum or plasma (&lt;3.0 mg/L normal)</summary>
    public static FhirCode CRP { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1988-5",
        Display: "C reactive protein [Mass/volume] in Serum or Plasma");

    /// <summary>Vitamin D, 25-hydroxy - Serum or plasma (20-50 ng/mL sufficient)</summary>
    public static FhirCode VitaminD { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "1989-3",
        Display: "Vitamin D [Mass/volume] in Serum or Plasma");

    /// <summary>International normalized ratio (INR) - Platelet poor plasma (0.9-1.1 normal)</summary>
    public static FhirCode INR { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6301-6",
        Display: "INR in Platelet poor plasma by Coagulation assay");

    /// <summary>Prothrombin time (PT) - Platelet poor plasma (11-13.5 seconds)</summary>
    public static FhirCode ProthrombinTime { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "5902-2",
        Display: "Prothrombin time (PT)");

    /// <summary>Partial thromboplastin time (PTT) - Platelet poor plasma (25-35 seconds)</summary>
    public static FhirCode PTT { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "3173-2",
        Display: "Activated partial thromboplastin time (aPTT)");

    // Allergy Testing (IgE Antibodies)

    /// <summary>Peanut IgE antibody - Serum (&lt;0.35 kU/L negative)</summary>
    public static FhirCode PeanutIgE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6206-7",
        Display: "Peanut IgE Ab [Units/volume] in Serum");

    /// <summary>Walnut (tree nut) IgE antibody - Serum (&lt;0.35 kU/L negative)</summary>
    public static FhirCode WalnutIgE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6273-7",
        Display: "Walnut IgE Ab [Units/volume] in Serum");

    /// <summary>Fish IgE antibody - Serum (&lt;0.35 kU/L negative)</summary>
    public static FhirCode FishIgE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6082-2",
        Display: "Codfish IgE Ab [Units/volume] in Serum");

    /// <summary>Shellfish (shrimp) IgE antibody - Serum (&lt;0.35 kU/L negative)</summary>
    public static FhirCode ShrimpIgE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6246-3",
        Display: "Shrimp IgE Ab [Units/volume] in Serum");

    /// <summary>Wheat IgE antibody - Serum (&lt;0.35 kU/L negative)</summary>
    public static FhirCode WheatIgE { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "6276-0",
        Display: "Wheat IgE Ab [Units/volume] in Serum");

    // Urinalysis

    /// <summary>Leukocyte esterase - Urine (negative normal)</summary>
    public static FhirCode LeukocyteEsterase { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "5799-2",
        Display: "Leukocyte esterase [Presence] in Urine");

    /// <summary>Nitrite - Urine (negative normal)</summary>
    public static FhirCode Nitrite { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "5802-4",
        Display: "Nitrite [Presence] in Urine");

    /// <summary>Bacteria - Urine (none normal)</summary>
    public static FhirCode Bacteria { get; } = new(
        System: FhirCode.Systems.Loinc,
        Code: "25145-4",
        Display: "Bacteria [Presence] in Urine");
}
