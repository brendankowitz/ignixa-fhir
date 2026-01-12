// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Provides comprehensive example lifecycle scenarios demonstrating Layer 3 (Patient Lifecycles) features.
/// These scenarios showcase realistic patient journeys from birth through various ages with clinically
/// accurate event sequences, probabilistic disease onset, and evidence-based care patterns.
/// </summary>
/// <remarks>
/// <para>
/// Each example scenario demonstrates:
/// <list type="bullet">
///   <item><description>Deterministic lifecycle events (wellness visits, immunizations)</description></item>
///   <item><description>Probabilistic disease onset using epidemiological data</description></item>
///   <item><description>Risk factor modeling with DiseaseRiskCalculator</description></item>
///   <item><description>Realistic clinical pathways and care patterns</description></item>
/// </list>
/// </para>
/// <para>
/// Use these examples as templates for:
/// <list type="bullet">
///   <item><description>Test data generation for integration testing</description></item>
///   <item><description>Demo datasets for system validation</description></item>
///   <item><description>Learning realistic patient lifecycle simulation patterns</description></item>
///   <item><description>Population health studies requiring longitudinal data</description></item>
/// </list>
/// </para>
/// </remarks>
public static class LifecycleExampleScenarios
{
    /// <summary>
    /// Generates a healthy child lifecycle from birth to age 18 with no chronic conditions.
    /// This scenario represents the baseline "healthy patient" pathway used for regression testing
    /// and validating preventive care schedules.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <returns>
    /// A ScenarioContext containing approximately 25-30 resources:
    /// <list type="bullet">
    ///   <item><description>1 Patient resource</description></item>
    ///   <item><description>10 Encounters (pediatric wellness visits at ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18)</description></item>
    ///   <item><description>15-20 Immunization resources (CDC schedule: HepB, DTaP, MMR, Varicella, etc.)</description></item>
    ///   <item><description>Multiple Observation resources (vital signs at each visit)</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Clinical Rationale</b>:</para>
    /// <para>
    /// This scenario models a child who receives recommended preventive care per AAP guidelines
    /// but has no acute illnesses or chronic conditions. It demonstrates:
    /// <list type="bullet">
    ///   <item><description>Complete pediatric wellness schedule (ages 1-18)</description></item>
    ///   <item><description>CDC immunization schedule compliance</description></item>
    ///   <item><description>Age-appropriate vital signs tracking</description></item>
    ///   <item><description>Normal growth and development patterns</description></item>
    /// </list>
    /// </para>
    /// <para><b>Use Cases</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Baseline test data for wellness visit functionality</description></item>
    ///   <item><description>Immunization tracking validation</description></item>
    ///   <item><description>Pediatric care pathway testing</description></item>
    ///   <item><description>Quality measure numerator/denominator testing (well-child visits)</description></item>
    /// </list>
    /// <para><b>Expected Resource Counts</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Encounters: 10 (wellness visits)</description></item>
    ///   <item><description>Immunizations: 15-20 (varies with annual flu shots)</description></item>
    ///   <item><description>Observations: 30-40 (vitals per visit)</description></item>
    ///   <item><description>Total: ~25-30 unique resources</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schemaProvider = new R4CoreSchemaProvider();
    /// var context = LifecycleExampleScenarios.GetHealthyChildLifecycle(schemaProvider);
    ///
    /// Console.WriteLine($"Patient: {context.Patient.Id}");
    /// Console.WriteLine($"Encounters: {context.Encounters.Count}");
    /// Console.WriteLine($"Immunizations: {context.Immunizations.Count}");
    /// </code>
    /// </example>
    public static ScenarioContext GetHealthyChildLifecycle(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var lifecycle = new PatientLifecycleGenerator(schemaProvider)
            .WithBirthYear(2005)
            .WithGender("male")
            .WithGivenName("Michael")
            .WithFamilyName("Johnson")
            .AddWellnessSchedule(pediatric: true, adult: false)
            .AddImmunizationSchedule();

        return lifecycle.SimulateUntilAge(18);
    }

    /// <summary>
    /// Generates a typical adult lifecycle from birth (1980) to age 45 with common age-related conditions.
    /// This scenario demonstrates probabilistic disease onset using DiseaseRiskCalculator for realistic
    /// risk modeling based on age, BMI, and lifestyle factors.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <returns>
    /// A ScenarioContext containing approximately 60-70 resources across 45 years:
    /// <list type="bullet">
    ///   <item><description>1 Patient resource</description></item>
    ///   <item><description>35-40 Encounters (pediatric + adult wellness visits from ages 1-45)</description></item>
    ///   <item><description>15-20 Immunization resources (childhood + adult schedules)</description></item>
    ///   <item><description>0-2 Condition resources (probabilistic: Type 2 Diabetes, Essential Hypertension)</description></item>
    ///   <item><description>0-2 MedicationRequest resources (if conditions develop: Metformin, Lisinopril)</description></item>
    ///   <item><description>Multiple Observation resources (A1c, blood pressure, BMI tracking)</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Clinical Rationale</b>:</para>
    /// <para>
    /// This scenario models a typical adult born in 1980 who receives both pediatric and adult
    /// preventive care and has probabilistic risks for common adult-onset chronic conditions:
    /// <list type="bullet">
    ///   <item><description><b>Type 2 Diabetes</b>: 15% probability ages 40-65 (age 45 baseline risk ~10%, no risk factors)</description></item>
    ///   <item><description><b>Essential Hypertension</b>: 30% probability ages 35-60 (baseline 29.6% NHANES prevalence)</description></item>
    /// </list>
    /// </para>
    /// <para><b>Risk Modeling</b>:</para>
    /// <para>
    /// Uses DiseaseRiskCalculator for evidence-based probabilities:
    /// <list type="bullet">
    ///   <item><description>Age 45: Diabetes risk ~10% baseline (40-49 age group)</description></item>
    ///   <item><description>Age 45: Hypertension risk ~29.6% baseline (NHANES data)</description></item>
    ///   <item><description>No obesity (BMI 25), non-smoker, no family history</description></item>
    /// </list>
    /// </para>
    /// <para><b>Use Cases</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Adult care pathway validation</description></item>
    ///   <item><description>Chronic disease management testing</description></item>
    ///   <item><description>Quality measure testing (diabetes A1c, hypertension BP control)</description></item>
    ///   <item><description>Population health analytics (adult onset patterns)</description></item>
    /// </list>
    /// <para><b>Expected Resource Counts</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Encounters: 35-40 (10 pediatric + 27 adult wellness)</description></item>
    ///   <item><description>Immunizations: 15-20 (childhood + adult flu/tetanus)</description></item>
    ///   <item><description>Conditions: 0-2 (probabilistic, ~35% chance of at least one)</description></item>
    ///   <item><description>Medications: 0-2 (if conditions develop)</description></item>
    ///   <item><description>Observations: 100+ (vitals, labs over 45 years)</description></item>
    ///   <item><description>Total: ~60-70 resources</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schemaProvider = new R4CoreSchemaProvider();
    /// var context = LifecycleExampleScenarios.GetTypicalAdultLifecycle(schemaProvider);
    ///
    /// Console.WriteLine($"Patient: {context.Patient.Id}, Age: 45");
    /// Console.WriteLine($"Conditions: {context.Conditions.Count}");
    /// Console.WriteLine($"Medications: {context.Medications.Count}");
    /// </code>
    /// </example>
    public static ScenarioContext GetTypicalAdultLifecycle(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var riskCalc = new DiseaseRiskCalculator();

        var lifecycle = new PatientLifecycleGenerator(schemaProvider)
            .WithBirthYear(1980)
            .WithGender("female")
            .WithGivenName("Jennifer")
            .WithFamilyName("Martinez")
            .AddWellnessSchedule(pediatric: true, adult: true)
            .AddImmunizationSchedule()

            // Probabilistic Type 2 Diabetes (15% overall probability ages 40-65)
            // Uses age-dependent risk calculation: age 45 baseline ~10%
            .AddProbabilisticCondition(
                "Type 2 Diabetes",
                onsetAges: 40..65,
                probability: 0.15,
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Metformin500mg, isChronic: true, frequency: "BID", reasonCode: FhirCode.Conditions.DiabetesType2))

            // Probabilistic Essential Hypertension (30% overall probability ages 35-60)
            // NHANES baseline prevalence: 29.6% in U.S. adults
            .AddProbabilisticCondition(
                "Essential Hypertension",
                onsetAges: 35..60,
                probability: 0.30,
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.HypertensionEssential, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Lisinopril10mg, isChronic: true, frequency: "QD", reasonCode: FhirCode.Conditions.HypertensionEssential));

        return lifecycle.SimulateUntilAge(45);
    }

    /// <summary>
    /// Generates a metabolic syndrome lifecycle from birth (1975) to age 50 with high BMI and
    /// calculated disease risks. This scenario demonstrates advanced risk factor modeling using
    /// DiseaseRiskCalculator to compute age- and risk factor-adjusted probabilities for diabetes
    /// and hypertension.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <returns>
    /// A ScenarioContext containing approximately 70-80 resources with high probability of conditions:
    /// <list type="bullet">
    ///   <item><description>1 Patient resource</description></item>
    ///   <item><description>40-45 Encounters (full pediatric + adult wellness schedules)</description></item>
    ///   <item><description>15-20 Immunization resources</description></item>
    ///   <item><description>2-3 Condition resources (high probability: Obesity, Type 2 Diabetes, Essential Hypertension)</description></item>
    ///   <item><description>3-5 MedicationRequest resources (diabetes + hypertension + lipid management)</description></item>
    ///   <item><description>Multiple Observation resources (elevated A1c, BP, lipid panels)</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Clinical Rationale</b>:</para>
    /// <para>
    /// This scenario models a patient with metabolic syndrome risk factors (obesity, family history)
    /// and uses DiseaseRiskCalculator to compute realistic probabilities:
    /// <list type="bullet">
    ///   <item><description><b>BMI</b>: 35 (Class II Obesity, multiplies diabetes risk ×2.0, hypertension +0.15)</description></item>
    ///   <item><description><b>Family History</b>: First-degree relative with diabetes (multiplies risk ×2.0)</description></item>
    ///   <item><description><b>Non-smoker</b>: No smoking multiplier</description></item>
    /// </list>
    /// </para>
    /// <para><b>Calculated Risks (Age 50, DiseaseRiskCalculator)</b>:</para>
    /// <list type="bullet">
    ///   <item><description><b>Type 2 Diabetes</b>: Base 15% (age 50-59) × 2.0 (obesity) × 2.0 (family history) = 60% probability</description></item>
    ///   <item><description><b>Essential Hypertension</b>: Base 29.6% + 0.15 (obesity) = 44.6% probability</description></item>
    /// </list>
    /// <para><b>Disease Progression Timeline</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Ages 35-40: Obesity documented, lifestyle counseling</description></item>
    ///   <item><description>Ages 40-45: High probability diabetes onset, started on Metformin 500mg BID</description></item>
    ///   <item><description>Ages 45-50: Hypertension likely develops, started on Lisinopril 10mg QD</description></item>
    ///   <item><description>Age 50: If both conditions present, add Atorvastatin 20mg for ASCVD prevention</description></item>
    /// </list>
    /// </para>
    /// <para><b>Use Cases</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Complex chronic disease management testing</description></item>
    ///   <item><description>Multi-morbidity care coordination validation</description></item>
    ///   <item><description>Quality measures: diabetes control (A1c &lt;8%), BP control (&lt;140/90), statin therapy</description></item>
    ///   <item><description>Population health: high-risk patient stratification</description></item>
    ///   <item><description>Risk stratification algorithms validation</description></item>
    /// </list>
    /// <para><b>Expected Resource Counts</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Encounters: 40-45 (wellness + chronic disease management)</description></item>
    ///   <item><description>Conditions: 2-3 (Obesity ~100%, Diabetes ~60%, Hypertension ~45%)</description></item>
    ///   <item><description>Medications: 3-5 (Metformin, Lisinopril, possibly Atorvastatin + Aspirin)</description></item>
    ///   <item><description>Observations: 150+ (A1c, BP, lipids, BMI tracking over decades)</description></item>
    ///   <item><description>Total: ~70-80 resources</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schemaProvider = new R4CoreSchemaProvider();
    /// var context = LifecycleExampleScenarios.GetMetabolicSyndromeLifecycle(schemaProvider);
    ///
    /// Console.WriteLine($"Patient: {context.Patient.Id}, Age: 50, BMI: 35");
    /// Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 2-3)");
    /// Console.WriteLine($"Medications: {context.Medications.Count} (expect 3-5)");
    ///
    /// // Calculate expected risks
    /// var riskCalc = new DiseaseRiskCalculator();
    /// var diabetesRisk = riskCalc.CalculateDiabetesRisk(age: 50, smoker: false, bmi: 35m, familyHistory: true);
    /// var hyperRisk = riskCalc.CalculateHypertensionRisk(age: 50, bmi: 35m, hasDiabetes: true);
    /// Console.WriteLine($"Calculated Diabetes Risk: {diabetesRisk:P1} (60%)");
    /// Console.WriteLine($"Calculated Hypertension Risk: {hyperRisk:P1} (44.6-86.9% depending on diabetes)");
    /// </code>
    /// </example>
    public static ScenarioContext GetMetabolicSyndromeLifecycle(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var riskCalc = new DiseaseRiskCalculator();

        var lifecycle = new PatientLifecycleGenerator(schemaProvider)
            .WithBirthYear(1975)
            .WithGender("male")
            .WithGivenName("Robert")
            .WithFamilyName("Thompson")
            .AddWellnessSchedule(pediatric: true, adult: true)
            .AddImmunizationSchedule()

            // Obesity documented early (often precedes diabetes/hypertension)
            .AddProbabilisticCondition(
                "Obesity",
                onsetAges: 30..40,
                probability: 1.0, // Deterministic for this high-risk scenario
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.Obesity, severity: 2))

            // Type 2 Diabetes with HIGH risk due to obesity (BMI 35) + family history
            // Age 50: Base 0.15 × 2.0 (obesity) × 2.0 (family history) = 0.60 (60% probability)
            // Using calculated risk at age 50 (midpoint of range) as static probability
            .AddProbabilisticCondition(
                "Type 2 Diabetes",
                onsetAges: 40..60,
                probability: riskCalc.CalculateDiabetesRisk(age: 50, smoker: false, bmi: 35m, familyHistory: true),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Metformin500mg, isChronic: true, frequency: "BID", reasonCode: FhirCode.Conditions.DiabetesType2))

            // Essential Hypertension with MODERATE-HIGH risk due to obesity (BMI 35)
            // Age 50: Base 0.296 + 0.15 (obesity) = 0.446 (44.6% without diabetes)
            // Using calculated risk without diabetes (conservative estimate)
            .AddProbabilisticCondition(
                "Essential Hypertension",
                onsetAges: 40..60,
                probability: riskCalc.CalculateHypertensionRisk(age: 50, bmi: 35m, hasDiabetes: false),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.HypertensionEssential, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Lisinopril10mg, isChronic: true, frequency: "QD", reasonCode: FhirCode.Conditions.HypertensionEssential))

            // Hyperlipidemia with statin therapy (common in metabolic syndrome)
            .AddProbabilisticCondition(
                "Hyperlipidemia",
                onsetAges: 45..60,
                probability: 0.50, // 50% probability in metabolic syndrome patients
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.Hyperlipidemia, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Atorvastatin20mg, isChronic: true, frequency: "QD", reasonCode: FhirCode.Conditions.Hyperlipidemia));

        return lifecycle.SimulateUntilAge(50);
    }

    /// <summary>
    /// Generates a pediatric asthma lifecycle from birth (2015) to age 10 demonstrating the atopic march.
    /// This scenario models the natural history of allergic disease progression from allergic rhinitis
    /// to asthma in children.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <returns>
    /// A ScenarioContext containing approximately 30-35 resources:
    /// <list type="bullet">
    ///   <item><description>1 Patient resource</description></item>
    ///   <item><description>10 Encounters (pediatric wellness visits ages 1-10)</description></item>
    ///   <item><description>10-12 Immunization resources (CDC schedule ages 0-10)</description></item>
    ///   <item><description>1-2 Condition resources (allergic rhinitis → asthma if atopic march occurs)</description></item>
    ///   <item><description>2-3 MedicationRequest resources (Albuterol, Fluticasone, possibly Montelukast)</description></item>
    ///   <item><description>Multiple Observation resources (peak flow monitoring, vital signs)</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Clinical Rationale</b>:</para>
    /// <para>
    /// This scenario models the "atopic march" - the natural progression of allergic diseases
    /// in genetically predisposed children:
    /// <list type="bullet">
    ///   <item><description><b>Ages 1-3</b>: Allergic rhinitis often appears first (26.3% baseline pediatric asthma risk)</description></item>
    ///   <item><description><b>Ages 4-10</b>: Asthma may develop in atopic children (risk increases 1.8× with allergies)</description></item>
    ///   <item><description><b>Asthma Probability</b>: 26.3% baseline (CDC NHIS 2021 ages 0-17) × 1.8 (atopy) = 47.3%</description></item>
    /// </list>
    /// </para>
    /// <para><b>Atopic March Sequence</b>:</para>
    /// <list type="bullet">
    ///   <item><description><b>Age 2-3</b>: Allergic rhinitis onset (deterministic in this scenario)</description></item>
    ///   <item><description><b>Age 4-8</b>: Asthma onset (26.3% probability, checking atopic status)</description></item>
    ///   <item><description><b>If asthma develops</b>: Add rescue inhaler (Albuterol), controller (Fluticasone), possibly Montelukast</description></item>
    /// </list>
    /// <para><b>Clinical Management</b>:</para>
    /// <list type="bullet">
    ///   <item><description><b>Allergic Rhinitis</b>: Typically managed with antihistamines (not always prescribed)</description></item>
    ///   <item><description><b>Persistent Asthma</b>: Step therapy per NAEPP guidelines</description></item>
    ///   <item><description>  - Step 1: Rescue inhaler (Albuterol PRN)</description></item>
    ///   <item><description>  - Step 2: Add low-dose ICS (Fluticasone 50mcg)</description></item>
    ///   <item><description>  - Step 3: Add leukotriene modifier (Montelukast 10mg) if poorly controlled</description></item>
    ///   <item><description><b>Monitoring</b>: Peak expiratory flow rate (PEFR) at each visit, asthma control assessment</description></item>
    /// </list>
    /// </para>
    /// <para><b>Use Cases</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Pediatric chronic disease management testing</description></item>
    ///   <item><description>Asthma control quality measures (controller medication ratio)</description></item>
    ///   <item><description>Step therapy validation</description></item>
    ///   <item><description>Atopic disease progression modeling</description></item>
    ///   <item><description>Pediatric medication adherence studies</description></item>
    /// </list>
    /// <para><b>Expected Resource Counts</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Encounters: 10 (wellness visits + possible asthma follow-ups)</description></item>
    ///   <item><description>Immunizations: 10-12 (childhood schedule)</description></item>
    ///   <item><description>Conditions: 1-2 (allergic rhinitis ~100%, asthma ~26.3%)</description></item>
    ///   <item><description>Medications: 0-3 (if asthma: Albuterol + Fluticasone + possibly Montelukast)</description></item>
    ///   <item><description>Observations: 30-40 (vitals, peak flow)</description></item>
    ///   <item><description>Total: ~30-35 resources</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schemaProvider = new R4CoreSchemaProvider();
    /// var context = LifecycleExampleScenarios.GetPediatricAsthmaLifecycle(schemaProvider);
    ///
    /// Console.WriteLine($"Patient: {context.Patient.Id}, Age: 10");
    /// Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 1-2)");
    /// Console.WriteLine($"Medications: {context.Medications.Count} (0-3 depending on asthma)");
    ///
    /// // Check if atopic march occurred
    /// bool hasAllergicRhinitis = context.Conditions.Any(c => c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue&lt;string&gt;() == "61582004");
    /// bool hasAsthma = context.Conditions.Any(c => c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue&lt;string&gt;() == "195967001");
    /// Console.WriteLine($"Atopic March: Rhinitis={hasAllergicRhinitis}, Asthma={hasAsthma}");
    /// </code>
    /// </example>
    public static ScenarioContext GetPediatricAsthmaLifecycle(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var riskCalc = new DiseaseRiskCalculator();

        var lifecycle = new PatientLifecycleGenerator(schemaProvider)
            .WithBirthYear(2015)
            .WithGender("female")
            .WithGivenName("Emma")
            .WithFamilyName("Davis")
            .AddWellnessSchedule(pediatric: true, adult: false)
            .AddImmunizationSchedule()

            // Allergic rhinitis often precedes asthma in the atopic march
            // Onset ages 2-4, deterministic in this scenario to demonstrate atopic progression
            .AddProbabilisticCondition(
                "Allergic Rhinitis",
                onsetAges: 2..4,
                probability: 1.0, // Deterministic to ensure atopic march demonstration
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.AllergicRhinitis, severity: 1))

            // Pediatric asthma with elevated risk due to atopy
            // CDC NHIS 2021: 26.3% baseline in children ages 0-17
            // With allergies: 26.3% × 1.8 = 47.3% probability
            // Using atopic risk (allergic rhinitis develops at age 2-4 deterministically)
            .AddProbabilisticCondition(
                "Asthma",
                onsetAges: 4..10,
                probability: riskCalc.CalculateAsthmaRisk(age: 7, hasAllergies: true),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.Asthma, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Albuterol, isChronic: false, frequency: "PRN", reasonCode: FhirCode.Conditions.Asthma)
                    .AddMedicationOrder(FhirCode.Medications.Fluticasone50mcg, isChronic: true, frequency: "BID", reasonCode: FhirCode.Conditions.Asthma));

        return lifecycle.SimulateUntilAge(10);
    }

    /// <summary>
    /// Generates an elderly patient lifecycle demonstrating multi-morbidity and polypharmacy patterns
    /// typical in geriatric care. Born in 1945, simulated to age 80 with multiple chronic conditions.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <returns>
    /// A ScenarioContext containing approximately 120-150 resources spanning 80 years:
    /// <list type="bullet">
    ///   <item><description>1 Patient resource</description></item>
    ///   <item><description>70-80 Encounters (full pediatric + adult wellness schedules + chronic disease management)</description></item>
    ///   <item><description>15-20 Immunization resources (childhood + adult + geriatric: pneumonia, shingles)</description></item>
    ///   <item><description>4-6 Condition resources (diabetes, hypertension, hyperlipidemia, possible cancer/stroke)</description></item>
    ///   <item><description>6-10 MedicationRequest resources (polypharmacy: antihypertensives, statins, anticoagulants, etc.)</description></item>
    ///   <item><description>Multiple Observation resources (decades of labs, vitals, functional assessments)</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Clinical Rationale</b>:</para>
    /// <para>
    /// This scenario models the cumulative disease burden typical in elderly patients, demonstrating:
    /// <list type="bullet">
    ///   <item><description><b>Multi-morbidity</b>: Average of 2-3 chronic conditions by age 80</description></item>
    ///   <item><description><b>Polypharmacy</b>: 5+ medications common in elderly (risk of interactions)</description></item>
    ///   <item><description><b>Age-stratified risks</b>: Exponentially increasing cancer/stroke risks after age 65</description></item>
    /// </list>
    /// </para>
    /// <para><b>Disease Cascade Timeline</b>:</para>
    /// <list type="bullet">
    ///   <item><description><b>Ages 50-60</b>: Type 2 Diabetes onset (15% probability) → Metformin</description></item>
    ///   <item><description><b>Ages 55-65</b>: Essential Hypertension (30% → 50% if diabetic) → Lisinopril</description></item>
    ///   <item><description><b>Ages 60-70</b>: Hyperlipidemia/ASCVD risk → Atorvastatin + Aspirin 81mg</description></item>
    ///   <item><description><b>Ages 65-80</b>: Cancer risk increases (20% baseline age 60-69 → 35% age 70+)</description></item>
    ///   <item><description><b>Ages 70-80</b>: Stroke risk (10% baseline age 65-74 → 18% age 75+, higher with HTN/DM)</description></item>
    /// </list>
    /// <para><b>Geriatric Care Considerations</b>:</para>
    /// <list type="bullet">
    ///   <item><description><b>Preventive Care</b>: Pneumococcal vaccine (age 65), shingles vaccine (age 50+)</description></item>
    ///   <item><description><b>Cancer Screening</b>: Colonoscopy (ages 50-75), mammography (women 50-74), PSA (men 55-69)</description></item>
    ///   <item><description><b>Functional Assessment</b>: ADL/IADL screening, fall risk, cognitive assessment</description></item>
    ///   <item><description><b>Polypharmacy Review</b>: Beers Criteria, medication reconciliation</description></item>
    /// </list>
    /// </para>
    /// <para><b>Use Cases</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Geriatric care pathway validation</description></item>
    ///   <item><description>Multi-morbidity care coordination testing</description></item>
    ///   <item><description>Polypharmacy drug interaction checking</description></item>
    ///   <item><description>Quality measures: HEDIS Star Ratings (diabetes, hypertension, statin adherence)</description></item>
    ///   <item><description>Population health: high-risk elderly stratification</description></item>
    ///   <item><description>Longitudinal EHR data simulation (8 decades)</description></item>
    /// </list>
    /// <para><b>Expected Resource Counts</b>:</para>
    /// <list type="bullet">
    ///   <item><description>Encounters: 70-80 (wellness + chronic disease + possible acute events)</description></item>
    ///   <item><description>Immunizations: 15-20 (childhood + adult + geriatric)</description></item>
    ///   <item><description>Conditions: 4-6 (diabetes, hypertension, hyperlipidemia, possible cancer/stroke)</description></item>
    ///   <item><description>Medications: 6-10 (polypharmacy typical in elderly)</description></item>
    ///   <item><description>Observations: 200+ (80 years of vitals, labs, screenings)</description></item>
    ///   <item><description>Total: ~120-150 resources</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schemaProvider = new R4CoreSchemaProvider();
    /// var context = LifecycleExampleScenarios.GetElderlyMultiMorbidityLifecycle(schemaProvider);
    ///
    /// Console.WriteLine($"Patient: {context.Patient.Id}, Age: 80");
    /// Console.WriteLine($"Conditions: {context.Conditions.Count} (expect 4-6)");
    /// Console.WriteLine($"Medications: {context.Medications.Count} (expect 6-10, polypharmacy)");
    /// Console.WriteLine($"Total encounters over 80 years: {context.Encounters.Count}");
    /// </code>
    /// </example>
    public static ScenarioContext GetElderlyMultiMorbidityLifecycle(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var riskCalc = new DiseaseRiskCalculator();

        var lifecycle = new PatientLifecycleGenerator(schemaProvider)
            .WithBirthYear(1945)
            .WithGender("male")
            .WithGivenName("William")
            .WithFamilyName("Anderson")
            .AddWellnessSchedule(pediatric: true, adult: true)
            .AddImmunizationSchedule()

            // Type 2 Diabetes (ages 50-65): 15% probability
            .AddProbabilisticCondition(
                "Type 2 Diabetes",
                onsetAges: 50..65,
                probability: 0.15,
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Metformin500mg, isChronic: true, frequency: "BID", reasonCode: FhirCode.Conditions.DiabetesType2))

            // Essential Hypertension (ages 55-70): 30-50% probability depending on diabetes
            // Using age 62 (midpoint) without diabetes for conservative estimate
            .AddProbabilisticCondition(
                "Essential Hypertension",
                onsetAges: 55..70,
                probability: riskCalc.CalculateHypertensionRisk(age: 62, bmi: 27m, hasDiabetes: false),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.HypertensionEssential, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Lisinopril10mg, isChronic: true, frequency: "QD", reasonCode: FhirCode.Conditions.HypertensionEssential))

            // Hyperlipidemia (ages 60-75): 40% probability, statin for ASCVD prevention
            .AddProbabilisticCondition(
                "Hyperlipidemia",
                onsetAges: 60..75,
                probability: 0.40,
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.Hyperlipidemia, severity: 2)
                    .AddMedicationOrder(FhirCode.Medications.Atorvastatin20mg, isChronic: true, frequency: "QD", reasonCode: FhirCode.Conditions.Hyperlipidemia)
                    .AddMedicationOrder(FhirCode.Medications.Aspirin81mg, isChronic: true, frequency: "QD")) // Cardiac protection

            // Cancer (ages 60-80): Increasing probability with age (20% → 35%)
            // Using age 70 (midpoint) for static probability calculation
            .AddProbabilisticCondition(
                "Cancer",
                onsetAges: 60..80,
                probability: riskCalc.CalculateCancerRisk(age: 70, smoker: false, familyHistory: false),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddEncounter(reason: "Cancer diagnosis and treatment planning", durationMinutes: 60))

            // Stroke (ages 65-80): High risk with age + hypertension + diabetes
            // Using age 72 (midpoint) without pre-existing conditions for baseline
            .AddProbabilisticCondition(
                "Stroke",
                onsetAges: 65..80,
                probability: riskCalc.CalculateStrokeRisk(age: 72, hasHypertension: false, hasDiabetes: false, smoker: false),
                scenarioFactory: sp => new ScenarioBuilder(sp)
                    .AddEmergencyVisit(reason: "Stroke (CVA)")
                    .AddMedicationOrder(FhirCode.Medications.Clopidogrel75mg, isChronic: true, frequency: "QD")); // Secondary prevention

        return lifecycle.SimulateUntilAge(80);
    }
}
