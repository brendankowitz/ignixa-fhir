// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
        // Note: Country is stored as ISO 2-letter code (AU) per CityDemographics, not full name
        var results = await Harness.SearchAsync("Patient", $"address-country=AU&_tag={tag}");

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

    #region Ported Tests - Phase 1: Core Search Functionality

    /// <summary>
    /// Tests AND logic with multiple search parameters.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 42-53)
    /// </summary>
    [Fact]
    public async Task GivenResourceWithVariousValues_WhenSearchedWithMultipleParams_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
    {
        // Capability check - multiple parameters
        RequireSearchParameters("Patient", "address-city", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient1 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Robinson")
            .WithTag(tag)
            .Build();

        var patient2 = CreatePatient()
            .FromCity(KnownCities.LosAngeles)  // Portland not in KnownCities, using LA as different city
            .WithFamilyName("Williamas")
            .WithTag(tag)
            .Build();

        var patient3 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Jones")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - AND logic: city=Seattle AND family=Jones
        var results = await Harness.SearchAsync("Patient", $"address-city=Seattle&family=Jones&_tag={tag}");

        // Assert - Only patient3 matches both criteria
        results.Should().HaveCount(1);
        var matchedPatient = results[0];
        matchedPatient.MutableNode["name"]?[0]?["family"]?.GetValue<string>().Should().Be("Jones");
    }

    /// <summary>
    /// Tests basic resource type filtering.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 76-89)
    /// </summary>
    [Fact]
    public async Task GivenVariousTypesOfResources_WhenSearchedByResourceType_ThenOnlyResourcesMatchingTheResourceTypeShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var faker = Harness.CreateFaker().WithTag(tag);

        // Create various resource types
        var patient1 = CreatePatient().FromSeattle().WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Boston).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.Chicago).WithTag(tag).Build();

        // Create an Observation (uses faker which respects the tag)
        var observation = faker.Generate("Observation");
        // Create an Organization
        var organization = faker.Generate("Organization");

        await Harness.CreateResourcesAsync([patient1, patient2, patient3, observation, organization]);

        // Act - Search for Patient resources with our tag
        var results = await Harness.SearchAsync("Patient", $"_tag={tag}");

        // Assert - Should return only Patient resources
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.ResourceType.Should().Be("Patient"));
    }

    /// <summary>
    /// Tests the :missing modifier for finding resources with or without a specific parameter.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 93-123)
    /// </summary>
    [Fact]
    public async Task GivenResourcesWithVariousValues_WhenSearchedWithTheMissingModifier_ThenOnlyTheResourcesWithMissingOrPresentParametersAreReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with gender specified
        var patientWithGender = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();

        // Patient without gender (need to remove gender after building)
        var patientWithoutGender = CreatePatient()
            .FromCity(KnownCities.Boston)
            .WithTag(tag)
            .Build();
        patientWithoutGender.MutableNode.Remove("gender");

        await Harness.CreateResourcesAsync([patientWithGender, patientWithoutGender]);

        // Act & Assert - Search for patients with gender missing
        var missingResults = await Harness.SearchAsync("Patient", $"gender:missing=true&_tag={tag}");
        missingResults.Should().HaveCount(1);

        // Act & Assert - Search for patients with gender present
        var presentResults = await Harness.SearchAsync("Patient", $"gender:missing=false&_tag={tag}");
        presentResults.Should().HaveCount(1);
        presentResults[0].MutableNode["gender"]?.GetValue<string>().Should().Be("female");
    }

    /// <summary>
    /// Tests reference parameter searches combined with _id.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 127-142)
    /// </summary>
    [Fact]
    public async Task GivenResourcesWithReference_WhenSearchedWithReferenceAndIdParameter_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "organization");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create two patients with different organization references
        var patient1 = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();
        patient1.MutableNode["managingOrganization"] = new JsonObject
        {
            ["reference"] = "Organization/123"
        };

        var patient2 = CreatePatient()
            .FromCity(KnownCities.Boston)
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();
        patient2.MutableNode["managingOrganization"] = new JsonObject
        {
            ["reference"] = "Organization/234"
        };

        var createdResources = await Harness.CreateResourcesAsync([patient1, patient2]);
        var patient1Id = createdResources[0].Id;

        // Act - Search with matching reference
        var matchingResults = await Harness.SearchAsync("Patient", $"_id={patient1Id}&organization=Organization/123");
        matchingResults.Should().HaveCount(1);

        // Act - Search with non-matching reference
        var nonMatchingResults = await Harness.SearchAsync("Patient", $"_id={patient1Id}&organization=Organization/234");
        nonMatchingResults.Should().BeEmpty();
    }

    /// <summary>
    /// Tests the _type parameter for cross-resource-type searches.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 375-402)
    /// </summary>
    [Fact]
    public async Task GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var faker = Harness.CreateFaker().WithTag(tag);

        // Create various resource types
        var patient1 = CreatePatient().FromSeattle().WithTag(tag).Build();
        var patient2 = CreatePatient().FromCity(KnownCities.Boston).WithTag(tag).Build();
        var patient3 = CreatePatient().FromCity(KnownCities.Chicago).WithTag(tag).Build();

        var observation = faker.Generate("Observation");
        var organization = faker.Generate("Organization");

        await Harness.CreateResourcesAsync([patient1, patient2, patient3, observation, organization]);

        // Act - System-level search with _type=Patient
        var bundle = await Harness.SearchSystemAsync($"_type=Patient&_tag={tag}");

        // Assert - Should return only Patient resources
        var resources = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();

        resources.Should().HaveCount(3);
        resources.Should().AllSatisfy(r => r.ResourceType.Should().Be("Patient"));
    }

    #endregion

    #region Ported Tests - Phase 2: Pagination & Counting

    /// <summary>
    /// Tests that _count parameter limits the number of resources returned per page.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 572-624)
    /// </summary>
    [Fact]
    public async Task GivenResources_WhenSearchedWithCount_ThenNumberOfResourcesReturnedShouldNotExceedCount()
    {
        // Arrange
        const int numberOfResources = 5;
        const int count = 2;
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, numberOfResources)
            .Select(_ => CreatePatient().FromSeattle().WithTag(tag).Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Search with _count=2
        var bundle = await Harness.SearchBundleAsync("Patient", $"_count={count}&_tag={tag}");

        // Assert - First page should have at most 'count' entries
        bundle.Entry.Count.Should().BeLessOrEqualTo(count);
        bundle.Entry.Count.Should().BeGreaterThan(0);

        // Follow next links to collect all results
        var allResources = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToList();

        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        var iterations = 1;

        while (!string.IsNullOrEmpty(nextLink) && iterations < 10)
        {
            var nextBundle = await Harness.GetBundleAsync(nextLink);
            nextBundle.Entry.Count.Should().BeLessOrEqualTo(count);

            allResources.AddRange(
                nextBundle.Entry
                    .Where(e => e.Resource is not null)
                    .Select(e => e.Resource!));

            nextLink = nextBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
            iterations++;
        }

        // Assert - All resources should be retrieved across pages
        allResources.Should().HaveCount(numberOfResources);
    }

    /// <summary>
    /// Tests that next link is populated when more results exist than the count.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 628-666)
    /// </summary>
    [Fact]
    public async Task GivenMoreSearchResultsThanCount_WhenSearched_ThenNextLinkUrlShouldBePopulated()
    {
        // Arrange
        const int numberOfResources = 10;
        const int count = 2;
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, numberOfResources)
            .Select(_ => CreatePatient().FromSeattle().WithTag(tag).Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Search with _count=2
        var bundle = await Harness.SearchBundleAsync("Patient", $"_count={count}&_tag={tag}");

        // Assert - Should have next link since we have more results
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next");
        nextLink.Should().NotBeNull("There should be a next link when results exceed count");
        nextLink!.Url.Should().NotBeNullOrEmpty();

        // Follow all pages and collect resources
        var allResources = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToList();

        var currentNextLink = nextLink.Url;
        var iterations = 1;

        while (!string.IsNullOrEmpty(currentNextLink) && iterations < 10)
        {
            var nextBundle = await Harness.GetBundleAsync(currentNextLink);
            allResources.AddRange(
                nextBundle.Entry
                    .Where(e => e.Resource is not null)
                    .Select(e => e.Resource!));

            currentNextLink = nextBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
            iterations++;
        }

        // All resources should be retrieved
        allResources.Should().HaveCount(numberOfResources);
    }

    /// <summary>
    /// Tests _summary=count returns total without resources.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 719-740)
    /// </summary>
    [Fact]
    public async Task GivenListOfResources_WhenSearchedWithCountSummary_ThenTotalCountShouldBeReturned()
    {
        // Arrange
        const int numberOfResources = 5;
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, numberOfResources)
            .Select(_ => CreatePatient().FromSeattle().WithTag(tag).Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Search with _summary=count
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=count");

        // Assert
        bundle.Should().NotBeNull();
        bundle.Total.Should().Be(numberOfResources);
        bundle.Entry.Should().BeEmpty("_summary=count should not return any entries");
    }

    #endregion

    #region Ported Tests - Phase 3: Advanced Features

    /// <summary>
    /// Tests _total=accurate returns accurate total count.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 872-893)
    /// </summary>
    [Fact]
    public async Task GivenListOfResources_WhenSearchedWithTotalTypeAccurate_ThenTotalCountShouldBeIncludedInReturnedBundle()
    {
        // Arrange
        const int numberOfResources = 5;
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, numberOfResources)
            .Select(_ => CreatePatient().FromSeattle().WithTag(tag).Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Search with _total=accurate
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_total=accurate");

        // Assert
        bundle.Should().NotBeNull();
        bundle.Entry.Should().NotBeEmpty();
        bundle.Total.Should().Be(numberOfResources);
    }

    /// <summary>
    /// Tests _total=none does not return total count.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 897-918)
    /// </summary>
    [Fact]
    public async Task GivenListOfResources_WhenSearchedWithTotalTypeNone_ThenTotalCountShouldNotBeIncludedInReturnedBundle()
    {
        // Arrange
        const int numberOfResources = 5;
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, numberOfResources)
            .Select(_ => CreatePatient().FromSeattle().WithTag(tag).Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Search with _total=none
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_total=none");

        // Assert
        bundle.Should().NotBeNull();
        bundle.Entry.Should().NotBeEmpty();
        bundle.Total.Should().BeNull("_total=none should not include total count");
    }

    /// <summary>
    /// Tests that only the current version of a resource is returned when searching.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 693-715)
    /// </summary>
    [Fact]
    public async Task GivenResourceWithHistory_WhenSearchedWithParams_ThenOnlyCurrentVersionShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create an observation with a specific code
        var scenario = CreateScenario()
            .WithTag(tag)
            .WithPatient(p => p.FromSeattle())
            .AddEncounter("Test visit")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .Build();

        var createdResources = await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Find the created observation
        var observation = createdResources.FirstOrDefault(r => r.ResourceType == "Observation")
            ?? throw new InvalidOperationException("No observation was created");

        // Act - Update the observation with a new value
        var valueQuantity = observation.MutableNode["valueQuantity"] as JsonObject
            ?? throw new InvalidOperationException("Observation does not have valueQuantity");

        valueQuantity["value"] = JsonValue.Create(85m); // Update heart rate from 72 to 85

        var updatedObservation = await Harness.UpdateResourceAsync(observation);

        // Search for observations by code
        var searchResults = await Harness.SearchAsync("Observation", $"code=8867-4&_tag={tag}");

        // Assert
        searchResults.Should().HaveCount(1, "only the current version should be returned, not historical versions");

        var returnedObservation = searchResults.First();
        var returnedValue = returnedObservation.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();

        returnedValue.Should().Be(85m, "the returned observation should have the updated value");
        returnedObservation.Id.Should().Be(observation.Id, "the returned observation should have the same ID");
    }

    /// <summary>
    /// Tests case-insensitive city search.
    /// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.BasicSearchTests (line 55-72)
    /// </summary>
    [Fact]
    public async Task GivenResourceWithVariousValues_WhenSearchedWithCityParam_ThenOnlyResourcesMatchingAllSearchParamsShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "address-city");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patients with various city values (testing case-insensitivity)
        var patient1 = CreatePatient().FromSeattle().WithFamilyName("Robinson").WithTag(tag).Build();  // Seattle
        var patient2 = CreatePatient().FromCity(KnownCities.LosAngeles).WithFamilyName("Williamas").WithTag(tag).Build();  // Los Angeles
        var patient3 = CreatePatient().FromSeattle().WithFamilyName("Skouras").WithTag(tag).Build();   // Seattle
        var patient4 = CreatePatient().FromCity(KnownCities.NewYork).WithFamilyName("Cook").WithTag(tag).Build();  // New York

        await Harness.CreateResourcesAsync([patient1, patient2, patient3, patient4]);

        // Act - Search for Seattle (case-insensitive)
        var results = await Harness.SearchAsync("Patient", $"address-city=Seattle&_tag={tag}");

        // Assert - Should match both Seattle patients
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r =>
        {
            var city = r.MutableNode["address"]?[0]?["city"]?.GetValue<string>();
            city.Should().BeEquivalentTo("Seattle", "City search should be case-insensitive");
        });
    }

    #endregion
}
