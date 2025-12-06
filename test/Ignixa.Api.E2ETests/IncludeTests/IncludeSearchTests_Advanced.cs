// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;

namespace Ignixa.Api.E2ETests.IncludeTests;

/// <summary>
/// E2E tests for advanced FHIR _include and _revinclude functionality.
/// Tests pagination, sorting, and wildcard sources.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_Advanced : IncludeTestBase
{
    public IncludeSearchTests_Advanced(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Pagination Tests

    /// <summary>
    /// Tests _include with _count pagination.
    /// Ported from: GivenAnIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("DiagnosticReport", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create two patients with diagnostic reports
        var smithPatient = CreatePatientWithReferences(tag, "Smith");
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");
        var createdPatients = await Harness.CreateResourcesAsync([smithPatient, trumanPatient]);

        var smithReport = CreateDiagnosticReport(tag, createdPatients[0].Id, snomedCode, snomedSystem);
        var trumanReport = CreateDiagnosticReport(tag, createdPatients[1].Id, snomedCode, snomedSystem);
        await Harness.CreateResourcesAsync([smithReport, trumanReport]);

        // Act - search with count=1
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport",
            $"_tag={tag}&_include=DiagnosticReport:patient:Patient&code={snomedCode}&_count=1");

        // Assert - first page should have 1 match + 1 include
        GetCountBySearchMode(bundle, "match").Should().Be(1);
        GetCountBySearchMode(bundle, "include").Should().Be(1);

        // Follow next link
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty();

        var nextBundle = await Harness.GetBundleAsync(nextLink!);

        // Assert - second page should have 1 match + 1 include
        GetCountBySearchMode(nextBundle, "match").Should().Be(1);
        GetCountBySearchMode(nextBundle, "include").Should().Be(1);
    }

    /// <summary>
    /// Tests _revinclude with _count pagination.
    /// Ported from: GivenARevIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create observations
        var patient1 = CreatePatientWithReferences(tag, "Patient1");
        var patient2 = CreatePatientWithReferences(tag, "Patient2");
        var createdPatients = await Harness.CreateResourcesAsync([patient1, patient2]);

        var obs1 = CreateObservation(tag, createdPatients[0].Id, snomedCode, snomedSystem);
        var obs2 = CreateObservation(tag, createdPatients[1].Id, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourcesAsync([obs1, obs2]);

        // Create diagnostic reports
        var report1 = CreateDiagnosticReport(tag, createdPatients[0].Id, snomedCode, snomedSystem, createdObs[0].Id);
        var report2 = CreateDiagnosticReport(tag, createdPatients[1].Id, snomedCode, snomedSystem, createdObs[1].Id);
        await Harness.CreateResourcesAsync([report1, report2]);

        // Act - search with count=1
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:result&code={snomedCode}&_count=1");

        // Assert - first page
        GetCountBySearchMode(bundle, "match").Should().Be(1);
        GetCountBySearchMode(bundle, "include").Should().Be(1);

        // Follow next link
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty();

        var nextBundle = await Harness.GetBundleAsync(nextLink!);

        // Assert - second page
        GetCountBySearchMode(nextBundle, "match").Should().Be(1);
        GetCountBySearchMode(nextBundle, "include").Should().Be(1);
    }

    #endregion

    #region Sort with Include Tests

    /// <summary>
    /// Tests _include with _sort parameter.
    /// Ported from: GivenAnIncludeSearchWithSortAndResourcesWithAndWithoutTheIncludeParameter_WhenSearched_ThenCorrectResultsAreReturned
    /// </summary>
    [Fact(Skip = "Waiting for _sort support with _include")]
    public async Task GivenAnIncludeSearchWithSort_WhenSearched_ThenCorrectResultsAreReturned()
    {
        // This test validates that _sort works correctly with _include
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with _sort parameter (ascending).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearchedAndSorted_TheIterativeResultsShouldBeAddedToTheBundleAsc
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate and _sort support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithSort_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundleAsc()
    {
        // This test validates that _sort works correctly with _revinclude:iterate
        await Task.CompletedTask;
    }

    #endregion

    #region Wildcard Source Tests

    /// <summary>
    /// Tests _revinclude with wildcard source (*:*).
    /// Ported from: GivenARevIncludeSearchWildcardSourceExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact(Skip = "Waiting for wildcard _revinclude (*:*) support")]
    public async Task GivenARevIncludeSearchWildcardSourceExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        // Create various resources that reference the organization
        var location = CreateLocation(tag, createdOrg.Id);
        await Harness.CreateResourceAsync(location);

        var patient = CreatePatientWithReferences(tag, "TestPatient", managingOrganizationId: createdOrg.Id);
        await Harness.CreateResourceAsync(patient);

        // Act - wildcard source revinclude
        var bundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=*:*&_tag={tag}");

        // Assert - should include all resources referencing the organization
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Organization");
        resourceTypes.Should().Contain("Location");
        resourceTypes.Should().Contain("Patient");

        // Verify included resources are not counted
        var countBundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=*:*&_tag={tag}&_summary=count");
        countBundle.Total.Should().Be(1);
    }

    #endregion
}
