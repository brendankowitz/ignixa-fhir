// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.IncludeTests;

/// <summary>
/// E2E tests for basic FHIR _include functionality.
/// Tests single-level includes with various predicates and modifiers.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_BasicInclude : IncludeTestBase
{
    public IncludeSearchTests_BasicInclude(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests basic _include functionality with Location:organization reference.
    /// Ported from: GivenAnIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var location = CreateLocation(tag, createdOrg.Id);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Act
        var bundle = await Harness.SearchBundleAsync("Location", $"_include=Location:organization:Organization&_tag={tag}");

        // Assert
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation.Id);
        ValidateSearchEntryMode(bundle, "Location");

        // Verify included resources are not counted in total
        var countBundle = await Harness.SearchBundleAsync("Location", $"_include=Location:organization:Organization&_tag={tag}&_summary=count");
        countBundle.Total.Should().Be(1, "only match resources should be counted");

        // Verify _total=accurate also doesn't count included resources
        var accurateBundle = await Harness.SearchBundleAsync("Location", $"_include=Location:organization:Organization&_tag={tag}&_total=accurate");
        accurateBundle.Total.Should().Be(1, "only match resources should be counted with _total=accurate");
    }

    /// <summary>
    /// Tests that _id predicate is not applied to included resources.
    /// Ported from: GivenAnIncludeSearchExpressionWithAPredicateOnId_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithAPredicateOnId_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var location = CreateLocation(tag, createdOrg.Id);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Act - include with _id should still include the organization even though its ID doesn't match
        var bundle = await Harness.SearchBundleAsync("Location",
            $"_include=Location:organization:Organization&_tag={tag}&_id={createdLocation.Id}");

        // Assert - should contain both location and organization
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation.Id);

        // Verify included resources are not counted
        var countBundle = await Harness.SearchBundleAsync("Location",
            $"_include=Location:organization:Organization&_tag={tag}&_id={createdLocation.Id}&_summary=count");
        countBundle.Total.Should().Be(1);
    }

    /// <summary>
    /// Tests _include with POST _search.
    /// Ported from: GivenAnIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var location = CreateLocation(tag, createdOrg.Id);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Act - POST _search
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_include"] = "Location:organization:Organization",
            ["_tag"] = tag
        });
        var response = await Client.PostAsync("/Location/_search", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        // Assert
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation.Id);
        ValidateSearchEntryMode(bundle, "Location");
    }

    /// <summary>
    /// Tests _include with resource table predicates only.
    /// Ported from: GivenAnIncludeSearchExpressionWithOnlyResourceTablePredicates_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithOnlyResourceTablePredicates_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var patient = CreatePatientWithReferences(tag, "Adams");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        var group = CreateGroup(tag, createdPatient.Id);
        var createdGroup = await Harness.CreateResourceAsync(group);

        // Act - search by _lastUpdated and include member
        var bundle = await Harness.SearchBundleAsync("Group", $"_include=Group:member:Patient&_tag={tag}");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        matchEntries.Should().Contain(e => e.Resource != null && e.Resource.Id == createdGroup.Id);
        includeEntries.Should().Contain(e => e.Resource != null && e.Resource.Id == createdPatient.Id);
    }

    /// <summary>
    /// Tests _include with simple search parameter.
    /// Ported from: GivenAnIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("DiagnosticReport", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create two patients
        var smithPatient = CreatePatientWithReferences(tag, "Smith");
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");

        var createdPatients = await Harness.CreateResourcesAsync([smithPatient, trumanPatient]);
        var smithId = createdPatients[0].Id;
        var trumanId = createdPatients[1].Id;

        // Create observations
        var smithObs = CreateObservation(tag, smithId, snomedCode, snomedSystem);
        var trumanObs = CreateObservation(tag, trumanId, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourcesAsync([smithObs, trumanObs]);

        // Create diagnostic reports
        var smithReport = CreateDiagnosticReport(tag, smithId, snomedCode, snomedSystem, createdObs[0].Id);
        var trumanReport = CreateDiagnosticReport(tag, trumanId, snomedCode, snomedSystem, createdObs[1].Id);
        await Harness.CreateResourcesAsync([smithReport, trumanReport]);

        // Act
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport",
            $"_tag={tag}&_include=DiagnosticReport:patient:Patient&code={snomedCode}");

        // Assert - should include patients
        var resources = bundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!).ToList();
        resources.Should().Contain(r => r.ResourceType == "DiagnosticReport");
        resources.Should().Contain(r => r.ResourceType == "Patient");

        ValidateSearchEntryMode(bundle, "DiagnosticReport");
    }

    /// <summary>
    /// Tests _include with wildcard (*).
    /// Ported from: GivenAnIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("DiagnosticReport", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create patient
        var patient = CreatePatientWithReferences(tag, "Smith");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observation
        var obs = CreateObservation(tag, createdPatient.Id, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourceAsync(obs);

        // Create diagnostic report referencing both patient and observation
        var report = CreateDiagnosticReport(tag, createdPatient.Id, snomedCode, snomedSystem, createdObs.Id);
        var createdReport = await Harness.CreateResourceAsync(report);

        // Act - wildcard include should get all references
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport",
            $"_tag={tag}&_include=DiagnosticReport:*&code={snomedCode}");

        // Assert - should include both patient and observation
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("DiagnosticReport");
        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("Observation");
    }

    /// <summary>
    /// Tests _include with multiple include parameters.
    /// Ported from: GivenAnIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("DiagnosticReport", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create patient
        var patient = CreatePatientWithReferences(tag, "Smith");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observation
        var obs = CreateObservation(tag, createdPatient.Id, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourceAsync(obs);

        // Create diagnostic report
        var report = CreateDiagnosticReport(tag, createdPatient.Id, snomedCode, snomedSystem, createdObs.Id);
        await Harness.CreateResourceAsync(report);

        // Act - multiple includes
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport",
            $"_tag={tag}&_include=DiagnosticReport:patient:Patient&_include=DiagnosticReport:result:Observation&code={snomedCode}");

        // Assert
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("DiagnosticReport");
        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("Observation");
    }

    /// <summary>
    /// Tests _include with no target type specified (should include all matching reference types).
    /// Ported from: GivenAnIncludeSearchExpressionWithNoTargetType_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithNoTargetType_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organization and practitioner
        var organization = CreateOrganizationResource(tag, "Test Org");
        var practitioner = CreatePractitioner(tag, "TestDoc");
        var createdOrg = await Harness.CreateResourceAsync(organization);
        var createdPractitioner = await Harness.CreateResourceAsync(practitioner);

        // Create patient
        var patient = CreatePatientWithReferences(tag, "Adams");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observation with multiple performer types
        var obs = CreateObservation(tag, createdPatient.Id, "4548-4", "http://loinc.org",
            createdPractitioner.Id, createdOrg.Id);
        await Harness.CreateResourceAsync(obs);

        // Act - include performer without target type
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_include=Observation:performer");

        // Assert - should include both Practitioner and Organization
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("Practitioner");
        resourceTypes.Should().Contain("Organization");
    }

    /// <summary>
    /// Tests that _include does not include untyped references.
    /// Ported from: GivenAnIncludeSearchExpression_WhenSearched_DoesNotIncludeUntypedReferences
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpression_WhenSearched_DoesNotIncludeUntypedReferences()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatientWithReferences(tag, "Adams");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create observation with untyped reference
        var obs = CreateObservation(tag, createdPatient.Id, "4548-4", "http://loinc.org", untypedReferences: true);
        var createdObs = await Harness.CreateResourceAsync(obs);

        // Act - wildcard include
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_id={createdObs.Id}&_include=Observation:*");

        // Assert - should not include the patient since reference is untyped
        var resourceCount = bundle.Entry.Count(e => e.Resource is not null);
        resourceCount.Should().Be(1, "untyped references should not be included");
    }

    /// <summary>
    /// Tests that _include does not include deleted resources.
    /// Ported from: GivenAnIncludeSearchExpression_WhenSearched_DoesnotIncludeDeletedOrUpdatedResources
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpression_WhenSearched_DoesNotIncludeDeletedResources()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create two organizations
        var activeOrg = CreateOrganizationResource(tag, "Active Org");
        var deletedOrg = CreateOrganizationResource(tag, "Deleted Org");
        var createdActiveOrg = await Harness.CreateResourceAsync(activeOrg);
        var createdDeletedOrg = await Harness.CreateResourceAsync(deletedOrg);

        // Create patients referencing the organizations
        var patientWithActiveOrg = CreatePatientWithReferences(tag, "Active", managingOrganizationId: createdActiveOrg.Id);
        var patientWithDeletedOrg = CreatePatientWithReferences(tag, "Deleted", managingOrganizationId: createdDeletedOrg.Id);
        await Harness.CreateResourcesAsync([patientWithActiveOrg, patientWithDeletedOrg]);

        // Delete one organization
        var deleteResponse = await Client.DeleteAsync($"/Organization/{createdDeletedOrg.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_include=Patient:organization");

        // Assert - should include active org but not deleted org
        var includedOrgIds = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Organization" && e.Search?.Mode == "include")
            .Select(e => e.Resource!.Id)
            .ToList();

        includedOrgIds.Should().Contain(createdActiveOrg.Id);
        includedOrgIds.Should().NotContain(createdDeletedOrg.Id);
    }
}
