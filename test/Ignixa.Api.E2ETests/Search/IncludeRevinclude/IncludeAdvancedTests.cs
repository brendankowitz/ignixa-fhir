// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

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
    [Fact]
    public async Task GivenAnIncludeSearchWithSort_WhenSearched_ThenCorrectResultsAreReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patients
        var adamsPatient = CreatePatientWithReferences(tag, "Adams");
        var smithPatient = CreatePatientWithReferences(tag, "Smith");
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");
        var createdPatients = await Harness.CreateResourcesAsync([adamsPatient, smithPatient, trumanPatient]);

        // Create MedicationRequests for Adams and Smith only
        var adamsMedRequest = CreateMedicationRequest(tag, createdPatients[0].Id);
        var smithMedRequest = CreateMedicationRequest(tag, createdPatients[1].Id);
        var createdRequests = await Harness.CreateResourcesAsync([adamsMedRequest, smithMedRequest]);

        // Create MedicationDispenses with different whenPrepared dates (for sorting)
        // Adams: 2000-01-01, Smith: 1990-01-01, Truman: no whenPrepared
        var adamsMedDispense = CreateMedicationDispense(tag, createdPatients[0].Id, "2000-01-01", createdRequests[0].Id);
        var smithMedDispense = CreateMedicationDispense(tag, createdPatients[1].Id, "1990-01-01", createdRequests[1].Id);
        var trumanMedDispense = CreateMedicationDispense(tag, createdPatients[2].Id, null, null);
        await Harness.CreateResourcesAsync([adamsMedDispense, smithMedDispense, trumanMedDispense]);

        // Act - search with _include and _sort descending by whenPrepared
        var bundle = await Harness.SearchBundleAsync("MedicationDispense",
            $"_include=MedicationDispense:prescription&_sort=-whenPrepared&_count=3&_tag={tag}");

        // Assert - verify bundle link
        bundle.Link.Should().Contain(l => l.Relation == "self");

        // Verify all expected resources are present
        ValidateBundleContains(bundle,
            adamsMedDispense.Id,
            smithMedDispense.Id,
            trumanMedDispense.Id,
            adamsMedRequest.Id,
            smithMedRequest.Id);

        // Verify search modes
        var matches = bundle.Entry.Where(e => e.Search?.Mode == "match").Select(e => e.Resource!.ResourceType).ToList();
        matches.Should().AllBe("MedicationDispense");

        var includes = bundle.Entry.Where(e => e.Search?.Mode == "include").Select(e => e.Resource!.ResourceType).ToList();
        includes.Should().AllBe("MedicationRequest");

        // Verify sorting - matches should be sorted descending by whenPrepared
        var matchedDispenses = bundle.Entry
            .Where(e => e.Search?.Mode == "match")
            .Select(e => e.Resource)
            .ToList();

        matchedDispenses.Should().HaveCount(3);
        // Adams (2000-01-01) should be first, Smith (1990-01-01) second, Truman (null) last
        matchedDispenses[0]!.Id.Should().Be(adamsMedDispense.Id);
        matchedDispenses[1]!.Id.Should().Be(smithMedDispense.Id);
        matchedDispenses[2]!.Id.Should().Be(trumanMedDispense.Id);
    }

    /// <summary>
    /// Tests _revinclude:iterate with _sort parameter (ascending).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearchedAndSorted_TheIterativeResultsShouldBeAddedToTheBundleAsc
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeIterateSearchExpressionWithSort_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundleAsc()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patients with different birth dates (for sorting)
        // Oldest: Pati (1950-01-01), Adams (1960-01-01), Smith (1970-01-01), Truman (1980-01-01), Newest: PatientWithDeletedOrg (1990-01-01)
        var patiPatient = CreatePatientWithReferences(tag, "Pati", birthDate: "1950-01-01");
        var adamsPatient = CreatePatientWithReferences(tag, "Adams", birthDate: "1960-01-01");
        var smithPatient = CreatePatientWithReferences(tag, "Smith", birthDate: "1970-01-01");
        var trumanPatient = CreatePatientWithReferences(tag, "Truman", birthDate: "1980-01-01");
        var deletedOrgPatient = CreatePatientWithReferences(tag, "DeletedOrg", birthDate: "1990-01-01");
        var createdPatients = await Harness.CreateResourcesAsync([patiPatient, adamsPatient, smithPatient, trumanPatient, deletedOrgPatient]);

        // Create MedicationRequests for Adams and Smith
        var adamsMedRequest = CreateMedicationRequest(tag, createdPatients[1].Id);
        var smithMedRequest = CreateMedicationRequest(tag, createdPatients[2].Id);
        var createdRequests = await Harness.CreateResourcesAsync([adamsMedRequest, smithMedRequest]);

        // Create MedicationDispenses referencing the requests
        var adamsMedDispense = CreateMedicationDispense(tag, createdPatients[1].Id, "2000-01-01", createdRequests[0].Id);
        var smithMedDispense = CreateMedicationDispense(tag, createdPatients[2].Id, "1990-01-01", createdRequests[1].Id);
        await Harness.CreateResourcesAsync([adamsMedDispense, smithMedDispense]);

        // Act - search Patient with _revinclude:iterate and ascending sort by birthdate
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={tag}&_sort=birthdate");

        // Assert - verify all expected resources are present
        ValidateBundleContains(bundle,
            patiPatient.Id,
            adamsPatient.Id,
            smithPatient.Id,
            trumanPatient.Id,
            deletedOrgPatient.Id,
            adamsMedRequest.Id,
            smithMedRequest.Id,
            adamsMedDispense.Id,
            smithMedDispense.Id);

        // Verify search modes
        var matches = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        matches.Should().AllSatisfy(e => e.Resource!.ResourceType.Should().Be("Patient"));

        var includes = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includes.Should().NotBeEmpty();

        // Verify sorting - patient matches should be sorted ascending by birthdate
        var matchedPatients = matches.Select(e => e.Resource).ToList();

        matchedPatients.Should().HaveCount(5);
        // Should be in ascending order by birthdate
        matchedPatients[0]!.Id.Should().Be(patiPatient.Id); // 1950-01-01
        matchedPatients[1]!.Id.Should().Be(adamsPatient.Id); // 1960-01-01
        matchedPatients[2]!.Id.Should().Be(smithPatient.Id); // 1970-01-01
        matchedPatients[3]!.Id.Should().Be(trumanPatient.Id); // 1980-01-01
        matchedPatients[4]!.Id.Should().Be(deletedOrgPatient.Id); // 1990-01-01

        // Verify total count excludes included resources
        var countBundle = await Harness.SearchBundleAsync("Patient",
            $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={tag}&_sort=birthdate&_summary=count");
        countBundle.Total.Should().Be(5);
    }

    #endregion

    #region Wildcard Source Tests

    /// <summary>
    /// Tests _revinclude with wildcard source (*:*).
    /// This validates that Organization?_revinclude=*:* returns the Organization(s) (match)
    /// plus ALL resources of ANY type that reference those Organizations (includes).
    /// Ported from: GivenARevIncludeSearchWildcardSourceExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchWildcardSourceExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - create multiple Organizations and various resources that reference them
        var tag = Guid.NewGuid().ToString();

        // Create Organizations (these will be the main results)
        var org1 = CreateOrganizationResource(tag, "Organization One");
        var org2 = CreateOrganizationResource(tag, "Organization Two");
        var createdOrgs = await Harness.CreateResourcesAsync([org1, org2]);

        // Create a Location referencing org1 (Location.managingOrganization)
        var location = CreateLocation(tag, createdOrgs[0].Id);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Create Patients referencing both organizations (Patient.managingOrganization)
        var patient1 = CreatePatientWithReferences(tag, "Patient1", managingOrganizationId: createdOrgs[0].Id);
        var patient2 = CreatePatientWithReferences(tag, "Patient2", managingOrganizationId: createdOrgs[1].Id);
        var createdPatients = await Harness.CreateResourcesAsync([patient1, patient2]);

        // Create Observations referencing the organizations via performer (Observation.performer)
        var obs1 = CreateObservation(tag, createdPatients[0].Id, "429858000", "http://snomed.info/sct", organizationId: createdOrgs[0].Id);
        var obs2 = CreateObservation(tag, createdPatients[1].Id, "429858000", "http://snomed.info/sct", organizationId: createdOrgs[1].Id);
        var createdObs = await Harness.CreateResourcesAsync([obs1, obs2]);

        // Create a CareTeam referencing the organization (CareTeam.participant.member)
        var careTeam = CreateCareTeam(tag, [createdPatients[0].Id], organizationId: createdOrgs[0].Id);
        var createdCareTeam = await Harness.CreateResourceAsync(careTeam);

        // Act - search Organizations with wildcard source revinclude
        var bundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=*:*&_tag={tag}");

        // Assert - bundle should contain Organizations (matches) plus all referencing resources (includes)
        var resourceTypeToIds = bundle.Entry
            .Where(e => e.Resource is not null)
            .GroupBy(e => e.Resource!.ResourceType)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Resource!.Id).ToList());

        // Verify Organizations (main results) are present
        resourceTypeToIds.Should().ContainKey("Organization");
        resourceTypeToIds["Organization"].Should().Contain(createdOrgs[0].Id);
        resourceTypeToIds["Organization"].Should().Contain(createdOrgs[1].Id);

        // Verify Location (references org1) is included
        resourceTypeToIds.Should().ContainKey("Location");
        resourceTypeToIds["Location"].Should().Contain(createdLocation.Id);

        // Verify Patients (reference orgs) are included
        resourceTypeToIds.Should().ContainKey("Patient");
        resourceTypeToIds["Patient"].Should().Contain(createdPatients[0].Id);
        resourceTypeToIds["Patient"].Should().Contain(createdPatients[1].Id);

        // Verify Observations (reference orgs via performer) are included
        resourceTypeToIds.Should().ContainKey("Observation");
        resourceTypeToIds["Observation"].Should().Contain(createdObs[0].Id);
        resourceTypeToIds["Observation"].Should().Contain(createdObs[1].Id);

        // Verify CareTeam (references org1 via participant.member) is included
        resourceTypeToIds.Should().ContainKey("CareTeam");
        resourceTypeToIds["CareTeam"].Should().Contain(createdCareTeam.Id);

        // Verify search modes
        var matches = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        matches.Should().HaveCount(2); // 2 Organizations
        matches.Should().AllSatisfy(e => e.Resource!.ResourceType.Should().Be("Organization"));

        var includes = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();
        includes.Should().HaveCount(6); // 1 Location + 2 Patients + 2 Observations + 1 CareTeam

        // Verify total count excludes includes (only counts main results)
        var countBundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=*:*&_tag={tag}&_summary=count");
        countBundle.Total.Should().Be(2); // Only the 2 Organizations
    }

    #endregion
}
