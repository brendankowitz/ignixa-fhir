// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.Codes;

/// <summary>
/// Represents a FHIR code with system, code, and display values.
/// Used for coding conditions, medications, observations, and other clinical concepts.
/// </summary>
/// <param name="System">The code system URI (e.g., "http://snomed.info/sct").</param>
/// <param name="Code">The code value (e.g., "44054006").</param>
/// <param name="Display">The human-readable display text (e.g., "Diabetes mellitus type 2").</param>
public record FhirCode(string System, string Code, string Display)
{
    /// <summary>
    /// Common code systems used in FHIR resources.
    /// </summary>
    public static class Systems
    {
        public const string SnomedCt = "http://snomed.info/sct";
        public const string Loinc = "http://loinc.org";
        public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";
        public const string Cvx = "http://hl7.org/fhir/sid/cvx";
        public const string Icd10 = "http://hl7.org/fhir/sid/icd-10";
        public const string Ucum = "http://unitsofmeasure.org";
        public const string EncounterType = "http://terminology.hl7.org/CodeSystem/v3-ActCode";
    }

    /// <summary>
    /// Common condition codes.
    /// </summary>
    public static class Conditions
    {
        public static readonly FhirCode DiabetesType2 = new(Systems.SnomedCt, "44054006", "Diabetes mellitus type 2");
        public static readonly FhirCode Prediabetes = new(Systems.SnomedCt, "714628002", "Prediabetes");
        public static readonly FhirCode Hypertension = new(Systems.SnomedCt, "38341003", "Hypertensive disorder");
        public static readonly FhirCode HypertensionEssential = new(Systems.SnomedCt, "59621000", "Essential hypertension");
        public static readonly FhirCode Hyperlipidemia = new(Systems.SnomedCt, "55822004", "Hyperlipidemia");
        public static readonly FhirCode Obesity = new(Systems.SnomedCt, "414915002", "Obesity");
        public static readonly FhirCode Pregnancy = new(Systems.SnomedCt, "77386006", "Pregnancy");
        public static readonly FhirCode Asthma = new(Systems.SnomedCt, "195967001", "Asthma");
        public static readonly FhirCode AllergicRhinitis = new(Systems.SnomedCt, "61582004", "Allergic rhinitis");
        public static readonly FhirCode PregnancyNormal = new(Systems.SnomedCt, "72892002", "Normal pregnancy");
        public static readonly FhirCode UrinaryTractInfection = new(Systems.SnomedCt, "68566005", "Urinary tract infectious disease");
        public static readonly FhirCode AcuteUpperRespiratoryInfection = new(Systems.SnomedCt, "54150009", "Upper respiratory infection");
        public static readonly FhirCode Appendicitis = new(Systems.SnomedCt, "74400008", "Appendicitis");
        public static readonly FhirCode VitaminDDeficiency = new(Systems.SnomedCt, "34713006", "Vitamin D deficiency");
    }

    /// <summary>
    /// Common observation codes (LOINC).
    /// </summary>
    public static class Observations
    {
        public static readonly FhirCode HemoglobinA1c = new(Systems.Loinc, "4548-4", "Hemoglobin A1c/Hemoglobin.total in Blood");
        public static readonly FhirCode BloodGlucose = new(Systems.Loinc, "2339-0", "Glucose [Mass/volume] in Blood");
        public static readonly FhirCode BloodPressureSystolic = new(Systems.Loinc, "8480-6", "Systolic blood pressure");
        public static readonly FhirCode BloodPressureDiastolic = new(Systems.Loinc, "8462-4", "Diastolic blood pressure");
        public static readonly FhirCode BloodPressurePanel = new(Systems.Loinc, "85354-9", "Blood pressure panel");
        public static readonly FhirCode BodyWeight = new(Systems.Loinc, "29463-7", "Body weight");
        public static readonly FhirCode BodyHeight = new(Systems.Loinc, "8302-2", "Body height");
        public static readonly FhirCode Bmi = new(Systems.Loinc, "39156-5", "Body mass index (BMI)");
        public static readonly FhirCode PeakExpiratoryFlowRate = new(Systems.Loinc, "33452-4", "Peak expiratory flow rate");
        public static readonly FhirCode FetalHeartRate = new(Systems.Loinc, "55283-6", "Fetal heart rate");
        public static readonly FhirCode BodyTemperature = new(Systems.Loinc, "8310-5", "Body temperature");
        public static readonly FhirCode PainSeverity = new(Systems.Loinc, "72514-3", "Pain severity - 0-10 verbal numeric rating [Score] - Reported");
    }

    /// <summary>
    /// Common medication codes (RxNorm).
    /// Organized by drug class: Diabetes, Hypertension, Pain Management, Antibiotics, and more.
    /// </summary>
    public static class Medications
    {
        // Diabetes

        /// <summary>Metformin 500mg tablet - First-line oral diabetes medication</summary>
        public static readonly FhirCode Metformin500mg = new(Systems.RxNorm, "860975", "Metformin hydrochloride 500 MG Oral Tablet");

        /// <summary>Metformin 1000mg tablet - First-line oral diabetes medication</summary>
        public static readonly FhirCode Metformin1000mg = new(Systems.RxNorm, "861007", "Metformin hydrochloride 1000 MG Oral Tablet");

        /// <summary>Insulin glargine 100 UNT/ML injection - Long-acting insulin</summary>
        public static readonly FhirCode InsulinGlargine = new(Systems.RxNorm, "261551", "Insulin glargine 100 UNT/ML Injectable Solution");

        /// <summary>Insulin lispro 100 UNT/ML injection - Rapid-acting insulin</summary>
        public static readonly FhirCode InsulinLispro = new(Systems.RxNorm, "865097", "Insulin lispro 100 UNT/ML Injectable Solution");

        // Hypertension & Cardiovascular

        /// <summary>Lisinopril 10mg tablet - ACE inhibitor for hypertension</summary>
        public static readonly FhirCode Lisinopril10mg = new(Systems.RxNorm, "314076", "Lisinopril 10 MG Oral Tablet");

        /// <summary>Lisinopril 20mg tablet - ACE inhibitor for hypertension</summary>
        public static readonly FhirCode Lisinopril20mg = new(Systems.RxNorm, "314077", "Lisinopril 20 MG Oral Tablet");

        /// <summary>Amlodipine 5mg tablet - Calcium channel blocker for hypertension</summary>
        public static readonly FhirCode Amlodipine5mg = new(Systems.RxNorm, "329528", "Amlodipine 5 MG Oral Tablet");

        /// <summary>Amlodipine 10mg tablet - Calcium channel blocker for hypertension</summary>
        public static readonly FhirCode Amlodipine10mg = new(Systems.RxNorm, "329526", "Amlodipine 10 MG Oral Tablet");

        /// <summary>Atorvastatin 20mg tablet - Statin for cholesterol</summary>
        public static readonly FhirCode Atorvastatin20mg = new(Systems.RxNorm, "617318", "Atorvastatin 20 MG Oral Tablet");

        /// <summary>Atorvastatin 40mg tablet - Statin for cholesterol</summary>
        public static readonly FhirCode Atorvastatin40mg = new(Systems.RxNorm, "617310", "Atorvastatin 40 MG Oral Tablet");

        /// <summary>Aspirin 81mg tablet - Low-dose antiplatelet for cardiac protection</summary>
        public static readonly FhirCode Aspirin81mg = new(Systems.RxNorm, "243670", "Aspirin 81 MG Oral Tablet");

        /// <summary>Clopidogrel 75mg tablet - Antiplatelet medication</summary>
        public static readonly FhirCode Clopidogrel75mg = new(Systems.RxNorm, "309362", "Clopidogrel 75 MG Oral Tablet");

        /// <summary>Warfarin 5mg tablet - Anticoagulant</summary>
        public static readonly FhirCode Warfarin5mg = new(Systems.RxNorm, "855333", "Warfarin Sodium 5 MG Oral Tablet");

        // Respiratory

        /// <summary>Albuterol 0.083 mg/mL inhalation solution - Bronchodilator for asthma</summary>
        public static readonly FhirCode Albuterol = new(Systems.RxNorm, "435", "Albuterol 0.083 MG/ML Inhalation Solution");

        /// <summary>Fluticasone propionate 50 mcg inhaler - Inhaled corticosteroid for asthma</summary>
        public static readonly FhirCode Fluticasone50mcg = new(Systems.RxNorm, "746030", "Fluticasone propionate 0.05 MG/ACTUAT Metered Dose Inhaler");

        /// <summary>Montelukast 10mg tablet - Leukotriene inhibitor for asthma</summary>
        public static readonly FhirCode Montelukast10mg = new(Systems.RxNorm, "198032", "Montelukast 10 MG Oral Tablet");

        // Pain Management

        /// <summary>Ibuprofen 400mg tablet - NSAID for pain and inflammation</summary>
        public static readonly FhirCode Ibuprofen400mg = new(Systems.RxNorm, "197805", "Ibuprofen 400 MG Oral Tablet");

        /// <summary>Acetaminophen 500mg tablet - Analgesic and antipyretic</summary>
        public static readonly FhirCode Acetaminophen500mg = new(Systems.RxNorm, "313782", "Acetaminophen 500 MG Oral Tablet");

        /// <summary>Naproxen 500mg tablet - NSAID for pain and inflammation</summary>
        public static readonly FhirCode Naproxen500mg = new(Systems.RxNorm, "198014", "Naproxen 500 MG Oral Tablet");

        /// <summary>Tramadol 50mg tablet - Opioid analgesic for moderate pain</summary>
        public static readonly FhirCode Tramadol50mg = new(Systems.RxNorm, "835603", "Tramadol hydrochloride 50 MG Oral Tablet");

        /// <summary>Oxycodone 5mg tablet - Opioid analgesic for severe pain</summary>
        public static readonly FhirCode Oxycodone5mg = new(Systems.RxNorm, "1049621", "Oxycodone hydrochloride 5 MG Oral Tablet");

        // Antibiotics

        /// <summary>Amoxicillin 500mg capsule - Penicillin antibiotic</summary>
        public static readonly FhirCode Amoxicillin500mg = new(Systems.RxNorm, "308192", "Amoxicillin 500 MG Oral Capsule");

        /// <summary>Azithromycin 250mg tablet - Macrolide antibiotic</summary>
        public static readonly FhirCode Azithromycin250mg = new(Systems.RxNorm, "308460", "Azithromycin 250 MG Oral Tablet");

        /// <summary>Ciprofloxacin 500mg tablet - Fluoroquinolone antibiotic</summary>
        public static readonly FhirCode Ciprofloxacin500mg = new(Systems.RxNorm, "197517", "Ciprofloxacin 500 MG Oral Tablet");

        /// <summary>Doxycycline 100mg capsule - Tetracycline antibiotic</summary>
        public static readonly FhirCode Doxycycline100mg = new(Systems.RxNorm, "197516", "Doxycycline 100 MG Oral Capsule");

        /// <summary>Cephalexin 500mg capsule - Cephalosporin antibiotic</summary>
        public static readonly FhirCode Cephalexin500mg = new(Systems.RxNorm, "308191", "Cephalexin 500 MG Oral Capsule");

        /// <summary>Nitrofurantoin 100mg capsule - Antibiotic for urinary tract infections</summary>
        public static readonly FhirCode Nitrofurantoin100mg = new(Systems.RxNorm, "312017", "Nitrofurantoin 100 MG Oral Capsule");

        // Mental Health

        /// <summary>Sertraline 50mg tablet - SSRI antidepressant</summary>
        public static readonly FhirCode Sertraline50mg = new(Systems.RxNorm, "312938", "Sertraline 50 MG Oral Tablet");

        /// <summary>Escitalopram 10mg tablet - SSRI antidepressant</summary>
        public static readonly FhirCode Escitalopram10mg = new(Systems.RxNorm, "351249", "Escitalopram 10 MG Oral Tablet");

        /// <summary>Alprazolam 0.5mg tablet - Benzodiazepine for anxiety</summary>
        public static readonly FhirCode Alprazolam05mg = new(Systems.RxNorm, "308047", "Alprazolam 0.5 MG Oral Tablet");

        /// <summary>Lorazepam 1mg tablet - Benzodiazepine for anxiety</summary>
        public static readonly FhirCode Lorazepam1mg = new(Systems.RxNorm, "197589", "Lorazepam 1 MG Oral Tablet");

        // Gastrointestinal

        /// <summary>Omeprazole 20mg delayed release capsule - Proton pump inhibitor</summary>
        public static readonly FhirCode Omeprazole20mg = new(Systems.RxNorm, "312681", "Omeprazole 20 MG Delayed Release Oral Capsule");

        /// <summary>Pantoprazole 40mg delayed release tablet - Proton pump inhibitor</summary>
        public static readonly FhirCode Pantoprazole40mg = new(Systems.RxNorm, "261242", "Pantoprazole 40 MG Delayed Release Oral Tablet");

        /// <summary>Ondansetron 4mg tablet - Antiemetic for nausea</summary>
        public static readonly FhirCode Ondansetron4mg = new(Systems.RxNorm, "312086", "Ondansetron 4 MG Oral Tablet");

        // Vitamins & Supplements

        /// <summary>Prenatal vitamins - Multivitamin for pregnancy</summary>
        public static readonly FhirCode PrenatalVitamins = new(Systems.RxNorm, "315246", "Prenatal Vitamins");

        /// <summary>Folic acid 0.4mg tablet - Vitamin B9 supplement</summary>
        public static readonly FhirCode FolicAcid = new(Systems.RxNorm, "42963", "Folic Acid 0.4 MG Oral Tablet");

        /// <summary>Vitamin D3 1000 IU tablet - Cholecalciferol supplement</summary>
        public static readonly FhirCode VitaminD31000IU = new(Systems.RxNorm, "317127", "Cholecalciferol 1000 UNT Oral Tablet");

        /// <summary>Vitamin D 50,000 IU capsule - High-dose cholecalciferol for deficiency</summary>
        public static readonly FhirCode VitaminD50000IU = new(Systems.RxNorm, "316879", "Cholecalciferol 50000 UNT Oral Capsule");

        /// <summary>Cetirizine 10mg tablet - Antihistamine for allergic rhinitis</summary>
        public static readonly FhirCode Cetirizine = new(Systems.RxNorm, "1014678", "Cetirizine hydrochloride 10 MG Oral Tablet");

        /// <summary>Fluticasone propionate 110 mcg inhaler - Inhaled corticosteroid for asthma control</summary>
        public static readonly FhirCode FlucticasonePropionate = new(Systems.RxNorm, "745678", "Fluticasone propionate 0.11 MG/ACTUAT Metered Dose Inhaler");

        // Other Common Medications

        /// <summary>Levothyroxine 50mcg tablet - Thyroid hormone replacement</summary>
        public static readonly FhirCode Levothyroxine50mcg = new(Systems.RxNorm, "966222", "Levothyroxine Sodium 0.05 MG Oral Tablet");

        /// <summary>Prednisone 10mg tablet - Corticosteroid for inflammation</summary>
        public static readonly FhirCode Prednisone10mg = new(Systems.RxNorm, "312617", "Prednisone 10 MG Oral Tablet");

        /// <summary>Gabapentin 300mg capsule - Anticonvulsant for nerve pain</summary>
        public static readonly FhirCode Gabapentin300mg = new(Systems.RxNorm, "197653", "Gabapentin 300 MG Oral Capsule");
    }

    /// <summary>
    /// Common encounter type codes.
    /// </summary>
    public static class EncounterTypes
    {
        public static readonly FhirCode Ambulatory = new(Systems.EncounterType, "AMB", "ambulatory");
        public static readonly FhirCode Emergency = new(Systems.EncounterType, "EMER", "emergency");
        public static readonly FhirCode Inpatient = new(Systems.EncounterType, "IMP", "inpatient encounter");
        public static readonly FhirCode Wellness = new(Systems.EncounterType, "WELLNESS", "wellness visit");
    }
}
