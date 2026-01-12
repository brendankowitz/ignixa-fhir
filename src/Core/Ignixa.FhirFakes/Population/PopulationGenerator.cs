// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Generates large-scale patient populations with realistic demographic distributions.
/// </summary>
/// <remarks>
/// Orchestrates:
/// - Demographic sampling from real US cities (DemographicsDataProvider)
/// - Culturally appropriate name generation (LocalBasedNameGenerator + Bogus locales)
/// - Full lifecycle simulation from birth to current age (PatientLifecycleGenerator)
/// - Age/race-stratified disease risk modeling (DiseaseRiskCalculator)
///
/// Example:
/// Generate 1,000 patients from Massachusetts with demographics matching Boston census data.
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public class PopulationGenerator(IFhirSchemaProvider schemaProvider)
{
    private readonly IFhirSchemaProvider _schemaProvider = schemaProvider;
    private readonly DemographicsDataProvider _demographics = DemographicsDataProvider.CreateDefault();
    private readonly DiseaseRiskCalculator _riskCalculator = new();

    /// <summary>
    /// Gets all available cities with demographic data.
    /// </summary>
    public IReadOnlyList<CityDemographics> AvailableCities => _demographics.Cities;

    /// <summary>
    /// Gets all available state names for population generation.
    /// </summary>
    /// <example>
    /// Available states: Arizona, California, Illinois, Massachusetts, New York, Pennsylvania, Texas, Washington
    /// </example>
    public IReadOnlyList<string> AvailableStates => _demographics.States;

    /// <summary>
    /// Generates a population of patients for a specific US state as FHIR transaction bundles.
    /// </summary>
    /// <param name="state">US state name (e.g., "Massachusetts", "California", "Texas")</param>
    /// <param name="populationSize">Number of patients to generate (e.g., 100, 1000, 10000)</param>
    /// <returns>Enumerable of BundleJsonNode objects, each representing one patient's complete medical history as a FHIR transaction bundle</returns>
    /// <example>
    /// var generator = new PopulationGenerator(schemaProvider);
    /// foreach (var bundle in generator.Generate("Massachusetts", 1000))
    /// {
    ///     // Each bundle contains a patient with demographics matching Boston census data
    ///     await fhirClient.PostAsync(bundle);
    /// }
    /// </example>
    public IEnumerable<ScenarioContext> Generate(string state, int populationSize)
    {
        for (int i = 0; i < populationSize; i++)
        {
            // 1. Select city (weighted by population)
            var city = _demographics.SelectCity(state);

            // 2. Sample demographics using PatientBuilder (race, age, gender, name, zip, area code)
            var patientBuilder = PatientBuilderFactory.Create(_schemaProvider)
                .FromCity(city)
                .WithName()
                .WithRealisticBMI();

            // 3. Extract sampled demographics for lifecycle simulation
            var age = patientBuilder.Age!.Value;
            var birthYear = DateTime.Now.Year - age;

            // 4. Configure lifecycle generator with sampled demographics
            var lifecycle = new PatientLifecycleGenerator(_schemaProvider)
                .WithBirthYear(birthYear)
                .WithGender(patientBuilder.Gender!)
                .WithGivenName(patientBuilder.GivenName)
                .WithFamilyName(patientBuilder.FamilyName)
                .WithZipCode(patientBuilder.ZipCode)
                .WithAreaCode(patientBuilder.AreaCode)
                .AddWellnessSchedule(pediatric: age < 18, adult: age >= 18)
                .AddImmunizationSchedule();

            // 5. Add age/race-stratified probabilistic conditions
            AddProbabilisticConditions(lifecycle, age, patientBuilder.BMI!.Value);

            // 6. Simulate lifecycle until current age
            ScenarioContext context = lifecycle.SimulateUntilAge(age);

            yield return context;
        }
    }

    private void AddProbabilisticConditions(PatientLifecycleGenerator lifecycle, int currentAge, decimal bmi)
    {
        // Smoking prevalence: 13.7% (CDC 2021)
        var isSmoker = Random.Shared.NextDouble() < 0.137;

        // Family history: 30% (general estimate)
        var hasFamilyHistory = Random.Shared.NextDouble() < 0.30;

        // Type 2 Diabetes (age 40+)
        if (currentAge >= 40)
        {
            var diabetesRisk = _riskCalculator.CalculateDiabetesRisk(
                age: currentAge,
                smoker: isSmoker,
                bmi: bmi,
                familyHistory: hasFamilyHistory
            );

            lifecycle.AddProbabilisticCondition(
                "Type2Diabetes",
                40..90,
                diabetesRisk,
                sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.DiabetesType2)
                    .AddMedicationOrder(FhirCode.Medications.Metformin500mg)
            );
        }

        // Essential Hypertension (age 35+)
        if (currentAge >= 35)
        {
            var hypertensionRisk = _riskCalculator.CalculateHypertensionRisk(
                age: currentAge,
                bmi: bmi,
                hasDiabetes: false // Will be updated dynamically if diabetes develops
            );

            lifecycle.AddProbabilisticCondition(
                "Hypertension",
                35..90,
                hypertensionRisk,
                sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.HypertensionEssential)
                    .AddMedicationOrder(FhirCode.Medications.Lisinopril10mg)
            );
        }

        // Asthma (pediatric: ages 1-17)
        if (currentAge >= 1 && currentAge <= 17)
        {
            var asthmaRisk = _riskCalculator.CalculateAsthmaRisk(
                age: currentAge,
                hasAllergies: Random.Shared.NextDouble() < 0.25 // 25% have allergies
            );

            lifecycle.AddProbabilisticCondition(
                "Asthma",
                1..17,
                asthmaRisk,
                sp => new ScenarioBuilder(sp)
                    .AddConditionOnset(FhirCode.Conditions.Asthma)
                    .AddMedicationOrder(FhirCode.Medications.Albuterol)
            );
        }

        // Cancer (age 50+)
        if (currentAge >= 50)
        {
            var cancerRisk = _riskCalculator.CalculateCancerRisk(
                age: currentAge,
                smoker: isSmoker,
                familyHistory: hasFamilyHistory
            );

            // Don't add the scenario builder here to keep it simple
            // (Cancer management is complex - defer to future implementation)
        }
    }
}
