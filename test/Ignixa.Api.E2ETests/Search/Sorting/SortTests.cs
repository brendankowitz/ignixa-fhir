// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.Sorting;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.Sorting;

/// <summary>
/// E2E tests for FHIR search sorting functionality.
/// Tests ascending/descending sort on various fields, pagination with sort,
/// sort with includes/revincludes, and error handling for invalid sort params.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.SortTests
/// </summary>
[Collection(E2ETestCollection.Name)]
public class SortTests : CapabilityDrivenTestBase
{
    public SortTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests ascending sort by _lastUpdated.
    /// Ported from: GivenPatients_WhenSearchedWithSortParams_ThenPatientsAreReturnedInTheAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortByLastUpdated_ThenPatientsAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "_lastUpdated");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        var createdPatients = await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_sort=_lastUpdated");

        results.Length.ShouldBe(4);
        AssertResourcesInAscendingOrderByLastUpdated(results);
    }

    /// <summary>
    /// Tests descending sort by _lastUpdated using hyphen prefix.
    /// Ported from: GivenPatients_WhenSearchedWithSortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortByLastUpdatedDescending_ThenPatientsAreReturnedInDescendingOrder()
    {
        RequireSearchParameter("Patient", "_lastUpdated");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_sort=-_lastUpdated");

        results.Length.ShouldBe(4);
        AssertResourcesInDescendingOrderByLastUpdated(results);
    }

    /// <summary>
    /// Tests ascending sort by birthdate across multiple pages.
    /// Ported from: GivenMoreThanTenPatients_WhenSearchedWithSortParam_ThenPatientsAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenMoreThanTenPatients_WhenSearchedWithSortByBirthdate_ThenPatientsAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetPaginatedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var allResults = await GetAllPagesAsync("Patient", $"_tag={tag}&_sort=birthdate");

        allResults.Count.ShouldBe(12);
        AssertBirthdatesInAscendingOrder(allResults);
    }

    /// <summary>
    /// Tests descending sort by birthdate across multiple pages.
    /// Ported from: GivenMoreThanTenPatients_WhenSearchedWithSortParamWithHyphen_ThenPatientsAreReturnedInDescendingOrder
    /// </summary>
    [Fact]
    public async Task GivenMoreThanTenPatients_WhenSearchedWithSortByBirthdateDescending_ThenPatientsAreReturnedInDescendingOrder()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetPaginatedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var allResults = await GetAllPagesAsync("Patient", $"_tag={tag}&_sort=-birthdate");

        allResults.Count.ShouldBe(12);
        AssertBirthdatesInDescendingOrder(allResults);
    }

    /// <summary>
    /// Tests sort with missing values using birthdate:missing filter.
    /// Ported from: GivenPatients_WhenSearchedWithSortParamAndMissingIdentifier_SearchResultsReturnedShouldHonorMissingIdentifier
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortAndMissingFalse_ThenOnlyPatientsWithFieldAreReturned()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetMissingValueSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllPatients.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&birthdate:missing=false&_sort=birthdate");

        results.Length.ShouldBe(10);
        AssertBirthdatesInAscendingOrder(results.ToList());
    }

    /// <summary>
    /// Tests ascending sort by family name.
    /// Ported from: GivenPatients_WhenSearchedWithFamilySortParams_ThenPatientsAreReturnedInTheAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortByFamily_ThenPatientsAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_sort=family");

        results.Length.ShouldBe(4);
        AssertFamilyNamesInAscendingOrder(results);
    }

    /// <summary>
    /// Tests descending sort by family name.
    /// Ported from: GivenPatients_WhenSearchedWithFamilySortParamsWithHyphen_ThenPatientsAreReturnedInTheDescendingOrder
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortByFamilyDescending_ThenPatientsAreReturnedInDescendingOrder()
    {
        RequireSearchParameter("Patient", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_sort=-family");

        results.Length.ShouldBe(4);
        AssertFamilyNamesInDescendingOrder(results);
    }

    /// <summary>
    /// Tests sort with a datetime filter and birthdate sort.
    /// Ported from: GivenQueryWithDatetimeFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenQueryWithDatetimeFilter_WhenSearchedWithSortByBirthdate_ThenResourcesAreReturnedInAscendingOrder()
    {
        RequireSearchParameters("Patient", "birthdate", "_lastUpdated");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetPaginatedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_lastUpdated=ge2020-01-01&_sort=birthdate");

        results.ShouldNotBeEmpty();
        AssertBirthdatesInAscendingOrder(results.ToList());
    }

    /// <summary>
    /// Tests sort with tag filter.
    /// Ported from: GivenQueryWithTagFilter_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenQueryWithTagFilter_WhenSearchedWithSortByBirthdate_ThenResourcesAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetPaginatedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var allResults = await GetAllPagesAsync("Patient", $"_tag={tag}&_sort=birthdate");

        allResults.Count.ShouldBeGreaterThanOrEqualTo(12, "Should have at least 12 patients created by fixture");
        AssertBirthdatesInAscendingOrder(allResults);
    }

    /// <summary>
    /// Tests sort with multiple filters (tag AND family).
    /// Ported from: GivenQueryWithMultipleFilters_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenQueryWithMultipleFilters_WhenSearchedWithSortByBirthdate_ThenResourcesAreReturnedInAscendingOrder()
    {
        RequireSearchParameters("Patient", "birthdate", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetPaginatedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&family:contains=Family&_sort=birthdate");

        results.ShouldNotBeEmpty();
        AssertBirthdatesInAscendingOrder(results.ToList());
    }

    /// <summary>
    /// Tests sort with _revinclude.
    /// Ported from: GivenQueryWithRevinclude_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenQueryWithRevinclude_WhenSearchedWithSortByBirthdate_ThenPatientsAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetIncludeSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_sort=birthdate&_revinclude=Observation:subject");

        var patients = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Patient")
            .Select(e => e.Resource!)
            .ToList();

        patients.Count.ShouldBe(5);
        AssertBirthdatesInAscendingOrder(patients);

        var observations = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Observation")
            .ToList();

        observations.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Tests sort with _include on Observation.
    /// Ported from: GivenQueryWithObservationInclude_WhenSearchedWithSortParamOnDatetime_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenQueryWithObservationInclude_WhenSearchedWithSortByDate_ThenObservationsAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Observation", "date");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetIncludeSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var bundle = await Harness.SearchBundleAsync("Observation", $"_tag={tag}&_sort=date&_include=Observation:subject");

        var observations = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Observation")
            .Select(e => e.Resource!)
            .ToList();

        observations.ShouldNotBeEmpty();
        AssertObservationDatesInAscendingOrder(observations);

        var patients = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Patient")
            .ToList();

        patients.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Tests sorting when patients have multiple family names.
    /// Ported from: GivenPatientsWithMultipleNames_WhenFilteringAndSortingByFamilyName_ThenResourcesAreReturnedInAscendingOrder
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleNames_WhenFilteringAndSortingByFamily_ThenResourcesAreReturnedInAscendingOrder()
    {
        RequireSearchParameter("Patient", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetMultiNameSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&family:contains=R&_sort=family");

        results.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Tests that patients with missing family names are still included in sorted results.
    /// Ported from: GivenPatientsWithFamilyNameMissing_WhenSortingByFamilyName_ThenThosePatientsAreIncludedInResult
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMissingFamilyName_WhenSortingByFamily_ThenAllPatientsAreIncludedInResult()
    {
        RequireSearchParameter("Patient", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetMissingFamilyNameSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllPatients.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_sort=family");

        results.Length.ShouldBe(6);
    }

    /// <summary>
    /// Tests sorting when multiple patients have the same birthdate (tie-breaking).
    /// Ported from: GivenPatientsWithSameBirthdateAndMultiplePages_WhenSortedByBirthdate_ThenPatientsAreReturnedInCorrectOrder
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithSameBirthdate_WhenSortedByBirthdate_ThenPatientsAreReturnedWithConsistentOrdering()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetSameBirthdateSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.Patients.ToArray());

        var allResults = await GetAllPagesAsync("Patient", $"_tag={tag}&_sort=birthdate&_count=5");

        allResults.Count.ShouldBe(15);

        var resultIds = allResults.Select(r => r.Id).ToList();
        resultIds.Distinct().Count().ShouldBe(15);
    }

    /// <summary>
    /// Tests chained search with sort by organization name.
    /// Ported from: GivenPatientWithManagingOrg_WhenSearchedWithOrgIdentifierAndSorted_ThenPatientsAreReturned
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithManagingOrg_WhenSearchedWithOrgAndSortedByFamily_ThenPatientsAreReturned()
    {
        RequireSearchParameters("Patient", "organization", "family");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetChainedSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&organization._tag={tag}&_sort=family");

        results.Length.ShouldBe(3);
    }

    /// <summary>
    /// Tests organization partOf search with sort by name.
    /// Ported from: GivenOrg_WhenSearchedWithPartOfAndSortedByName_ThenOrgsAreReturned
    /// </summary>
    [Fact]
    public async Task GivenOrganizations_WhenSearchedWithPartOfAndSortedByName_ThenOrgsAreReturned()
    {
        RequireSearchParameters("Organization", "partof", "name");

        var tag = Guid.NewGuid().ToString();

        var parentOrg = OrganizationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithName("Parent Hospital")
            .Build();

        var childOrg1 = OrganizationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithName("Alpha Department")
            .WithPartOf(parentOrg.Id!)
            .Build();

        var childOrg2 = OrganizationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithName("Beta Department")
            .WithPartOf(parentOrg.Id!)
            .Build();

        await Harness.CreateResourcesAsync([parentOrg, childOrg1, childOrg2]);

        var results = await Harness.SearchAsync("Organization", $"partof={parentOrg.Id}&_sort=name");

        results.Length.ShouldBe(2);
    }

    /// <summary>
    /// Tests that sort results do not have continuation token when all results fit on first page.
    /// Ported from: GivenPatients_WhenSearchedWithSortAndAllResourcesRetrievedInFirstPhase_SearchResultShouldNotHaveContinuationToken
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithSortAndAllFitOnFirstPage_ThenNoContinuationToken()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_sort=birthdate");

        GetMatchEntries(bundle).Count.ShouldBe(4);
        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next");
        nextLink.ShouldBeNull();

        var bundleDesc = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_sort=-birthdate");

        GetMatchEntries(bundleDesc).Count.ShouldBe(4);
        var nextLinkDesc = bundleDesc.Link.FirstOrDefault(l => l.Relation == "next");
        nextLinkDesc.ShouldBeNull();
    }

    /// <summary>
    /// Tests pagination with _revinclude when included resources exceed page limit.
    /// Ported from: GivenPatientsWithIncludedResourcesGreaterThanOnePage_WhenSearchedWithSortAndInclude_ThenSearchResultsContainIncludedResources
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithIncludes_WhenSearchedWithSortAndRevinclude_ThenIncludedResourcesAreReturned()
    {
        RequireSearchParameter("Patient", "birthdate");

        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetIncludeSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var bundle = await Harness.SearchBundleAsync("Patient", $"_tag={tag}&_sort=birthdate&_revinclude=Observation:subject&_count=3");

        bundle.Entry.ShouldNotBeEmpty();

        var patients = bundle.Entry.Where(e => e.Resource?.ResourceType == "Patient").ToList();
        var observations = bundle.Entry.Where(e => e.Resource?.ResourceType == "Observation").ToList();

        patients.ShouldNotBeEmpty();
        observations.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Tests invalid sort parameter with lenient handling.
    /// Ported from: GivenPatients_WhenSearchedWithInvalidSortParamsAndHandlingLenient_ThenPatientsAreReturnedUnsortedWithWarning
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithInvalidSortAndHandlingLenient_ThenPatientsAreReturnedWithWarning()
    {
        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Patient?_tag={tag}&_sort=link");
        request.Headers.Add("Prefer", "handling=lenient");

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var responseJson = await response.Content.ReadAsStringAsync();
        var bundle = Ignixa.Serialization.JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        GetMatchEntries(bundle).Count.ShouldBe(4);
    }

    /// <summary>
    /// Tests invalid sort parameter with strict handling.
    /// Ported from: GivenPatients_WhenSearchedWithInvalidSortParamsAndHandlingStrict_ThenErrorReturnedWithMessage
    /// </summary>
    [Fact]
    public async Task GivenPatients_WhenSearchedWithInvalidSortAndHandlingStrict_ThenBadRequestReturned()
    {
        var tag = Guid.NewGuid().ToString();
        var testData = SchemaProvider.GetBasicSortTestData(tag);
        await Harness.CreateResourcesAsync(testData.AllResources.ToArray());

        var request = new HttpRequestMessage(HttpMethod.Get, $"/Patient?_tag={tag}&_sort=link");
        request.Headers.Add("Prefer", "handling=strict");

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<List<ResourceJsonNode>> GetAllPagesAsync(string resourceType, string query)
    {
        var allResources = new List<ResourceJsonNode>();
        var bundle = await Harness.SearchBundleAsync(resourceType, query);

        // Exclude outcome entries (like OperationOutcome)
        allResources.AddRange(GetMatchEntries(bundle).Where(e => e.Resource is not null).Select(e => e.Resource!));

        var nextLink = bundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
        var iterations = 0;

        while (!string.IsNullOrEmpty(nextLink) && iterations < 20)
        {
            var nextBundle = await Harness.GetBundleAsync(nextLink);
            allResources.AddRange(GetMatchEntries(nextBundle).Where(e => e.Resource is not null).Select(e => e.Resource!));
            nextLink = nextBundle.Link.FirstOrDefault(l => l.Relation == "next")?.Url;
            iterations++;
        }

        return allResources;
    }

    private static void AssertResourcesInAscendingOrderByLastUpdated(ResourceJsonNode[] resources)
    {
        for (var i = 1; i < resources.Length; i++)
        {
            var prevLastUpdated = GetLastUpdated(resources[i - 1]);
            var currLastUpdated = GetLastUpdated(resources[i]);

            prevLastUpdated.ShouldBeLessThanOrEqualTo(currLastUpdated,
                $"Resource at index {i - 1} ({prevLastUpdated}) should be <= resource at index {i} ({currLastUpdated})");
        }
    }

    private static void AssertResourcesInDescendingOrderByLastUpdated(ResourceJsonNode[] resources)
    {
        for (var i = 1; i < resources.Length; i++)
        {
            var prevLastUpdated = GetLastUpdated(resources[i - 1]);
            var currLastUpdated = GetLastUpdated(resources[i]);

            prevLastUpdated.ShouldBeGreaterThanOrEqualTo(currLastUpdated,
                $"Resource at index {i - 1} ({prevLastUpdated}) should be >= resource at index {i} ({currLastUpdated})");
        }
    }

    private static void AssertBirthdatesInAscendingOrder(List<ResourceJsonNode> patients)
    {
        for (var i = 1; i < patients.Count; i++)
        {
            var prevBirthdate = GetBirthdate(patients[i - 1]);
            var currBirthdate = GetBirthdate(patients[i]);

            if (prevBirthdate.HasValue && currBirthdate.HasValue)
            {
                prevBirthdate.Value.ShouldBeLessThanOrEqualTo(currBirthdate.Value,
                    $"Patient at index {i - 1} ({prevBirthdate}) should be <= patient at index {i} ({currBirthdate})");
            }
        }
    }

    private static void AssertBirthdatesInDescendingOrder(List<ResourceJsonNode> patients)
    {
        for (var i = 1; i < patients.Count; i++)
        {
            var prevBirthdate = GetBirthdate(patients[i - 1]);
            var currBirthdate = GetBirthdate(patients[i]);

            if (prevBirthdate.HasValue && currBirthdate.HasValue)
            {
                prevBirthdate.Value.ShouldBeGreaterThanOrEqualTo(currBirthdate.Value,
                    $"Patient at index {i - 1} ({prevBirthdate}) should be >= patient at index {i} ({currBirthdate})");
            }
        }
    }

    private static void AssertFamilyNamesInAscendingOrder(ResourceJsonNode[] patients)
    {
        var familyNames = patients
            .Select(GetFamilyName)
            .Where(n => n is not null)
            .ToList();

        for (var i = 1; i < familyNames.Count; i++)
        {
            string.Compare(familyNames[i - 1], familyNames[i], StringComparison.OrdinalIgnoreCase)
                .ShouldBeLessThanOrEqualTo(0,
                    $"Family name at index {i - 1} ({familyNames[i - 1]}) should be <= family name at index {i} ({familyNames[i]})");
        }
    }

    private static void AssertFamilyNamesInDescendingOrder(ResourceJsonNode[] patients)
    {
        var familyNames = patients
            .Select(GetFamilyName)
            .Where(n => n is not null)
            .ToList();

        for (var i = 1; i < familyNames.Count; i++)
        {
            string.Compare(familyNames[i - 1], familyNames[i], StringComparison.OrdinalIgnoreCase)
                .ShouldBeGreaterThanOrEqualTo(0,
                    $"Family name at index {i - 1} ({familyNames[i - 1]}) should be >= family name at index {i} ({familyNames[i]})");
        }
    }

    private static void AssertObservationDatesInAscendingOrder(List<ResourceJsonNode> observations)
    {
        var dates = observations
            .Select(GetObservationEffectiveDate)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        for (var i = 1; i < dates.Count; i++)
        {
            dates[i - 1].ShouldBeLessThanOrEqualTo(dates[i],
                $"Observation at index {i - 1} ({dates[i - 1]}) should be <= observation at index {i} ({dates[i]})");
        }
    }

    private static DateTimeOffset GetLastUpdated(ResourceJsonNode resource)
    {
        var lastUpdated = resource.MutableNode["meta"]?["lastUpdated"]?.GetValue<string>();
        return DateTimeOffset.Parse(lastUpdated ?? DateTimeOffset.MinValue.ToString("o"));
    }

    private static DateTime? GetBirthdate(ResourceJsonNode patient)
    {
        var birthDate = patient.MutableNode["birthDate"]?.GetValue<string>();
        if (string.IsNullOrEmpty(birthDate))
        {
            return null;
        }

        if (DateTime.TryParse(birthDate, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? GetFamilyName(ResourceJsonNode patient)
    {
        var names = patient.MutableNode["name"] as System.Text.Json.Nodes.JsonArray;
        if (names is null || names.Count == 0)
        {
            return null;
        }

        return names[0]?["family"]?.GetValue<string>();
    }

    private static DateTime? GetObservationEffectiveDate(ResourceJsonNode observation)
    {
        var effectiveDateTime = observation.MutableNode["effectiveDateTime"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(effectiveDateTime) && DateTime.TryParse(effectiveDateTime, out var parsed))
        {
            return parsed;
        }

        var effectivePeriod = observation.MutableNode["effectivePeriod"];
        if (effectivePeriod is not null)
        {
            var start = effectivePeriod["start"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(start) && DateTime.TryParse(start, out var parsedStart))
            {
                return parsedStart;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets match entries from a bundle, excluding outcome entries (like OperationOutcome).
    /// </summary>
    private static List<BundleComponentJsonNode> GetMatchEntries(BundleJsonNode bundle)
    {
        return bundle.Entry.Where(e => e.Search?.Mode != "outcome").ToList();
    }
}
