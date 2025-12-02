// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Lifecycle;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.Models;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Population;

/// <summary>
/// Generates large-scale patient populations with realistic demographic distributions.
/// </summary>
/// <remarks>
/// Orchestrates:
/// - Demographic sampling from real US cities (DemographicsDataProvider)
/// - Culturally appropriate name generation (EthnicNameGenerator + Bogus locales)
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
    private readonly EthnicNameGenerator _nameGenerator = new();
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

            // 2. Sample demographics from city distribution
            var race = _demographics.SampleRace(city);
            var age = _demographics.SampleAge(city);
            var gender = _demographics.SampleGender(city);

            // 3. Generate culturally appropriate name using Bogus locales
            var (firstName, lastName) = _nameGenerator.GenerateName(race, gender);

            // 4. Generate realistic BMI (US adult distribution from NHANES)
            var bmi = GenerateRealisticBMI();

            // 5. Sample city-appropriate zip code and area code
            var zipCode = _demographics.SampleZipCode(city);
            var areaCode = _demographics.SampleAreaCode(city);

            // 6. Generate full lifecycle from birth to current age
            var birthYear = DateTime.Now.Year - age;

            var lifecycle = new PatientLifecycleGenerator(_schemaProvider)
                .WithBirthYear(birthYear)
                .WithGender(gender)
                .WithGivenName(firstName)
                .WithFamilyName(lastName)
                .WithZipCode(zipCode)
                .WithAreaCode(areaCode)
                .AddWellnessSchedule(pediatric: age < 18, adult: age >= 18)
                .AddImmunizationSchedule();

            // 7. Add age/race-stratified probabilistic conditions
            AddProbabilisticConditions(lifecycle, age, bmi);

            // 8. Simulate lifecycle until current age
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

    private static decimal GenerateRealisticBMI()
    {
        // US adult BMI distribution (NHANES 2017-2020):
        // Mean: 29.0, SD: 6.5
        // Distribution: 35% normal (18.5-24.9), 34% overweight (25-29.9), 31% obese (30+)
        var random = Random.Shared.NextDouble();
        return random switch
        {
            < 0.35 => (decimal)Random.Shared.Next(19, 25),  // Normal weight
            < 0.69 => (decimal)Random.Shared.Next(25, 30),  // Overweight
            _ => (decimal)Random.Shared.Next(30, 42)        // Obese
        };
    }
}
