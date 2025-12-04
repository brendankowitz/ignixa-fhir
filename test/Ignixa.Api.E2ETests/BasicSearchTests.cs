// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// E2E tests for basic FHIR search functionality.
/// Tests use ScenarioBuilder pattern with tag-based isolation.
/// </summary>
public class BasicSearchTests : CapabilityDrivenTestBase
{
    public BasicSearchTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task GivenPatients_WhenSearchedByCity_ThenReturnsMatching()
    {
        // Capability check - skip if not supported
        RequireSearchParameter("Patient", "address-city");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient().FromSeattle().WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Boston).WithTag(tag).Build();
        var patient3 = CreatePatient().FromSeattle().WithTag(tag).Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act
        var results = await Harness.SearchAsync("Patient", $"address-city=Seattle&_tag={tag}");

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r =>
        {
            r.ResourceType.Should().Be("Patient");
        });
    }

    [Fact(Skip = "Todo")]
    public async Task GivenPatients_WhenSearchedByFamily_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();
        var patient2 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Jones")
            .WithTag(tag)
            .Build();
        var patient3 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act
        var results = await Harness.SearchAsync("Patient", $"family=Smith&_tag={tag}");

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact(Skip = "Todo")]
    public async Task GivenPatients_WhenSearchedByFamilyAndGiven_ThenReturnsMatching()
    {
        // Capability check - multiple parameters
        RequireSearchParameters("Patient", "family", "given");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient()
            .FromSeattle()
            .WithGivenName("John")
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();
        var patient2 = CreatePatient()
            .FromSeattle()
            .WithGivenName("Jane")
            .WithFamilyName("Smith")
            .WithTag(tag)
            .Build();
        var patient3 = CreatePatient()
            .FromSeattle()
            .WithGivenName("John")
            .WithFamilyName("Jones")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - AND logic: family=Smith AND given=John
        var results = await Harness.SearchAsync("Patient", $"family=Smith&given=John&_tag={tag}");

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact(Skip = "TODO")]
    public async Task GivenPatients_WhenSearchedByGender_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient().FromCity(KnownCities.Seattle).WithGender(g => g.Male).WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Boston).WithGender(g => g.Female).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.Chicago).WithGender(g => g.Male).WithTag(tag).Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act
        var results = await Harness.SearchAsync("Patient", $"gender=male&_tag={tag}");

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GivenObservations_WhenSearchedByCode_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Observation Code Search Test")
            .WithDescription("Tests Observation search by code parameter with realistic patient")
            .WithTag(tag)
            .WithPatient(p => p
                .FromSeattle()
                .WithRealisticBMI())
            .AddEncounter("Lab visit")
            // Body Weight observation #1
            .AddObservation(new ObservationState
            {
                Name = "Observation_BodyWeight_1",
                Code = VitalSigns.BodyWeight,
                Value = 75m,
                Unit = "kg",
                UnitCode = "kg"
            })
            // Heart Rate observation
            .AddObservation(new ObservationState
            {
                Name = "Observation_HeartRate",
                Code = VitalSigns.HeartRate,
                Value = 72m,
                Unit = "beats/minute",
                UnitCode = "/min"
            })
            // Body Weight observation #2
            .AddObservation(new ObservationState
            {
                Name = "Observation_BodyWeight_2",
                Code = VitalSigns.BodyWeight,
                Value = 80m,
                Unit = "kg",
                UnitCode = "kg"
            })
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act - Search for Body Weight observations (LOINC code 29463-7)
        var results = await Harness.SearchAsync("Observation", $"code=29463-7&_tag={tag}");

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GivenRealisticPatients_WhenSearchedByState_ThenFiltersByState()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-state");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient().FromCity(KnownCities.Seattle).WithAge(45).WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Boston).WithAge(32).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.LosAngeles).WithAge(28).WithTag(tag).Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - Search for patients in Washington state (only Seattle patient)
        var results = await Harness.SearchAsync("Patient", $"address-state=Washington&_tag={tag}");

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact(Skip = "Todo")]
    public async Task GivenInternationalPatients_WhenSearchedByCountry_ThenFiltersCorrectly()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-country");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient().FromCity(KnownCities.Melbourne).WithAge(35).WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Amsterdam).WithAge(42).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.Seattle).WithAge(28).WithTag(tag).Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - Search for patients in Australia (only Melbourne patient)
        var results = await Harness.SearchAsync("Patient", $"address-country=Australia&_tag={tag}");

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task GivenPatientsWithBMI_WhenSearched_ThenIncludesBMIExtension()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create two separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient()
            .FromCity(KnownCities.Seattle)
            .WithAge(45)
            .WithGender(g => g.Male)
            .WithRealisticBMI()
            .WithTag(tag)
            .Build();
        var patient2 = CreatePatient()
            .FromCity(KnownCities.Boston)
            .WithAge(32)
            .WithGender(g => g.Female)
            .WithRealisticBMI()
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2]);

        // Act - Search for all patients with the tag
        var results = await Harness.SearchAsync("Patient", $"_tag={tag}");

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(patient =>
        {
            patient.MutableNode["extension"].Should().NotBeNull("Patient should have BMI extension");
        });
    }

    [Fact]
    public async Task GivenRealisticPatients_WhenSearchedByCity_ThenShowsEthnicNames()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-city");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create three separate patients (scenario builder only supports one patient)
        var patient1 = CreatePatient().FromCity(KnownCities.Chicago).WithAge(35).WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Houston).WithAge(42).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.SanDiego).WithAge(28).WithTag(tag).Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - Search for all patients with the tag
        var results = await Harness.SearchAsync("Patient", $"_tag={tag}");

        // Assert - All patients should have names and demographic extensions
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(patient =>
        {
            patient.MutableNode["name"].Should().NotBeNull("Patient should have name");
            patient.MutableNode["extension"].Should().NotBeNull("Patient should have race/ethnicity extensions");
        });
    }
}
