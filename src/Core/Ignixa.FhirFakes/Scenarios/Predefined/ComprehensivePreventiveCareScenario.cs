// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Predefined;

/// <summary>
/// Provides extension methods for generating comprehensive preventive care scenarios combining
/// wellness visits with age-appropriate immunizations, cancer screenings, and preventive counseling.
/// </summary>
/// <remarks>
/// Demonstrates:
/// - **Age-based care profiles**: Pediatric (0-17), Adult (18-64), Senior (65+)
/// - **Preventive services**: Immunizations, cancer screenings, geriatric assessments
/// - **Gender-specific screenings**: Mammogram, Pap smear (women); PSA (men)
/// - **Evidence-based guidelines**: USPSTF, CDC, CMS recommendations
///
/// Clinical Guidelines:
/// - USPSTF (U.S. Preventive Services Task Force) cancer screening recommendations
/// - CDC immunization schedules (pediatric and adult)
/// - CMS Medicare Annual Wellness Visit requirements
/// - AAP (American Academy of Pediatrics) well-child visit guidelines
/// </remarks>
public static class ComprehensivePreventiveCareScenario
{
    /// <summary>
    /// Generates a comprehensive pediatric well-child visit scenario with age-appropriate immunizations.
    ///
    /// Timeline:
    /// 1. Well-child visit encounter
    /// 2. Growth measurements (height, weight, head circumference if &lt;24 months)
    /// 3. Developmental screening observations
    /// 4. Age-appropriate immunizations (CDC schedule)
    /// 5. Anticipatory guidance (counseling procedure)
    ///
    /// Generated Resources (typical 12-month visit):
    /// - 1 Encounter (well-child visit)
    /// - 1 Practitioner (Pediatrician)
    /// - 1 Organization (Pediatric Clinic)
    /// - 4 Vital Sign Observations (height, weight, head circumference, BMI)
    /// - 1 Developmental screening Observation
    /// - 4-5 Immunizations (MMR, Varicella, Hep A, PCV13 based on age)
    /// - 1 Procedure (Anticipatory guidance counseling)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="ageInMonths">Patient age in months (0-216 for ages 0-18 years).</param>
    /// <param name="gender">Patient gender (default: "male").</param>
    /// <returns>A complete scenario context with pediatric preventive care resources.</returns>
    /// <remarks>
    /// Immunization schedule (CDC):
    /// - Birth: Hep B #1
    /// - 2 months: DTaP #1, Hib #1, PCV13 #1, IPV #1, Rotavirus #1, Hep B #2
    /// - 4 months: DTaP #2, Hib #2, PCV13 #2, IPV #2, Rotavirus #2
    /// - 6 months: DTaP #3, Hib #3, PCV13 #3, IPV #3, Rotavirus #3 (if 3-dose), Hep B #3
    /// - 12 months: MMR #1, Varicella #1, Hep A #1, PCV13 #4
    /// - 15 months: DTaP #4, Hib #4
    /// - 18 months: Hep A #2
    /// - 4-6 years: DTaP #5, IPV #4, MMR #2, Varicella #2
    /// - 11-12 years: Tdap, HPV #1, Meningococcal
    /// - 16 years: Meningococcal booster
    /// </remarks>
    public static ScenarioContext GetPediatricWellChildVisit(
        this IFhirSchemaProvider schemaProvider,
        int ageInMonths = 12,
        string gender = "male")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (ageInMonths < 0 || ageInMonths > 216)
        {
            throw new ArgumentOutOfRangeException(nameof(ageInMonths), "Age must be between 0 and 216 months (0-18 years).");
        }

        var ageInYears = ageInMonths / 12;
        var builder = new ScenarioBuilder(schemaProvider)
            .WithName($"{ageInMonths}-Month Well-Child Visit")
            .WithDescription($"Well-child visit for {ageInMonths}-month-old child with growth assessment, developmental screening, and age-appropriate immunizations per CDC schedule.")

            // Initial patient
            .WithPatient(age: ageInYears, gender: gender)

            // Pediatric clinic and pediatrician
            .AddClinicFamilyPractice("Happy Kids Pediatric Clinic")
            .AddPediatrician()

            // Well-child visit encounter
            .AddWellnessVisit($"{ageInMonths}-month well-child visit");

        // Growth measurements
        builder = PediatricWellChildVisit(ageInMonths, builder);

        return builder.Build();
    }

    /// <summary>
    /// Generates a comprehensive adult annual physical exam scenario with age-appropriate cancer screenings
    /// and immunizations.
    ///
    /// Timeline:
    /// 1. Annual physical examination encounter
    /// 2. Vital signs (BP, HR, temp, height, weight, BMI)
    /// 3. Laboratory tests (lipid panel, glucose, HbA1c)
    /// 4. Age-appropriate cancer screenings (mammogram, Pap smear, colorectal, PSA)
    /// 5. Adult immunizations (Tdap, influenza, others as indicated)
    /// 6. Preventive counseling (smoking cessation, diet, exercise)
    ///
    /// Generated Resources (typical 45-year-old female):
    /// - 1 Encounter (annual physical)
    /// - 1 Practitioner (Family Physician)
    /// - 1 Organization (Primary Care Clinic)
    /// - 6 Vital Sign Observations
    /// - 2 DiagnosticReports (lipid panel, metabolic panel)
    /// - 1 Observation (HbA1c)
    /// - 1-2 DiagnosticReports (mammogram for women 40+, colorectal screening 45+)
    /// - 2-3 Immunizations (Tdap every 10 years, annual influenza)
    /// - 2-3 Procedures (smoking cessation, diet, exercise counseling)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (18-64 years).</param>
    /// <param name="gender">Patient gender ("male" or "female").</param>
    /// <returns>A complete scenario context with adult preventive care resources.</returns>
    /// <remarks>
    /// Cancer Screening Guidelines (USPSTF):
    /// - **Women**:
    ///   - Mammogram: Ages 40-74, biennial (Grade B)
    ///   - Pap smear: Ages 21-65, every 3 years (Grade A)
    ///   - Colorectal: Ages 45-75 (Grade A)
    /// - **Men**:
    ///   - Colorectal: Ages 45-75 (Grade A)
    ///   - PSA: Ages 50-69, shared decision-making (Grade C)
    ///   - AAA ultrasound: Ages 65-75 if ever smoked (Grade B)
    ///
    /// Adult Immunizations (CDC):
    /// - Tdap: Once, then Td booster every 10 years
    /// - Influenza: Annual
    /// - COVID-19: Per current guidelines
    /// - Shingles: Age 50+ (Shingrix, 2 doses)
    /// - Pneumococcal: Age 65+ or high-risk
    /// </remarks>
    public static ScenarioContext GetAdultAnnualPhysical(
        this IFhirSchemaProvider schemaProvider,
        int age = 45,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (age < 18 || age > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be between 18 and 64 years for adult preventive care.");
        }

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Adult Annual Physical Examination")
            .WithDescription($"Comprehensive annual physical for {age}-year-old {gender} with age-appropriate cancer screenings, immunizations, and preventive counseling.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Primary care clinic and family physician
            .AddClinicFamilyPractice("Riverside Family Medicine")
            .AddFamilyPractitioner()

            // Annual physical encounter
            .AddWellnessVisit("Annual preventive health examination")

            // REUSABLE FRAGMENT: Standard vital signs
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Standard Vital Signs")

            // REUSABLE FRAGMENT: Comprehensive metabolic panel
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Order Comprehensive Metabolic Panel")

            // REUSABLE FRAGMENT: Lipid panel (cardiovascular risk assessment)
            .AddSubScenario(CommonScenarios.LipidPanel(), "Order Lipid Panel")

            // Hemoglobin A1c for diabetes screening
            .AddObservation(ObservationState.HemoglobinA1c());

        // Age-appropriate cancer screenings and immunizations
        builder = AdultAnnualPhysical(age, gender, builder);

        return builder.Build();
    }

    /// <summary>
    /// Generates a comprehensive Medicare Annual Wellness Visit scenario for seniors with
    /// geriatric assessments and age-appropriate preventive services.
    ///
    /// Timeline:
    /// 1. Medicare Annual Wellness Visit encounter
    /// 2. Vital signs (BP, HR, weight, BMI)
    /// 3. Laboratory tests (lipid panel, glucose, vitamin D, B12)
    /// 4. Cancer screenings (colorectal if &lt;76, mammogram if female &lt;75)
    /// 5. Geriatric assessments (fall risk, cognitive screening, depression screening)
    /// 6. Senior immunizations (pneumococcal, shingles, annual influenza)
    /// 7. Medication review and advance care planning
    ///
    /// Generated Resources (typical 70-year-old):
    /// - 1 Encounter (Medicare Annual Wellness Visit)
    /// - 1 Practitioner (Family Physician or Internist)
    /// - 1 Organization (Primary Care Clinic)
    /// - 5 Vital Sign Observations
    /// - 2 DiagnosticReports (metabolic panel with vitamin D/B12, lipid panel)
    /// - 4 Geriatric Assessment Observations (fall risk, cognitive, depression, functional status)
    /// - 1-2 DiagnosticReports (colorectal screening if indicated, mammogram for women &lt;75)
    /// - 3 Immunizations (pneumococcal, shingles, influenza)
    /// - 2-3 Procedures (medication review, advance care planning, health risk assessment)
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="age">Patient age (65+ years).</param>
    /// <param name="gender">Patient gender ("male" or "female").</param>
    /// <returns>A complete scenario context with senior preventive care resources.</returns>
    /// <remarks>
    /// Medicare Annual Wellness Visit (AWV) Requirements:
    /// - Health Risk Assessment questionnaire
    /// - Review of medical and family history
    /// - Measurement of height, weight, BP, BMI
    /// - Detection of cognitive impairment
    /// - Depression screening (PHQ-2)
    /// - Functional ability and fall risk assessment
    /// - Review of current medications and providers
    /// - Advance care planning discussion
    /// - Personalized prevention plan
    ///
    /// Geriatric Assessment Tools:
    /// - **Fall risk**: Morse Fall Scale, Timed Up and Go test
    /// - **Cognitive**: Mini-Cog (3-item recall + clock drawing), SLUMS, MoCA
    /// - **Depression**: PHQ-2, PHQ-9 if PHQ-2 positive
    /// - **Functional status**: ADLs (Activities of Daily Living), IADLs
    ///
    /// Senior Immunizations (CDC):
    /// - Pneumococcal: PCV13 (once) + PPSV23 (1-2 doses)
    /// - Shingles: Shingrix 2-dose series (age 50+)
    /// - Influenza: Annual high-dose or adjuvanted vaccine
    /// - Tdap/Td: If not up to date
    /// - COVID-19: Per current guidelines
    /// </remarks>
    public static ScenarioContext GetSeniorMedicareWellnessVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 70,
        string gender = "female")
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (age < 65)
        {
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be 65 or older for Medicare Annual Wellness Visit.");
        }

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Medicare Annual Wellness Visit")
            .WithDescription($"Medicare Annual Wellness Visit for {age}-year-old {gender} with geriatric assessments, preventive screenings, medication review, and advance care planning.")

            // Initial patient
            .WithPatient(age: age, gender: gender)

            // Primary care clinic and internist/family physician
            .AddClinicFamilyPractice("Silver Years Senior Care")
            .AddInternist()

            // Medicare Annual Wellness Visit encounter
            .AddEncounter("Medicare Annual Wellness Visit (G0438/G0439)", durationMinutes: 60)

            // REUSABLE FRAGMENT: Standard vital signs (BP, weight, height, BMI)
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Vital Signs")

            // REUSABLE FRAGMENT: Comprehensive metabolic panel
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Order Comprehensive Metabolic Panel")

            // REUSABLE FRAGMENT: Lipid panel
            .AddSubScenario(CommonScenarios.LipidPanel(), "Order Lipid Panel");

        // Geriatric assessments and senior-specific preventive care
        builder = SeniorMedicareWellnessVisit(age, gender, builder);

        return builder.Build();
    }

    #region Private Helper Methods

    /// <summary>
    /// Adds pediatric growth measurements, developmental screening, immunizations, and anticipatory guidance.
    /// </summary>
    private static ScenarioBuilder PediatricWellChildVisit(int ageInMonths, ScenarioBuilder builder)
    {
        // Growth measurements - all ages
        builder = builder
            .AddObservation(VitalSigns.BodyHeight, minValue: 50m, maxValue: 120m, unit: "cm", unitCode: "cm")
            .AddObservation(VitalSigns.BodyWeight, minValue: 3m, maxValue: 40m, unit: "kg", unitCode: "kg");

        // Head circumference for infants/toddlers (< 24 months)
        if (ageInMonths < 24)
        {
            builder = builder.AddObservation(VitalSigns.HeadCircumference, minValue: 30m, maxValue: 50m, unit: "cm", unitCode: "cm");
        }

        // BMI for children 2+ years
        if (ageInMonths >= 24)
        {
            builder = builder.AddObservation(VitalSigns.BMI, minValue: 13m, maxValue: 20m, unit: "kg/m2", unitCode: "kg/m2");
        }

        // Developmental screening observation
        builder = builder.AddObservation(
            new FhirCode(FhirCode.Systems.Loinc, "71493-2", "Developmental screening assessment"),
            value: 1.0m,
            unit: "{score}",
            unitCode: "{score}");

        // Age-appropriate immunizations (CDC schedule)
        builder = AddPediatricImmunizations(ageInMonths, builder);

        // Anticipatory guidance counseling
        builder = builder.AddProcedure(new ProcedureState
        {
            Code = new FhirCode(FhirCode.Systems.SnomedCt, "409073007", "Education"),
            Category = "procedure",
            Duration = TimeSpan.FromMinutes(10),
            Outcome = "Anticipatory guidance provided for age-appropriate development, nutrition, safety, and parenting.",
            Note = GetAnticipatoryGuidanceNote(ageInMonths)
        });

        return builder;
    }

    /// <summary>
    /// Adds adult cancer screenings, immunizations, and preventive counseling based on age and gender.
    /// </summary>
    private static ScenarioBuilder AdultAnnualPhysical(int age, string gender, ScenarioBuilder builder)
    {
        // Gender-specific cancer screenings for women
        if (gender.Equals("female", StringComparison.OrdinalIgnoreCase))
        {
            // Mammogram (ages 40-74)
            if (age >= 40 && age <= 74)
            {
                builder = builder.AddDiagnosticReport(new DiagnosticReportState
                {
                    Code = new FhirCode(FhirCode.Systems.Loinc, "24606-6", "Mammogram"),
                    Category = "imaging",
                    Conclusion = "BIRADS 1 - Negative. No evidence of malignancy. Annual screening recommended.",
                    Status = "final"
                });
            }

            // Pap smear (ages 21-65)
            if (age >= 21 && age <= 65)
            {
                builder = builder.AddDiagnosticReport(new DiagnosticReportState
                {
                    Code = new FhirCode(FhirCode.Systems.Loinc, "10524-7", "Cervical cytology"),
                    Category = "laboratory",
                    Conclusion = "Negative for intraepithelial lesion or malignancy (NILM). Repeat in 3 years.",
                    Status = "final"
                });
            }
        }

        // Gender-specific cancer screenings for men
        if (gender.Equals("male", StringComparison.OrdinalIgnoreCase))
        {
            // PSA (ages 50-69, shared decision-making)
            if (age >= 50 && age <= 69)
            {
                builder = builder.AddObservation(
                    LabObservations.PSA,
                    minValue: 0.5m,
                    maxValue: 3.5m,
                    unit: "ng/mL",
                    unitCode: "ng/mL");
            }
        }

        // Colorectal cancer screening (ages 45-75)
        if (age >= 45 && age <= 75)
        {
            // Colonoscopy (every 10 years) or FIT test (annual)
            builder = builder.AddColonoscopy("Normal colonoscopy. No polyps identified. Repeat in 10 years.");
        }

        // Adult immunizations
        builder = builder.AddInfluenzaVaccine();

        // Tdap booster (every 10 years)
        if (age % 10 == 5) // Simplified logic: assume booster needed at ages 25, 35, 45, 55
        {
            builder = builder.AddImmunization(ImmunizationState.TdapBooster());
        }

        // COVID-19 vaccine (if recent)
        if (age >= 18)
        {
            builder = builder.AddCovid19Vaccine(doseNumber: 1);
        }

        // Preventive counseling procedures
        builder = builder
            .AddProcedure(new ProcedureState
            {
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "225323000", "Smoking cessation education"),
                Category = "procedure",
                Duration = TimeSpan.FromMinutes(5),
                Outcome = "Patient counseled on smoking cessation strategies and resources.",
                Note = "Discussed health risks of tobacco use, benefits of quitting, and available cessation programs."
            })
            .AddProcedure(new ProcedureState
            {
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "226072003", "Dietary advice"),
                Category = "procedure",
                Duration = TimeSpan.FromMinutes(5),
                Outcome = "Patient counseled on healthy eating patterns and portion control.",
                Note = "Discussed Mediterranean diet, limiting processed foods, and increasing vegetables/fruits."
            })
            .AddProcedure(new ProcedureState
            {
                Code = new FhirCode(FhirCode.Systems.SnomedCt, "281090004", "Exercise counseling"),
                Category = "procedure",
                Duration = TimeSpan.FromMinutes(5),
                Outcome = "Patient counseled on importance of regular physical activity.",
                Note = "Recommended 150 minutes of moderate aerobic activity per week, plus strength training twice weekly."
            });

        return builder;
    }

    /// <summary>
    /// Adds geriatric assessments, senior immunizations, medication review, and advance care planning.
    /// </summary>
    private static ScenarioBuilder SeniorMedicareWellnessVisit(int age, string gender, ScenarioBuilder builder)
    {
        // Additional lab tests for seniors
        builder = builder
            .AddObservation(
                new FhirCode(FhirCode.Systems.Loinc, "1989-3", "Vitamin D [Mass/volume] in Serum or Plasma"),
                minValue: 20m,
                maxValue: 50m,
                unit: "ng/mL",
                unitCode: "ng/mL")
            .AddObservation(
                new FhirCode(FhirCode.Systems.Loinc, "2132-9", "Vitamin B12 [Mass/volume] in Serum or Plasma"),
                minValue: 200m,
                maxValue: 900m,
                unit: "pg/mL",
                unitCode: "pg/mL");

        // Geriatric Assessment: Fall Risk Screening
        builder = builder.AddObservation(
            new FhirCode(FhirCode.Systems.Loinc, "73830-2", "Fall risk total [Score] Morse Fall Scale"),
            value: 25m,
            unit: "{score}",
            unitCode: "{score}");

        // Geriatric Assessment: Cognitive Screening (Mini-Cog)
        builder = builder.AddObservation(
            new FhirCode(FhirCode.Systems.Loinc, "72107-6", "Mini-Cog"),
            value: 4m,
            unit: "{score}",
            unitCode: "{score}");

        // Geriatric Assessment: Depression Screening (PHQ-2)
        builder = builder.AddObservation(
            new FhirCode(FhirCode.Systems.Loinc, "55758-7", "Patient Health Questionnaire 2 item (PHQ-2) total score [Reported]"),
            value: 1m,
            unit: "{score}",
            unitCode: "{score}");

        // Geriatric Assessment: Functional Status (ADL)
        builder = builder.AddObservation(
            new FhirCode(FhirCode.Systems.Loinc, "83233-7", "Katz Activities of Daily Living total score"),
            value: 5m,
            unit: "{score}",
            unitCode: "{score}");

        // Cancer screenings (age-appropriate)
        if (gender.Equals("female", StringComparison.OrdinalIgnoreCase) && age <= 74)
        {
            // Mammogram up to age 74
            builder = builder.AddDiagnosticReport(new DiagnosticReportState
            {
                Code = new FhirCode(FhirCode.Systems.Loinc, "24606-6", "Mammogram"),
                Category = "imaging",
                Conclusion = "BIRADS 1 - Negative. No evidence of malignancy. Annual screening recommended.",
                Status = "final"
            });
        }

        // Colorectal screening (up to age 75)
        if (age <= 75)
        {
            builder = builder.AddColonoscopy("Normal colonoscopy. No polyps identified. Repeat in 10 years.");
        }

        // Senior immunizations
        builder = builder
            .AddInfluenzaVaccine()
            .AddImmunization(new ImmunizationState
            {
                Code = Immunizations.PPSV23,
                DoseNumber = 1,
                Series = "Pneumococcal Adult Series",
                SeriesDosesRecommended = 2
            })
            .AddImmunization(new ImmunizationState
            {
                Code = Immunizations.Zoster,
                DoseNumber = 1,
                Series = "Shingles Vaccine Series",
                SeriesDosesRecommended = 2
            });

        // Medication review procedure
        builder = builder.AddProcedure(new ProcedureState
        {
            Code = new FhirCode(FhirCode.Systems.SnomedCt, "182838006", "Medication review"),
            Category = "procedure",
            Duration = TimeSpan.FromMinutes(15),
            Outcome = "Comprehensive medication review completed. No drug-drug interactions identified.",
            Note = "Reviewed all current medications, dosages, and adherence. Discussed potential side effects and interactions. Patient verbalized understanding."
        });

        // Advance care planning discussion
        builder = builder.AddProcedure(new ProcedureState
        {
            Code = new FhirCode(FhirCode.Systems.SnomedCt, "423861004", "Advance care planning discussion"),
            Category = "procedure",
            Duration = TimeSpan.FromMinutes(20),
            Outcome = "Advance care planning discussion completed. Patient preferences documented.",
            Note = "Discussed healthcare proxy, living will, and end-of-life care preferences. Patient expressed wishes for care. Encouraged documentation of advance directives."
        });

        // Health risk assessment
        builder = builder.AddProcedure(new ProcedureState
        {
            Code = new FhirCode(FhirCode.Systems.SnomedCt, "225338004", "Risk assessment"),
            Category = "procedure",
            Duration = TimeSpan.FromMinutes(10),
            Outcome = "Health risk assessment completed. Personalized prevention plan created.",
            Note = "Assessed cardiovascular risk, fall risk, cognitive status, and functional independence. Provided personalized recommendations for health maintenance."
        });

        return builder;
    }

    /// <summary>
    /// Adds age-appropriate immunizations per CDC pediatric schedule.
    /// </summary>
    private static ScenarioBuilder AddPediatricImmunizations(int ageInMonths, ScenarioBuilder builder)
    {
        return ageInMonths switch
        {
            // Birth
            0 => builder.AddImmunization(ImmunizationState.HepB(1)),

            // 2 months
            2 => builder
                .AddImmunization(ImmunizationState.DTaP(1))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.Hib,
                    DoseNumber = 1,
                    Series = "Hib Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(ImmunizationState.PneumococcalPCV13(1))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.IPV,
                    DoseNumber = 1,
                    Series = "Polio Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.RotavirusMonovalent,
                    DoseNumber = 1,
                    Series = "Rotavirus Series",
                    SeriesDosesRecommended = 2,
                    Route = "oral"
                })
                .AddImmunization(ImmunizationState.HepB(2)),

            // 4 months
            4 => builder
                .AddImmunization(ImmunizationState.DTaP(2))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.Hib,
                    DoseNumber = 2,
                    Series = "Hib Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(ImmunizationState.PneumococcalPCV13(2))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.IPV,
                    DoseNumber = 2,
                    Series = "Polio Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.RotavirusMonovalent,
                    DoseNumber = 2,
                    Series = "Rotavirus Series",
                    SeriesDosesRecommended = 2,
                    Route = "oral"
                }),

            // 6 months
            6 => builder
                .AddImmunization(ImmunizationState.DTaP(3))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.Hib,
                    DoseNumber = 3,
                    Series = "Hib Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(ImmunizationState.PneumococcalPCV13(3))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.IPV,
                    DoseNumber = 3,
                    Series = "Polio Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(ImmunizationState.HepB(3)),

            // 12 months (typical well-child visit)
            12 => builder
                .AddImmunization(ImmunizationState.MMRDose1())
                .AddImmunization(ImmunizationState.Varicella(1))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.HepA,
                    DoseNumber = 1,
                    Series = "Hepatitis A Series",
                    SeriesDosesRecommended = 2
                })
                .AddImmunization(ImmunizationState.PneumococcalPCV13(4)),

            // 15 months
            15 => builder
                .AddImmunization(ImmunizationState.DTaP(4))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.Hib,
                    DoseNumber = 4,
                    Series = "Hib Series",
                    SeriesDosesRecommended = 4
                }),

            // 18 months
            18 => builder
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.HepA,
                    DoseNumber = 2,
                    Series = "Hepatitis A Series",
                    SeriesDosesRecommended = 2
                }),

            // 4-6 years (assume 60 months = 5 years)
            >= 48 and <= 72 => builder
                .AddImmunization(ImmunizationState.DTaP(5))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.IPV,
                    DoseNumber = 4,
                    Series = "Polio Series",
                    SeriesDosesRecommended = 4
                })
                .AddImmunization(ImmunizationState.MMRDose2())
                .AddImmunization(ImmunizationState.Varicella(2)),

            // 11-12 years (assume 132 months = 11 years)
            >= 132 and <= 144 => builder
                .AddImmunization(ImmunizationState.TdapBooster())
                .AddImmunization(ImmunizationState.HPV(1))
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.MeningococcalMCV4P,
                    DoseNumber = 1,
                    Series = "Meningococcal Series",
                    SeriesDosesRecommended = 2
                }),

            // 16 years (assume 192 months)
            >= 192 and <= 204 => builder
                .AddImmunization(new ImmunizationState
                {
                    Code = Immunizations.MeningococcalMCV4P,
                    DoseNumber = 2,
                    Series = "Meningococcal Series",
                    SeriesDosesRecommended = 2
                }),

            // Default: Annual influenza for all ages
            _ => builder.AddInfluenzaVaccine()
        };
    }

    /// <summary>
    /// Gets age-appropriate anticipatory guidance note.
    /// </summary>
    private static string GetAnticipatoryGuidanceNote(int ageInMonths) => ageInMonths switch
    {
        < 12 => "Discussed safe sleep practices, feeding schedules, and developmental milestones. Reviewed car seat safety and injury prevention.",
        < 24 => "Discussed toddler nutrition, language development, and toilet training readiness. Reviewed home safety and poison prevention.",
        < 60 => "Discussed preschool readiness, socialization, and screen time limits. Reviewed safety around water, strangers, and crossing streets.",
        < 132 => "Discussed school performance, peer relationships, and physical activity. Reviewed bike safety, sports safety, and bullying prevention.",
        _ => "Discussed adolescent health, risk behaviors, and academic goals. Reviewed substance use prevention, mental health, and safe driving."
    };

    #endregion
}
