// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// Example E2E tests demonstrating the new PatientBuilder integration with ScenarioBuilder.
/// These tests showcase best practices for creating realistic patient scenarios.
/// </summary>
/// <remarks>
/// Key API methods demonstrated:
/// - WithRealisticPatient: City demographics with ethnically appropriate names
/// - WithSeattlePatient: Seattle is special and deserves its own method
/// - WithPatientFromCity: Selector pattern for discoverability (c => c.BostonMA)
/// - WithSimplePatient: Basic patient for simple tests
/// </remarks>
public class PatientBuilderE2EExamples : CapabilityDrivenTestBase
{
    public PatientBuilderE2EExamples(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region WithRealisticPatient Examples

    /// <summary>
    /// Demonstrates creating a realistic patient from Boston with city demographics.
    /// Uses the selector pattern (c => c.BostonMA) for IntelliSense discoverability.
    /// </summary>
    [Fact]
    public async Task GivenRealisticPatient_WhenSearchingByCity_ThenFindsPatient()
    {
        // Capability check - skip if not supported
        RequireSearchParameter("Patient", "address-city");

        // Arrange - Create a realistic patient from Boston
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Realistic Patient City Search")
            .WithDescription("Demonstrates WithRealisticPatient with city demographics")
            .WithTag(tag)
            .WithRealisticPatient(p => p
                .FromCity(KnownCities.Boston)  // Selector pattern for discoverability
                .WithAge(45)
                .WithGender(g => g.Male))
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act - Search by city
        var results = await Harness.SearchAsync("Patient", $"address-city=Boston&_tag={tag}");

        // Assert
        results.Should().ContainSingle();
        results[0].ResourceType.Should().Be("Patient");

        // Verify patient has Boston address
        var address = results[0].MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Boston");
        address?["state"]?.GetValue<string>().Should().Be("MA");
    }

    /// <summary>
    /// Demonstrates creating a realistic patient with BMI and searching by gender.
    /// Shows how to chain multiple configuration methods.
    /// </summary>
    [Fact]
    public async Task GivenRealisticPatientWithBMI_WhenSearchingByGender_ThenFindsPatient()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange - Create a realistic patient with BMI
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Realistic Patient Gender Search with BMI")
            .WithDescription("Demonstrates WithRealisticPatient with BMI and gender search")
            .WithTag(tag)
            .WithRealisticPatient(p => p
                .FromCity(KnownCities.Chicago)
                .WithAge(35)
                .WithGender(g => g.Female)
                .WithRealisticBMI())  // Adds BMI extension from US adult distribution
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act
        var results = await Harness.SearchAsync("Patient", $"gender=female&_tag={tag}");

        // Assert
        results.Should().ContainSingle();
        results[0].MutableNode["gender"]?.GetValue<string>().Should().Be("female");

        // Verify BMI extension is present
        results[0].MutableNode["extension"].Should().NotBeNull();
    }

    #endregion

    #region WithSeattlePatient Examples

    /// <summary>
    /// Demonstrates the special WithSeattlePatient method.
    /// Seattle is special and deserves its own method!
    /// </summary>
    [Fact]
    public async Task GivenSeattlePatient_WhenSearchingByCity_ThenFindsPatient()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-city");

        // Arrange - Seattle patient with minimal configuration
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Seattle Patient Search")
            .WithDescription("Demonstrates WithSeattlePatient")
            .WithTag(tag)
            .WithSeattlePatient(p => p.WithAge(28))  // Seattle + age override
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act
        var results = await Harness.SearchAsync("Patient", $"address-city=Seattle&_tag={tag}");

        // Assert
        results.Should().ContainSingle();

        // Verify Seattle demographics
        var address = results[0].MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Seattle");
        address?["state"]?.GetValue<string>().Should().Be("WA");

        // Seattle zip codes start with 981
        address?["postalCode"]?.GetValue<string>().Should().StartWith("981");
    }

    #endregion

    #region WithPatientFromCity Examples

    /// <summary>
    /// Demonstrates the WithPatientFromCity method with multiple patients from different cities.
    /// Shows how to use the selector pattern for city selection.
    /// </summary>
    [Fact]
    public async Task GivenPatientsFromDifferentCities_WhenSearchingByState_ThenFiltersByState()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-state");

        // Arrange - Create patients from different cities/states
        var tag = Guid.NewGuid().ToString();

        // Patient 1: New York
        var scenario1 = CreateScenario()
            .WithTag(tag)
            .WithPatientFromCity(KnownCities.NewYork, p => p.WithAge(40))
            .Build();

        // Patient 2: Los Angeles
        var scenario2 = CreateScenario()
            .WithTag(tag)
            .WithPatientFromCity(KnownCities.LosAngeles, p => p.WithAge(32))
            .Build();

        // Patient 3: Houston
        var scenario3 = CreateScenario()
            .WithTag(tag)
            .WithPatientFromCity(KnownCities.Houston, p => p.WithAge(55))
            .Build();

        await Harness.CreateResourcesAsync(
            scenario1.AllResources
                .Concat(scenario2.AllResources)
                .Concat(scenario3.AllResources)
                .ToArray());

        // Act - Search for California patients
        var results = await Harness.SearchAsync("Patient", $"address-state=CA&_tag={tag}");

        // Assert - Only LA patient should match
        results.Should().ContainSingle();
        var address = results[0].MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().Should().Be("Los Angeles");
    }

    #endregion

    #region Integration with Clinical Scenario Examples

    /// <summary>
    /// Demonstrates a complete clinical scenario with realistic patient,
    /// encounter, and observation. Shows how the new PatientBuilder API
    /// integrates seamlessly with existing ScenarioBuilder methods.
    /// </summary>
    [Fact]
    public async Task GivenRealisticPatientWithEncounterAndObservation_WhenSearchingByCode_ThenFindsObservation()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange - Complete clinical scenario
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Complete Clinical Scenario")
            .WithDescription("Demonstrates PatientBuilder integration with clinical states")
            .WithTag(tag)
            // Start with a realistic patient from Philadelphia
            .WithPatientFromCity(
                KnownCities.Philadelphia,
                p => p
                    .WithAge(50)
                    .WithGender(g => g.Male)
                    .WithRealisticBMI())
            // Add clinical encounter
            .AddEncounter("Annual Physical")
            // Add vital signs observations
            .AddObservation(VitalSigns.BloodPressureSystolic, 128m, "mmHg")
            .AddObservation(VitalSigns.BloodPressureDiastolic, 82m, "mmHg")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act - Search for blood pressure observations
        var results = await Harness.SearchAsync(
            "Observation",
            $"code={VitalSigns.BloodPressureSystolic.Code}&_tag={tag}");

        // Assert
        results.Should().ContainSingle();
        results[0].ResourceType.Should().Be("Observation");

        // Verify observation references the patient
        var subjectRef = results[0].MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.Should().Contain(scenario.Patient!.Id);
    }

    #endregion
}
