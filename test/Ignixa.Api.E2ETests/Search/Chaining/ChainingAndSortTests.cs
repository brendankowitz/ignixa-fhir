// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.Chaining;

/// <summary>
/// E2E tests for FHIR chained search functionality combined with sorting and counting.
/// Tests chained search on HealthcareService with _has:PractitionerRole reverse chaining,
/// validating results with different _sort, _summary=count, and _total=accurate combinations.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.ChainingAndSortTests
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ChainingAndSortTests : CapabilityDrivenTestBase
{
    public ChainingAndSortTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests a chained search pattern with various sorting and counting parameter combinations.
    /// Validates that _sort, _summary=count, and _total=accurate work correctly with chained searches.
    /// Ported from: GivenAChainedSearchPattern_WhenSearched_ThenCompareTheResultsWithDifferentVariationsOfSortingExpressions
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchPattern_WhenSearched_ThenCompareTheResultsWithDifferentVariationsOfSortingExpressions()
    {
        // Arrange - Create test data with 15 HealthcareService resources
        var tag = Guid.NewGuid().ToString();
        var testPractitionerId = Guid.NewGuid().ToString();
        await CreateHealthcareServiceTestDataAsync(tag, testPractitionerId);

        const int expectedNumberOfEntriesInFirstPage = 10;
        const int expectedNumberOfLinks = 2;
        const int totalNumberOfFilteredHealthcareServices = 13;

        // Verify all 15 HealthcareServices were ingested
        var allServicesBundle = await Harness.SearchBundleAsync("HealthcareService", $"_tag={tag}&_total=accurate");
        allServicesBundle.Entry.Count.ShouldBe(expectedNumberOfEntriesInFirstPage);
        allServicesBundle.Link.Count.ShouldBe(expectedNumberOfLinks);
        allServicesBundle.Total.ShouldBe(15);

        // Common query: name exists, has active PractitionerRole for specific practitioner, service is active, location exists
        var commonQuery = $"_tag={tag}&name:missing=false&_has:PractitionerRole:service:practitioner={testPractitionerId}&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";

        // Act & Assert - Test 1: Search with no sort
        var bundleWithNoSort = await Harness.SearchBundleAsync("HealthcareService", commonQuery);
        bundleWithNoSort.Entry.Count.ShouldBe(expectedNumberOfEntriesInFirstPage);
        bundleWithNoSort.Link.Count.ShouldBe(expectedNumberOfLinks);

        // Act & Assert - Test 2: Search with _sort=name
        var bundleWithSort = await Harness.SearchBundleAsync("HealthcareService", commonQuery + "&_sort=name");
        bundleWithSort.Entry.Count.ShouldBe(expectedNumberOfEntriesInFirstPage);
        bundleWithSort.Link.Count.ShouldBe(expectedNumberOfLinks);

        // Act & Assert - Test 3: Search with _summary=count (no entries, just count)
        var bundleWithNoSortAndSummaryCount = await Harness.SearchBundleAsync("HealthcareService", commonQuery + "&_summary=count");
        bundleWithNoSortAndSummaryCount.Entry.ShouldBeEmpty();
        bundleWithNoSortAndSummaryCount.Total.ShouldBe(totalNumberOfFilteredHealthcareServices);

        // Act & Assert - Test 4: Search with _sort=name&_summary=count (sort should be ignored)
        var bundleWithSortAndSummaryCount = await Harness.SearchBundleAsync("HealthcareService", commonQuery + "&_sort=name&_summary=count");
        bundleWithSortAndSummaryCount.Entry.ShouldBeEmpty();
        bundleWithSortAndSummaryCount.Total.ShouldBe(totalNumberOfFilteredHealthcareServices);

        // Act & Assert - Test 5: Search with _total=accurate
        var bundleWithNoSortAndTotalAccurate = await Harness.SearchBundleAsync("HealthcareService", commonQuery + "&_total=accurate");
        bundleWithNoSortAndTotalAccurate.Entry.Count.ShouldBe(expectedNumberOfEntriesInFirstPage);
        bundleWithNoSortAndTotalAccurate.Link.Count.ShouldBe(expectedNumberOfLinks);
        bundleWithNoSortAndTotalAccurate.Total.ShouldBe(totalNumberOfFilteredHealthcareServices);

        // Act & Assert - Test 6: Search with _sort=name&_total=accurate
        var bundleWithSortAndTotalAccurate = await Harness.SearchBundleAsync("HealthcareService", commonQuery + "&_sort=name&_total=accurate");
        bundleWithSortAndTotalAccurate.Entry.Count.ShouldBe(expectedNumberOfEntriesInFirstPage);
        bundleWithSortAndTotalAccurate.Link.Count.ShouldBe(expectedNumberOfLinks);
        bundleWithSortAndTotalAccurate.Total.ShouldBe(totalNumberOfFilteredHealthcareServices);
    }

    /// <summary>
    /// Creates test data matching the Microsoft FHIR Server test scenario:
    /// - 15 total HealthcareService resources
    /// - 13 match the filter criteria (have name, active, location, linked to active PractitionerRole)
    /// - 2 don't match (missing name or location, or inactive)
    /// </summary>
    private async Task CreateHealthcareServiceTestDataAsync(string tag, string practitionerId)
    {
        var faker = Harness.CreateFaker().WithTag(tag);
        var resources = new List<ResourceJsonNode>();

        // Create Practitioner for PractitionerRole references
        var practitioner = faker.Generate("Practitioner");
        practitioner.Id = practitionerId;
        resources.Add(practitioner);

        // Create Location resources (shared by multiple services)
        var location1 = faker.Generate("Location");
        var location2 = faker.Generate("Location");
        resources.Add(location1);
        resources.Add(location2);

        // Create 13 HealthcareServices that MATCH the filter criteria
        // (have name, active=true, location exists, linked to active PractitionerRole)
        for (int i = 0; i < 13; i++)
        {
            var service = faker.Generate("HealthcareService");
            service.MutableNode["name"] = $"Healthcare Service {i + 1:D2}";
            service.MutableNode["active"] = true;
            service.MutableNode["location"] = new JsonArray
            {
                new JsonObject { ["reference"] = $"Location/{(i % 2 == 0 ? location1.Id : location2.Id)}" }
            };
            resources.Add(service);

            // Create active PractitionerRole linking service to practitioner
            var role = faker.Generate("PractitionerRole");
            role.MutableNode["active"] = true;
            role.MutableNode["practitioner"] = new JsonObject { ["reference"] = $"Practitioner/{practitionerId}" };
            role.MutableNode["healthcareService"] = new JsonArray
            {
                new JsonObject { ["reference"] = $"HealthcareService/{service.Id}" }
            };
            resources.Add(role);
        }

        // Create 2 HealthcareServices that DON'T match the filter criteria
        // Service 14: Missing name (name:missing=false will exclude it)
        var serviceNoName = faker.Generate("HealthcareService");
        serviceNoName.MutableNode.Remove("name");
        serviceNoName.MutableNode["active"] = true;
        serviceNoName.MutableNode["location"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"Location/{location1.Id}" }
        };
        resources.Add(serviceNoName);

        var roleForNoName = faker.Generate("PractitionerRole");
        roleForNoName.MutableNode["active"] = true;
        roleForNoName.MutableNode["practitioner"] = new JsonObject { ["reference"] = $"Practitioner/{practitionerId}" };
        roleForNoName.MutableNode["healthcareService"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"HealthcareService/{serviceNoName.Id}" }
        };
        resources.Add(roleForNoName);

        // Service 15: Missing location (location:missing=false will exclude it)
        var serviceNoLocation = faker.Generate("HealthcareService");
        serviceNoLocation.MutableNode["name"] = "Healthcare Service No Location";
        serviceNoLocation.MutableNode["active"] = true;
        serviceNoLocation.MutableNode.Remove("location");
        resources.Add(serviceNoLocation);

        var roleForNoLocation = faker.Generate("PractitionerRole");
        roleForNoLocation.MutableNode["active"] = true;
        roleForNoLocation.MutableNode["practitioner"] = new JsonObject { ["reference"] = $"Practitioner/{practitionerId}" };
        roleForNoLocation.MutableNode["healthcareService"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"HealthcareService/{serviceNoLocation.Id}" }
        };
        resources.Add(roleForNoLocation);

        // Create all resources
        await Harness.CreateResourcesAsync(resources.ToArray());
    }
}
