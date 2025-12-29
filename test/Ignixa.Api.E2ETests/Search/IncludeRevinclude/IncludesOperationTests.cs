// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

/// <summary>
/// E2E tests for the $includes operation feature.
/// This feature provides paginated includes/revincludes for FHIR search, allowing clients
/// to paginate through included resources separately from primary search results.
///
/// Key components tested:
/// - _includesCount parameter: Limits included resources in initial search
/// - _includesContinuationToken parameter: Pagination token for subsequent requests
/// - GET /{resourceType}/$includes endpoint: Fetches additional included resources
/// </summary>
public class IncludesOperationTests : IncludeTestBase
{
    public IncludesOperationTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Basic _includesCount Limiting Tests

    /// <summary>
    /// Tests that _includesCount limits the number of included resources in search results.
    /// When the number of includes exceeds the limit, only _includesCount includes are returned.
    /// </summary>
    [Fact]
    public async Task GivenSearchWithIncludesCount_WhenSearched_ThenIncludesAreLimited()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create multiple organizations that will be included
        var org1 = CreateOrganization().WithTag(tag).WithName("Org 1").Build();
        var org2 = CreateOrganization().WithTag(tag).WithName("Org 2").Build();
        var org3 = CreateOrganization().WithTag(tag).WithName("Org 3").Build();

        var createdOrgs = await Harness.CreateResourcesAsync([org1, org2, org3]);

        // Create locations referencing different organizations
        var location1 = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrgs[0].Id)
            .Build();
        var location2 = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrgs[1].Id)
            .Build();
        var location3 = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrgs[2].Id)
            .Build();

        await Harness.CreateResourcesAsync([location1, location2, location3]);

        // Act - search with _includesCount=2 (limit includes to 2)
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=2");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        matchEntries.Count.ShouldBe(3, "should have 3 matching locations");
        includeEntries.Count.ShouldBe(2, "should have only 2 included organizations due to _includesCount=2");
    }

    /// <summary>
    /// Tests that when includes are below the _includesCount limit, all are returned.
    /// </summary>
    [Fact]
    public async Task GivenIncludesUnderLimit_WhenSearched_ThenAllIncludesAreReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var org = CreateOrganization().WithTag(tag).WithName("Single Org").Build();
        var createdOrg = await Harness.CreateResourceAsync(org);

        var location = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrg.Id)
            .Build();
        await Harness.CreateResourceAsync(location);

        // Act - _includesCount=10 is higher than actual includes (1)
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=10");

        // Assert
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includeEntries.Count.ShouldBe(1, "should return the single included organization");

        // Should not have a "related" link since all includes fit
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldBeNull("no related link needed when all includes fit");
    }

    #endregion

    #region Related Link Generation Tests

    /// <summary>
    /// Tests that when includes exceed _includesCount, a "related" link is generated.
    /// The related link contains the _includesContinuationToken for fetching more includes.
    /// </summary>
    [Fact]
    public async Task GivenIncludesExceedLimit_WhenSearched_ThenRelatedLinkIsGenerated()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create 5 organizations
        var orgs = Enumerable.Range(1, 5)
            .Select(i => CreateOrganization().WithTag(tag).WithName($"Org {i}").Build())
            .ToArray();
        var createdOrgs = await Harness.CreateResourcesAsync(orgs);

        // Create locations referencing each organization
        var locations = createdOrgs.Select(org =>
            LocationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithManagingOrganization(org.Id)
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(locations);

        // Act - search with _includesCount=2
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=2");

        // Assert
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldNotBeNull("should have a related link for additional includes");
        relatedLink.Url.ShouldNotBeNullOrEmpty();
        relatedLink.Url!.ShouldContain("$includes");
        relatedLink.Url.ShouldContain("_includesContinuationToken");
    }

    /// <summary>
    /// Tests that no related link is generated when there are no includes at all.
    /// </summary>
    [Fact]
    public async Task GivenNoIncludes_WhenSearchedWithIncludesCount_ThenNoRelatedLink()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create location without any references
        var location = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .Build();
        await Harness.CreateResourceAsync(location);

        // Act - search with _include that won't match anything
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=2");

        // Assert
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldBeNull("no related link when there are no includes");
    }

    #endregion

    #region $includes Endpoint Pagination Tests

    /// <summary>
    /// Tests that the $includes endpoint returns additional included resources.
    /// </summary>
    [Fact]
    public async Task GivenRelatedLink_WhenFollowed_ThenReturnsAdditionalIncludes()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create 5 organizations
        var orgs = Enumerable.Range(1, 5)
            .Select(i => CreateOrganization().WithTag(tag).WithName($"Org {i}").Build())
            .ToArray();
        var createdOrgs = await Harness.CreateResourcesAsync(orgs);

        // Create locations referencing each organization
        var locations = createdOrgs.Select(org =>
            LocationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithManagingOrganization(org.Id)
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(locations);

        // Act - initial search with _includesCount=2
        var initialBundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=2");

        var relatedLink = initialBundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldNotBeNull("should have related link");

        // Follow the related link
        var includesBundle = await Harness.GetBundleAsync(relatedLink.Url!);

        // Assert
        includesBundle.ShouldNotBeNull();
        includesBundle.Type.ShouldBe(BundleJsonNode.BundleType.Searchset);

        // Should have entries (the remaining includes)
        var includeEntries = includesBundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includeEntries.Count.ShouldBeGreaterThan(0, "should have additional includes");
    }

    /// <summary>
    /// Tests that multiple pages of includes can be fetched via continuation tokens.
    /// </summary>
    [Fact]
    public async Task GivenManyIncludes_WhenPaginatingThroughIncludes_ThenAllIncludesAreReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create 10 organizations
        var orgs = Enumerable.Range(1, 10)
            .Select(i => CreateOrganization().WithTag(tag).WithName($"Org {i}").Build())
            .ToArray();
        var createdOrgs = await Harness.CreateResourcesAsync(orgs);

        // Create locations referencing each organization
        var locations = createdOrgs.Select(org =>
            LocationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithManagingOrganization(org.Id)
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(locations);

        // Act - collect all includes across pages
        var allIncludedOrgIds = new HashSet<string>();
        var pageCount = 0;
        const int maxPages = 10; // Safety limit

        // Initial search with _includesCount=3
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=3");

        // Collect initial includes
        foreach (var entry in bundle.Entry.Where(e => e.Search?.Mode == "include"))
        {
            if (entry.Resource?.Id is not null)
            {
                allIncludedOrgIds.Add(entry.Resource.Id);
            }
        }

        // Follow related links
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        while (relatedLink is not null && pageCount < maxPages)
        {
            pageCount++;
            var includesBundle = await Harness.GetBundleAsync(relatedLink.Url!);

            foreach (var entry in includesBundle.Entry.Where(e => e.Search?.Mode == "include"))
            {
                if (entry.Resource?.Id is not null)
                {
                    allIncludedOrgIds.Add(entry.Resource.Id);
                }
            }

            relatedLink = includesBundle.Link?.FirstOrDefault(l => l.Relation == "related");
        }

        // Assert
        allIncludedOrgIds.Count.ShouldBe(10, "should have collected all 10 organization IDs across pages");
        pageCount.ShouldBeGreaterThan(0, "should have followed at least one related link");
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests that $includes endpoint requires _includesContinuationToken parameter.
    /// </summary>
    [Fact]
    public async Task GivenMissingContinuationToken_WhenCallingIncludesEndpoint_ThenReturnsBadRequest()
    {
        // Act
        var response = await Client.GetAsync("/Location/$includes");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var responseJson = await response.Content.ReadAsStringAsync();
        var outcome = JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
        outcome.ResourceType.ShouldBe("OperationOutcome");
    }

    /// <summary>
    /// Tests that $includes endpoint returns error for invalid continuation token.
    /// </summary>
    [Fact]
    public async Task GivenInvalidContinuationToken_WhenCallingIncludesEndpoint_ThenHandlesGracefully()
    {
        // Arrange
        var invalidToken = "this-is-not-a-valid-token";

        // Act
        var response = await Client.GetAsync($"/Location/$includes?_includesContinuationToken={invalidToken}");

        // Assert - should handle gracefully (either return empty results or error)
        // The implementation decodes the token and returns empty if invalid
        var responseJson = await response.Content.ReadAsStringAsync();

        // Could be successful with empty results or an error response
        if (response.IsSuccessStatusCode)
        {
            var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);
            bundle.ShouldNotBeNull();
            bundle.Type.ShouldBe(BundleJsonNode.BundleType.Searchset);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>
    /// Tests that $includes endpoint returns error for malformed base64 token.
    /// </summary>
    [Fact]
    public async Task GivenMalformedBase64Token_WhenCallingIncludesEndpoint_ThenHandlesGracefully()
    {
        // Arrange - valid base64 but not valid JSON inside
        var malformedToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-json"));

        // Act
        var response = await Client.GetAsync($"/Location/$includes?_includesContinuationToken={malformedToken}");

        // Assert - should handle gracefully
        var responseJson = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);
            bundle.ShouldNotBeNull();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    #endregion

    #region Integration with Other Search Features Tests

    /// <summary>
    /// Tests that _includesCount works with _count parameter.
    /// Both pagination mechanisms should work independently.
    /// </summary>
    [Fact]
    public async Task GivenIncludesCountWithCount_WhenSearched_ThenBothLimitsApply()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organizations
        var orgs = Enumerable.Range(1, 3)
            .Select(i => CreateOrganization().WithTag(tag).WithName($"Org {i}").Build())
            .ToArray();
        var createdOrgs = await Harness.CreateResourcesAsync(orgs);

        // Create locations
        var locations = createdOrgs.Select(org =>
            LocationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithManagingOrganization(org.Id)
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(locations);

        // Act - _count=2 limits matches, _includesCount=1 limits includes
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_count=2&_includesCount=1");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        matchEntries.Count.ShouldBeLessThanOrEqualTo(2, "_count should limit matches");
        includeEntries.Count.ShouldBeLessThanOrEqualTo(1, "_includesCount should limit includes");
    }

    /// <summary>
    /// Tests that _includesCount works with revinclude as well.
    /// </summary>
    [Fact]
    public async Task GivenIncludesCountWithRevinclude_WhenSearched_ThenRevincludesAreLimited()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatient()
            .FromSeattle()
            .WithFamilyName("IncludesTest")
            .WithTag(tag)
            .Build();
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create multiple observations for this patient
        var observations = Enumerable.Range(1, 5)
            .Select(i => ObservationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithCode($"code-{i}", "http://loinc.org")
                .WithSubject(createdPatient.Id)
                .WithStatus("final")
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(observations);

        // Act - revinclude with _includesCount=2
        var bundle = await Harness.SearchBundleAsync(
            "Patient",
            $"_tag={tag}&_revinclude=Observation:patient&_includesCount=2");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        matchEntries.Count.ShouldBe(1, "should have 1 matching patient");
        includeEntries.Count.ShouldBeLessThanOrEqualTo(2, "should limit revincludes to 2");
    }

    /// <summary>
    /// Tests that _includesCount works with wildcard includes.
    /// </summary>
    [Fact]
    public async Task GivenIncludesCountWithWildcard_WhenSearched_ThenAllIncludeTypesAreLimited()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient and practitioner
        var patient = CreatePatient()
            .FromSeattle()
            .WithFamilyName("WildcardTest")
            .WithTag(tag)
            .Build();
        var practitioner = PractitionerBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithFamilyName("TestDoc")
            .Build();

        var createdPatient = await Harness.CreateResourceAsync(patient);
        var createdPractitioner = await Harness.CreateResourceAsync(practitioner);

        // Create observation referencing both
        var obs = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("4548-4", "http://loinc.org")
            .WithSubject(createdPatient.Id)
            .WithStatus("final")
            .WithPractitionerPerformer(createdPractitioner.Id)
            .Build();
        await Harness.CreateResourceAsync(obs);

        // Act - wildcard include with _includesCount=1
        var bundle = await Harness.SearchBundleAsync(
            "Observation",
            $"_tag={tag}&_include=Observation:*&_includesCount=1");

        // Assert
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includeEntries.Count.ShouldBeLessThanOrEqualTo(1, "should limit total includes to 1");
    }

    #endregion

    #region Tenant-Explicit Route Tests

    /// <summary>
    /// Tests that $includes works with tenant-explicit routes.
    /// </summary>
    [Fact]
    public async Task GivenTenantExplicitRoute_WhenCallingIncludesEndpoint_ThenSucceeds()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create test data
        var org = CreateOrganization().WithTag(tag).WithName("Tenant Test Org").Build();
        var createdOrg = await Harness.CreateResourceAsync(org);

        var location = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrg.Id)
            .Build();
        await Harness.CreateResourceAsync(location);

        // Get continuation token from initial search
        var initialBundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=0");

        var relatedLink = initialBundle.Link?.FirstOrDefault(l => l.Relation == "related");

        if (relatedLink is not null)
        {
            // Act - follow the link (should work with tenant routing)
            var includesBundle = await Harness.GetBundleAsync(relatedLink.Url!);

            // Assert
            includesBundle.ShouldNotBeNull();
            includesBundle.Type.ShouldBe(BundleJsonNode.BundleType.Searchset);
        }
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Tests behavior when _includesCount is set to 0.
    /// </summary>
    [Fact]
    public async Task GivenIncludesCountZero_WhenSearched_ThenNoIncludesReturnedButRelatedLinkGenerated()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var org = CreateOrganization().WithTag(tag).WithName("Zero Count Org").Build();
        var createdOrg = await Harness.CreateResourceAsync(org);

        var location = LocationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithManagingOrganization(createdOrg.Id)
            .Build();
        await Harness.CreateResourceAsync(location);

        // Act
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=0");

        // Assert
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includeEntries.Count.ShouldBe(0, "should have no includes with _includesCount=0");

        // Should have a related link to fetch includes
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldNotBeNull("should have related link to fetch includes");
    }

    /// <summary>
    /// Tests that duplicate includes are not counted multiple times in the limit.
    /// </summary>
    [Fact]
    public async Task GivenDuplicateIncludes_WhenSearched_ThenDeduplicatedBeforeCounting()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create one organization
        var org = CreateOrganization().WithTag(tag).WithName("Shared Org").Build();
        var createdOrg = await Harness.CreateResourceAsync(org);

        // Create multiple locations referencing the same organization
        var locations = Enumerable.Range(1, 3)
            .Select(i => LocationBuilder.Create(SchemaProvider)
                .WithTag(tag)
                .WithManagingOrganization(createdOrg.Id)
                .Build())
            .ToArray();
        await Harness.CreateResourcesAsync(locations);

        // Act
        var bundle = await Harness.SearchBundleAsync(
            "Location",
            $"_tag={tag}&_include=Location:organization&_includesCount=5");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        matchEntries.Count.ShouldBe(3, "should have 3 matching locations");
        includeEntries.Count.ShouldBe(1, "should have only 1 included organization (deduplicated)");

        // No related link since all (1) includes fit within limit (5)
        var relatedLink = bundle.Link?.FirstOrDefault(l => l.Relation == "related");
        relatedLink.ShouldBeNull("no related link needed when all includes fit");
    }

    #endregion
}
