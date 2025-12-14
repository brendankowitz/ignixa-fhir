// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Scenarios;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.Chaining;

/// <summary>
/// E2E tests for FHIR chained search functionality.
/// Tests forward chaining (Patient?organization.name=X), reverse chaining (_has),
/// and multi-level chaining (Observation?subject:Patient.organization.name=X).
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.ChainingSearchTests
/// </summary>
/// <remarks>
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class ChainingSearchTests : CapabilityDrivenTestBase
{
    public ChainingSearchTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Test Data Setup

    /// <summary>
    /// Creates the standard test data using the ChainingTestScenario.
    /// </summary>
    private async Task<ChainingTestData> CreateChainingTestDataAsync(string tag)
    {
        var data = SchemaProvider.GetChainingSearchScenario(tag);
        await Harness.CreateResourcesAsync(data.AllResources.ToArray());
        return data;
    }

    #endregion

    #region Forward Chaining Tests

    /// <summary>
    /// Tests a basic chained search expression: DiagnosticReport?subject:Patient.name=X
    /// Ported from: GivenAChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// SKIPPED: Forward chaining with Patient.name returns empty results.
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Search for DiagnosticReports where subject:Patient.name matches Smith's given name
        var query = $"_tag={tag}&subject:Patient.name={data.SmithPatientGivenName}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - Should return both Smith's SNOMED and LOINC diagnostic reports
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a nested chained search expression: DiagnosticReport?result.subject:Patient.name=X
    /// Ported from: GivenANestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Search via result (Observation) -> subject (Patient) -> name
        var query = $"_tag={tag}&result.subject:Patient.name={data.SmithPatientGivenName}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a multi-level (3-hop) chained search: DiagnosticReport?result.subject:Patient.organization.address-city=X
    /// Ported from: GivenAMultiNestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAMultiNestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - 3-hop chain: DiagnosticReport -> result (Observation) -> subject (Patient) -> organization -> address-city
        var query = $"_tag={tag}&result.subject:Patient.organization.address-city={data.OrganizationCity}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - Only Smith's reports (linked to org with that city)
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a chained search with OR condition in final parameter: subject:Patient.name=Smith,Truman
    /// Ported from: GivenANestedChainedSearchExpressionWithAnOrFinalCondition_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANestedChainedSearchExpressionWithAnOrFinalCondition_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Search with OR on final condition (comma-separated values)
        var query = $"_tag={tag}&result.subject:Patient.name={data.SmithPatientGivenName},{data.TrumanPatientGivenName}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - Should return all 4 diagnostic reports (2 Smith + 2 Truman)
        results.Should().HaveCount(4);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a chained search over a simple parameter (_tag): DiagnosticReport?subject:Patient._tag=X
    /// Ported from: GivenAChainedSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Chain on _tag parameter
        var query = $"_tag={tag}&subject:Patient._tag={tag}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - All 4 diagnostic reports
        results.Should().HaveCount(4);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a chained search with pagination: subject:Patient._tag=X&amp;_count=2
    /// Ported from: GivenAChainedSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Search with pagination
        var query = $"_tag={tag}&subject:Patient._tag={tag}&_count=2";
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport", query);

        // Assert - First page should have 2 results
        bundle.Entry.Should().HaveCount(2);

        // Get next page
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty("Should have next link for pagination");

        var nextBundle = await Harness.GetBundleAsync(nextLink!);

        // Assert - Second page should have remaining 2 results
        nextBundle.Entry.Should().HaveCount(2);

        // Verify all 4 reports are returned across both pages
        var allIds = bundle.Entry.Select(e => e.Resource!.Id)
            .Concat(nextBundle.Entry.Select(e => e.Resource!.Id))
            .ToList();
        allIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        allIds.Should().Contain(data.SmithLoincDiagnosticReport.Id);
        allIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
        allIds.Should().Contain(data.TrumanLoincDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests a chained search with no results: subject:Patient._type=Observation (Patient can't be Observation)
    /// Ported from: GivenAChainedSearchExpressionOverASimpleParameterWithNoResults_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpressionOverASimpleParameterWithNoResults_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        await CreateChainingTestDataAsync(tag);

        // Act - Search with impossible condition (_type=Observation for Patient)
        var query = $"_tag={tag}&subject:Patient._type=Observation";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - No results
        results.Should().BeEmpty();
    }

    /// <summary>
    /// Tests chained search with :not modifier: subject:Patient.gender:not=female
    /// Ported from: GivenAChainedSearchExpressionWithNotProvider_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpressionWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Search for observations where subject is NOT female
        var notFemaleQuery = $"subject:Patient.gender:not=female&subject:Patient._tag={tag}";
        var notFemaleResults = await Harness.SearchAsync("Observation", notFemaleQuery);

        // Smith (male) and Truman (male) have 2+1 observations each = 4 observations (excluding Device observations)
        notFemaleResults.Should().HaveCount(4);

        // Act - Search for observations where subject is NOT male
        var notMaleQuery = $"subject:Patient.gender:not=male&subject:Patient._tag={tag}";
        var notMaleResults = await Harness.SearchAsync("Observation", notMaleQuery);

        // Adams (female) has 1 observation
        notMaleResults.Should().HaveCount(1);

        // Verify totals add up
        var allPatientQuery = $"subject:Patient._tag={tag}";
        var allPatientResults = await Harness.SearchAsync("Observation", allPatientQuery);
        allPatientResults.Should().HaveCount(notFemaleResults.Length + notMaleResults.Length);
    }

    #endregion

    #region Reverse Chaining (_has) Tests

    /// <summary>
    /// Tests reverse chaining: Patient?_has:Observation:patient:code=X
    /// Ported from: GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Reverse chain: find patients who have observations with the SNOMED code
        var query = $"_tag={tag}&_has:Observation:patient:code={data.SnomedCode}";
        var results = await Harness.SearchAsync("Patient", query);

        // Assert - Smith and Truman have SNOMED observations
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithPatient.Id);
        resultIds.Should().Contain(data.TrumanPatient.Id);
    }

    /// <summary>
    /// Tests reverse chaining with pagination.
    /// Ported from: GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Reverse chain with pagination
        var query = $"_tag={tag}&_has:Observation:patient:code={data.SnomedCode}&_count=1";
        var bundle = await Harness.SearchBundleAsync("Patient", query);

        // Assert - First page has 1 result
        bundle.Entry.Should().HaveCount(1);

        // Get next page
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty();

        var nextBundle = await Harness.GetBundleAsync(nextLink!);
        nextBundle.Entry.Should().HaveCount(1);

        // Verify both patients returned across pages
        var allIds = bundle.Entry.Select(e => e.Resource!.Id)
            .Concat(nextBundle.Entry.Select(e => e.Resource!.Id))
            .ToList();
        allIds.Should().Contain(data.SmithPatient.Id);
        allIds.Should().Contain(data.TrumanPatient.Id);
    }

    /// <summary>
    /// Tests reverse chaining with multiple target types: ?_type=Patient,Device&amp;_has:Observation:subject:code=X
    /// Ported from: GivenAReverseChainSearchExpressionWithMultipleTargetTypes_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAReverseChainSearchExpressionWithMultipleTargetTypes_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - System-level search with _type=Patient,Device and reverse chain
        var query = $"_tag={tag}&_type=Patient,Device&_has:Observation:subject:code={data.SnomedCode}";
        var bundle = await Harness.SearchSystemAsync(query);

        // Assert - Should return Smith, Truman, and DeviceSnomedSubject
        var resources = bundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!).ToList();
        resources.Should().HaveCount(3);
        var resultIds = resources.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithPatient.Id);
        resultIds.Should().Contain(data.TrumanPatient.Id);
        resultIds.Should().Contain(data.DeviceSnomedSubject.Id);
    }

    /// <summary>
    /// Tests nested reverse chaining: Patient?_has:Observation:patient:_has:DiagnosticReport:result:code=X
    /// Ported from: GivenANestedReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANestedReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Nested reverse chain: Patient <- Observation <- DiagnosticReport
        var query = $"_tag={tag}&_has:Observation:patient:_has:DiagnosticReport:result:code={data.SnomedCode}";
        var results = await Harness.SearchAsync("Patient", query);

        // Assert - Smith and Truman have observations linked to SNOMED diagnostic reports
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithPatient.Id);
        resultIds.Should().Contain(data.TrumanPatient.Id);
    }

    /// <summary>
    /// Tests reverse chaining with _id parameter: Patient?_has:Group:member:_id=X
    /// Ported from: GivenANestedReverseChainSearchExpressionOverTheIdResourceParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANestedReverseChainSearchExpressionOverTheIdResourceParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Reverse chain on Group member with _id filter
        var query = $"_tag={tag}&_has:Group:member:_id={data.PatientGroup.Id}";
        var results = await Harness.SearchAsync("Patient", query);

        // Assert - Adams, Smith, and Truman are members of the group
        results.Should().HaveCount(3);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.AdamsPatient.Id);
        resultIds.Should().Contain(data.SmithPatient.Id);
        resultIds.Should().Contain(data.TrumanPatient.Id);
    }

    /// <summary>
    /// Tests reverse chaining with _type parameter: Patient?_has:Group:member:_type=Group
    /// Ported from: GivenANestedReverseChainSearchExpressionOverTheTypeResourceParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenANestedReverseChainSearchExpressionOverTheTypeResourceParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - _type=Group should match (Group resource type)
        var groupTypeQuery = $"_tag={tag}&_has:Group:member:_type=Group";
        var groupTypeResults = await Harness.SearchAsync("Patient", groupTypeQuery);
        groupTypeResults.Should().NotBeEmpty();

        // Act - _type=Patient should NOT match (Group is not a Patient)
        var patientTypeQuery = $"_tag={tag}&_has:Group:member:_type=Patient";
        var patientTypeResults = await Harness.SearchAsync("Patient", patientTypeQuery);
        patientTypeResults.Should().BeEmpty();
    }

    /// <summary>
    /// Tests reverse chain with deleted resource handling: _summary=count should not count deleted patients.
    /// Ported from: GivenCountOnlyReverseChainSearchWithDeletedResource_WhenSearched_ThenCorrectCountIsReturned
    /// </summary>
    [Fact]
    public async Task GivenCountOnlyReverseChainSearchWithDeletedResource_WhenSearched_ThenCorrectCountIsReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Create and then delete a patient with CareTeam
        var (deletedPatient, deletedCareTeam) = SchemaProvider.GetDeletedPatientCareTeamScenario(tag);
        await Harness.CreateResourcesAsync([deletedPatient, deletedCareTeam]);
        await DeleteResourceAsync($"Patient/{deletedPatient.Id}");

        // Act - Reverse chain with _summary=count (should only count non-deleted patients)
        var query = $"_has:CareTeam:patient:_tag={tag}&_summary=count";
        var bundle = await Harness.SearchBundleAsync("Patient", query);

        // Assert - Only 1 (Adams), not 2 (deleted patient should not be counted)
        bundle.Total.Should().Be(1);
    }

    #endregion

    #region Combined Forward and Reverse Chaining Tests

    /// <summary>
    /// Tests combination of forward and reverse chaining.
    /// Ported from: GivenACombinationOfChainingReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenACombinationOfChainingReverseChainSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Forward chain (code) + reverse chain (_has:Group:member)
        var query = $"_tag={tag}&code={data.SnomedCode}&patient:Patient._has:Group:member:_tag={tag}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert - Only SNOMED diagnostic reports for patients in the group
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests combination of forward and reverse chaining with pagination.
    /// Ported from: GivenACombinationOfChainingReverseChainSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenACombinationOfChainingReverseChainSearchExpression_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Combined chaining with pagination
        var query = $"_tag={tag}&code={data.SnomedCode}&patient:Patient._has:Group:member:_tag={tag}&_count=1";
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport", query);

        // Assert - First page
        bundle.Entry.Should().HaveCount(1);

        // Get second page
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty();

        var nextBundle = await Harness.GetBundleAsync(nextLink!);
        nextBundle.Entry.Should().HaveCount(1);

        // Verify both SNOMED reports returned
        var allIds = bundle.Entry.Select(e => e.Resource!.Id)
            .Concat(nextBundle.Entry.Select(e => e.Resource!.Id))
            .ToList();
        allIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        allIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests combination of forward and reverse chaining with _id parameter.
    /// Ported from: GivenACombinationOfChainingReverseChainSearchExpressionOverAResourceTableParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverAResourceTableParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Combined with _id filter
        var query = $"_tag={tag}&code={data.SnomedCode}&patient:Patient._has:Group:member:_id={data.PatientGroup.Id}";
        var results = await Harness.SearchAsync("DiagnosticReport", query);

        // Assert
        results.Should().HaveCount(2);
        var resultIds = results.Select(r => r.Id).ToList();
        resultIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        resultIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
    }

    /// <summary>
    /// Tests combination of forward and reverse chaining with _id parameter and pagination.
    /// Ported from: GivenACombinationOfChainingReverseChainSearchExpressionOverAResourceTableParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverAResourceTableParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Act - Combined with _id and pagination
        var query = $"_tag={tag}&code={data.SnomedCode}&patient:Patient._has:Group:member:_id={data.PatientGroup.Id}&_count=1";
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport", query);

        // Assert - First page
        bundle.Entry.Should().HaveCount(1);

        // Get second page
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        nextLink.Should().NotBeNullOrEmpty();

        var nextBundle = await Harness.GetBundleAsync(nextLink!);
        nextBundle.Entry.Should().HaveCount(1);

        // Verify both SNOMED reports returned
        var allIds = bundle.Entry.Select(e => e.Resource!.Id)
            .Concat(nextBundle.Entry.Select(e => e.Resource!.Id))
            .ToList();
        allIds.Should().Contain(data.SmithSnomedDiagnosticReport.Id);
        allIds.Should().Contain(data.TrumanSnomedDiagnosticReport.Id);
    }

    #endregion

    #region Pagination with Surrogate ID Tests

    /// <summary>
    /// Tests chained search with pagination using surrogate IDs to ensure consistent results.
    /// Ported from: GivenAChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        await CreateChainingTestDataAsync(tag);

        // Act - Get complete results first
        var query = $"subject:Patient._type=Patient&subject:Patient._tag={tag}";
        var completeBundle = await Harness.SearchBundleAsync("DiagnosticReport", query);
        completeBundle.Entry.Count.Should().BeGreaterThan(2);

        // Paginate through all results
        var paginatedBundle = await Harness.SearchBundleAsync("DiagnosticReport", query + "&_count=1");
        var allResources = new List<ResourceJsonNode>();
        allResources.AddRange(paginatedBundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!));

        var nextLink = paginatedBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        var iterations = 0;
        while (!string.IsNullOrEmpty(nextLink) && iterations < 20)
        {
            var nextBundle = await Harness.GetBundleAsync(nextLink);
            allResources.AddRange(nextBundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!));
            nextLink = nextBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
            iterations++;
        }

        // Assert - Paginated results should match complete results
        allResources.Should().HaveCount(completeBundle.Entry.Count);
        var completeIds = completeBundle.Entry.Select(e => e.Resource!.Id).OrderBy(x => x).ToList();
        var paginatedIds = allResources.Select(r => r.Id).OrderBy(x => x).ToList();
        paginatedIds.Should().BeEquivalentTo(completeIds);
    }

    /// <summary>
    /// Tests reverse chained search with pagination using surrogate IDs.
    /// Ported from: GivenAReverseChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAReverseChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        await CreateChainingTestDataAsync(tag);

        // Act - Get complete results first
        var query = $"_has:Observation:patient:_tag={tag}";
        var completeBundle = await Harness.SearchBundleAsync("Patient", query);
        completeBundle.Entry.Count.Should().BeGreaterThan(2);

        // Paginate through all results
        var paginatedBundle = await Harness.SearchBundleAsync("Patient", query + "&_count=1");
        var allResources = new List<ResourceJsonNode>();
        allResources.AddRange(paginatedBundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!));

        var nextLink = paginatedBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        var iterations = 0;
        while (!string.IsNullOrEmpty(nextLink) && iterations < 20)
        {
            var nextBundle = await Harness.GetBundleAsync(nextLink);
            allResources.AddRange(nextBundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!));
            nextLink = nextBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
            iterations++;
        }

        // Assert - Paginated results should match complete results
        allResources.Should().HaveCount(completeBundle.Entry.Count);
        var completeIds = completeBundle.Entry.Select(e => e.Resource!.Id).OrderBy(x => x).ToList();
        var paginatedIds = allResources.Select(r => r.Id).OrderBy(x => x).ToList();
        paginatedIds.Should().BeEquivalentTo(completeIds);
    }

    #endregion

    #region OrganizationAffiliation Tests (R4+ Only)

    /// <summary>
    /// Tests two chained search expressions with _include on OrganizationAffiliation.
    /// Ported from: GivenTwoChainedSearchExpressionsAndInclude_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// Note: OrganizationAffiliation is not available in STU3, test may be skipped if not supported.
    /// </summary>
    [Fact(Skip = "OrganizationAffiliation resource type support not yet implemented")]
    public async Task GivenTwoChainedSearchExpressionsAndInclude_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var data = await CreateChainingTestDataAsync(tag);

        // Create OrganizationAffiliation linking organization and location
        var faker = Harness.CreateFaker().WithTag(tag);
        var affiliation = faker.Generate("OrganizationAffiliation");
        affiliation.MutableNode["participatingOrganization"] = new JsonObject
        {
            ["reference"] = $"Organization/{data.Organization.Id}"
        };
        affiliation.MutableNode["location"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"Location/{data.Location.Id}" }
        };
        await Harness.CreateResourcesAsync([affiliation]);

        // Act - Two chained expressions + _include
        var query = $"participating-organization.identifier={data.OrganizationIdentifier}&_include=OrganizationAffiliation:location&participating-organization.type=practice";
        var bundle = await Harness.SearchBundleAsync("OrganizationAffiliation", query);

        // Assert - Should return 2 entries (affiliation + included location)
        bundle.Entry.Should().HaveCount(2);
    }

    #endregion

    #region Private Helper Methods

    private async Task DeleteResourceAsync(string resourceUrl)
    {
        var response = await Client.DeleteAsync($"/{resourceUrl}");
        response.EnsureSuccessStatusCode();
    }

    #endregion
}
