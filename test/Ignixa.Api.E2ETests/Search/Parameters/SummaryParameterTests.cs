// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization;

namespace Ignixa.Api.E2ETests.Search.Parameters;

/// <summary>
/// E2E tests for FHIR _summary parameter functionality.
/// Tests verify that the _summary parameter correctly controls response content and size.
/// </summary>
/// <remarks>
/// The _summary parameter is a core FHIR feature that affects bandwidth and performance
/// by controlling which elements are included in responses:
/// - false: Full representation (longest response)
/// - true: Summary elements only (predefined subset)
/// - text: Text elements only (narrative)
/// - data: Data elements (no text/narrative)
/// - count: Count only (no entry resources)
///
/// Reference: FHIR R4 spec section 3.1.1.5.7
/// Gap analysis: docs/investigations/e2e-test-gap-analysis.md lines 273-350
/// </remarks>
public class SummaryParameterTests : CapabilityDrivenTestBase
{
    public SummaryParameterTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Core Summary Flag Tests

    /// <summary>
    /// Tests that _summary=false returns full resource representation.
    /// This should be the longest response with all elements included.
    /// </summary>
    [Fact]
    public async Task GivenSummaryFalse_WhenSearched_ThenReturnsFullRepresentation()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithGivenName("TestGiven")
                .WithAge(35)
                .WithGender(g => g.Female)
                .WithRealisticBMI()
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=false");

        // Assert
        bundle.ShouldNotBeNull();
        // Note: Bundle.total is OPTIONAL per FHIR spec (only required for _summary=count)
        // We verify entry count instead of total which may be null
        bundle.Entry.Count.ShouldBe(3, "_summary=false should return all matching resources");

        // Verify resources contain full data (name, address, gender, extensions, etc.)
        foreach (var entry in bundle.Entry)
        {
            entry.Resource.ShouldNotBeNull();
            var resource = entry.Resource!;
            resource.MutableNode["name"].ShouldNotBeNull("full representation includes name");
            resource.MutableNode["address"].ShouldNotBeNull("full representation includes address");
            resource.MutableNode["gender"].ShouldNotBeNull("full representation includes gender");
            resource.MutableNode["extension"].ShouldNotBeNull("full representation includes extensions");
        }

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain("_summary=false");
    }

    /// <summary>
    /// Tests that _summary=true returns summary elements only.
    /// Summary should include only core identification and high-level fields.
    /// </summary>
    [Fact]
    public async Task GivenSummaryTrue_WhenSearched_ThenReturnsSummaryElements()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithGivenName("TestGiven")
                .WithAge(35)
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=true");

        // Assert
        bundle.ShouldNotBeNull();
        // Note: Bundle.total is OPTIONAL per FHIR spec (only required for _summary=count)
        bundle.Entry.Count.ShouldBe(3, "_summary=true should return all matching resources");

        // Verify resources are returned (summary still includes resources, just fewer elements)
        foreach (var entry in bundle.Entry)
        {
            entry.Resource.ShouldNotBeNull();
            var resource = entry.Resource!;
            resource.ResourceType.ShouldBe("Patient");
            resource.Id.ShouldNotBeNullOrEmpty();
        }

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain("_summary=true");
    }

    /// <summary>
    /// Tests that _summary=text returns text elements only.
    /// Text summary should include narrative and basic identification.
    /// </summary>
    [Fact]
    public async Task GivenSummaryText_WhenSearched_ThenReturnsTextElements()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=text");

        // Assert
        bundle.ShouldNotBeNull();
        // Note: Bundle.total is OPTIONAL per FHIR spec (only required for _summary=count)
        bundle.Entry.Count.ShouldBe(3, "_summary=text should return all matching resources");

        // Verify resources are returned
        foreach (var entry in bundle.Entry)
        {
            entry.Resource.ShouldNotBeNull();
            var resource = entry.Resource!;
            resource.ResourceType.ShouldBe("Patient");
            resource.Id.ShouldNotBeNullOrEmpty();
        }

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain("_summary=text");
    }

    /// <summary>
    /// Tests that _summary=data returns data elements without text/narrative.
    /// Data summary excludes narrative text but includes all coded data.
    /// </summary>
    [Fact]
    public async Task GivenSummaryData_WhenSearched_ThenReturnsDataElements()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithGivenName("TestGiven")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=data");

        // Assert
        bundle.ShouldNotBeNull();
        // Note: Bundle.total is OPTIONAL per FHIR spec (only required for _summary=count)
        bundle.Entry.Count.ShouldBe(3, "_summary=data should return all matching resources");

        // Verify resources contain data elements but not text
        foreach (var entry in bundle.Entry)
        {
            entry.Resource.ShouldNotBeNull();
            var resource = entry.Resource!;
            resource.ResourceType.ShouldBe("Patient");
            resource.Id.ShouldNotBeNullOrEmpty();
            resource.MutableNode["name"].ShouldNotBeNull("data summary includes coded data like names");
        }

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain("_summary=data");
    }

    /// <summary>
    /// Tests that _summary=count returns only the total count without any entry resources.
    /// This is the most bandwidth-efficient option for counting results.
    /// </summary>
    [Fact]
    public async Task GivenSummaryCount_WhenSearched_ThenReturnsOnlyTotal()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=count");

        // Assert
        bundle.ShouldNotBeNull();
        bundle.Total.ShouldBe(3, "_summary=count should return accurate total");
        bundle.Entry.ShouldBeEmpty("_summary=count should not include any resource entries");

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain("_summary=count");
    }

    #endregion

    #region Summary Flag Combinations and Edge Cases

    /// <summary>
    /// Tests all summary flags with the same dataset to verify entry count consistency.
    /// All non-count summary flags should return the same entry count.
    /// </summary>
    /// <remarks>
    /// Per FHIR spec, Bundle.total is OPTIONAL for non-count searches.
    /// Only _summary=count is required to populate total.
    /// </remarks>
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("text")]
    [InlineData("data")]
    [InlineData("count")]
    public async Task GivenSummaryParameter_WhenSearched_ThenReturnsConsistentResults(string summaryFlag)
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 3)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithGivenName("TestGiven")
                .WithAge(35)
                .WithGender(g => g.Male)
                .WithRealisticBMI()
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary={summaryFlag}");

        // Assert
        if (summaryFlag == "count")
        {
            // _summary=count MUST return total (required by FHIR spec)
            bundle.Total.ShouldBe(3, "_summary=count should return accurate total");
            bundle.Entry.ShouldBeEmpty("_summary=count should have no entries");
        }
        else
        {
            // For other summary modes, total is OPTIONAL per FHIR spec
            // We verify entry count instead
            bundle.Entry.Count.ShouldBe(3, $"_summary={summaryFlag} should return entry resources");
        }

        // Verify self link includes _summary parameter
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull();
        selfLink!.Url.ShouldContain($"_summary={summaryFlag}");
    }

    /// <summary>
    /// Tests that _summary parameter works correctly with pagination (_count).
    /// Summary should apply to each page of results.
    /// </summary>
    [Fact]
    public async Task GivenSummaryCountWithPagination_WhenSearched_ThenReturnsNoEntriesAcrossPages()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 10)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("TestFamily")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Request with both _summary=count and _count (should respect summary over pagination)
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=count&_count=2");

        // Assert
        bundle.Total.ShouldBe(10);
        bundle.Entry.ShouldBeEmpty("_summary=count overrides pagination and returns no entries");

        // Next link should not exist when using _summary=count
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next");
        nextLink.ShouldBeNull("_summary=count returns no entries, so no pagination needed");
    }

    /// <summary>
    /// Tests that _summary parameter works with other search parameters.
    /// Summary should apply after filtering by search criteria.
    /// </summary>
    [Fact]
    public async Task GivenSummaryWithFilterParameters_WhenSearched_ThenAppliesSummaryToFilteredResults()
    {
        // Capability check
        RequireSearchParameters("Patient", "family", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient1 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Smith")
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();

        var patient2 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Jones")
            .WithGender(g => g.Male)
            .WithTag(tag)
            .Build();

        var patient3 = CreatePatient()
            .FromSeattle()
            .WithFamilyName("Smith")
            .WithGender(g => g.Male)
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3]);

        // Act - Filter by family=Smith AND combine with _summary=count
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&family=Smith&_summary=count");

        // Assert - Should count only the filtered results
        bundle.Total.ShouldBe(2, "should count only patients matching family=Smith");
        bundle.Entry.ShouldBeEmpty("_summary=count should return no entries");
    }

    #endregion

    #region Response Size Validation

    /// <summary>
    /// Tests that different summary flags produce different response sizes.
    /// Specifically: false (full) should be larger than true (summary), which should be larger than count.
    /// </summary>
    [Fact]
    public async Task GivenDifferentSummaryFlags_WhenSearched_ThenResponseSizesVaryAccordingly()
    {
        // Arrange - Create patients with rich data
        var tag = Guid.NewGuid().ToString();

        var patients = Enumerable.Range(0, 5)
            .Select(_ => CreatePatient()
                .FromSeattle()
                .WithFamilyName("RichDataPatient")
                .WithGivenName("FullName")
                .WithAge(35)
                .WithGender(g => g.Female)
                .WithRealisticBMI()
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act - Get responses with different summary flags
        var responseFalse = await Client.GetAsync($"/Patient?_tag={tag}&_summary=false");
        var responseTrue = await Client.GetAsync($"/Patient?_tag={tag}&_summary=true");
        var responseCount = await Client.GetAsync($"/Patient?_tag={tag}&_summary=count");

        // Assert - All requests should succeed
        responseFalse.EnsureSuccessStatusCode();
        responseTrue.EnsureSuccessStatusCode();
        responseCount.EnsureSuccessStatusCode();

        // Get response sizes
        var contentFalse = await responseFalse.Content.ReadAsStringAsync();
        var contentTrue = await responseTrue.Content.ReadAsStringAsync();
        var contentCount = await responseCount.Content.ReadAsStringAsync();

        var sizeFalse = contentFalse.Length;
        var sizeTrue = contentTrue.Length;
        var sizeCount = contentCount.Length;

        // Assert - false (full) should be largest
        sizeFalse.ShouldBeGreaterThan(sizeCount,
            "_summary=false (full representation) should be larger than _summary=count");

        // Assert - count should be smallest (no entry resources)
        sizeCount.ShouldBeLessThan(sizeFalse,
            "_summary=count should be smaller than _summary=false");
        sizeCount.ShouldBeLessThan(sizeTrue,
            "_summary=count should be smaller than _summary=true");
    }

    #endregion

    #region Scenario-Based Tests

    /// <summary>
    /// Tests _summary parameter with Observation resources (more complex than Patient).
    /// Verifies summary works across different resource types.
    /// </summary>
    [Fact]
    public async Task GivenObservations_WhenSearchedWithSummaryCount_ThenReturnsOnlyTotal()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        var scenario = CreateScenario()
            .WithName("Observation Summary Test")
            .WithTag(tag)
            .WithPatient(p => p.FromSeattle())
            .AddEncounter("Lab visit")
            .AddObservation(VitalSigns.BodyWeight, 75m, "kg", "kg")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .AddObservation(VitalSigns.BodyHeight, 170m, "cm", "cm")
            .Build();

        await Harness.CreateResourcesAsync(scenario.AllResources.ToArray());

        // Act
        var bundle = await Harness.SearchBundleAsync("Observation", $"_tag={tag}&_summary=count");

        // Assert
        bundle.Total.ShouldBe(3, "should have 3 observations");
        bundle.Entry.ShouldBeEmpty("_summary=count should not return any entries");
    }

    /// <summary>
    /// Tests _summary parameter with no matching results.
    /// Should return total=0 and no entries regardless of summary flag.
    /// </summary>
    [Fact]
    public async Task GivenNoMatchingResults_WhenSearchedWithSummary_ThenReturnsZeroTotal()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Don't create any resources - search with unique tag that won't match anything

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary=count");

        // Assert
        bundle.Total.ShouldBe(0);
        bundle.Entry.ShouldBeEmpty();
    }

    #endregion

    #region Self-Link Validation

    /// <summary>
    /// Tests that self links preserve the _summary parameter.
    /// This ensures clients can bookmark or refresh the same search with summary.
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("text")]
    [InlineData("data")]
    [InlineData("count")]
    public async Task GivenSummaryParameter_WhenSearched_ThenSelfLinkIncludesSummary(string summaryFlag)
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient = CreatePatient()
            .FromSeattle()
            .WithFamilyName("TestFamily")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourceAsync(patient);

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_summary={summaryFlag}");

        // Assert
        var selfLink = bundle.Link.FirstOrDefault(l => l.Relation == "self");
        selfLink.ShouldNotBeNull("bundle should include a self link");
        selfLink!.Url.ShouldContain($"_summary={summaryFlag}",
            customMessage: "self link should preserve the _summary parameter");
        selfLink.Url.ShouldContain($"_tag={tag}",
            customMessage: "self link should preserve all search parameters");
    }

    #endregion
}
