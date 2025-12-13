// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

/// <summary>
/// E2E tests for basic FHIR _revinclude functionality.
/// Tests reverse includes that find resources pointing back to the search results.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_BasicRevinclude : IncludeTestBase
{
    public IncludeSearchTests_BasicRevinclude(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests basic _revinclude functionality.
    /// Ported from: GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var location = CreateLocation(tag, createdOrg.Id);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Act - reverse include: get all organizations and include locations that reference them
        var bundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=Location:organization&_tag={tag}");

        // Assert
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation.Id);
        ValidateSearchEntryMode(bundle, "Organization");

        // Verify included resources are not counted
        var countBundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=Location:organization&_tag={tag}&_summary=count");
        countBundle.Total.Should().Be(1, "only match resources should be counted");

        // Verify _total=accurate also doesn't count included resources
        var accurateBundle = await Harness.SearchBundleAsync("Organization",
            $"_revinclude=Location:organization&_tag={tag}&_total=accurate");
        accurateBundle.Total.Should().Be(1);
    }

    /// <summary>
    /// Tests _revinclude with POST _search.
    /// Ported from: GivenARevIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
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
            ["_revinclude"] = "Location:organization",
            ["_tag"] = tag
        });
        var response = await Client.PostAsync("/Organization/_search", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        // Assert
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation.Id);
        ValidateSearchEntryMode(bundle, "Organization");
    }

    /// <summary>
    /// Tests _revinclude with simple search.
    /// Ported from: GivenARevIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create patients
        var smithPatient = CreatePatientWithReferences(tag, "Smith");
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");
        var createdPatients = await Harness.CreateResourcesAsync([smithPatient, trumanPatient]);

        // Create observations with specific code
        var smithObs = CreateObservation(tag, createdPatients[0].Id, snomedCode, snomedSystem);
        var trumanObs = CreateObservation(tag, createdPatients[1].Id, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourcesAsync([smithObs, trumanObs]);

        // Create diagnostic reports
        var smithReport = CreateDiagnosticReport(tag, createdPatients[0].Id, snomedCode, snomedSystem, createdObs[0].Id);
        var trumanReport = CreateDiagnosticReport(tag, createdPatients[1].Id, snomedCode, snomedSystem, createdObs[1].Id);
        await Harness.CreateResourcesAsync([smithReport, trumanReport]);

        // Act - revinclude DiagnosticReport:result when searching Observations
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:result&code={snomedCode}");

        // Assert - should include diagnostic reports that reference these observations
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("DiagnosticReport");
    }

    /// <summary>
    /// Tests _revinclude with wildcard (*).
    /// Ported from: GivenARevIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

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

        // Create diagnostic report referencing the observation
        var report = CreateDiagnosticReport(tag, createdPatient.Id, snomedCode, snomedSystem, createdObs.Id);
        await Harness.CreateResourceAsync(report);

        // Act - wildcard revinclude
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:*&code={snomedCode}");

        // Assert
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("DiagnosticReport");
    }

    /// <summary>
    /// Tests _revinclude returns correct results and nothing else.
    /// Ported from: GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturnedAndNothingElse
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturnedAndNothingElse()
    {
        // Capability check
        RequireSearchParameters("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var loincCode = "4548-4";
        var loincSystem = "http://loinc.org";

        // Create patients
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");
        var smithPatient = CreatePatientWithReferences(tag, "Smith");
        var createdPatients = await Harness.CreateResourcesAsync([trumanPatient, smithPatient]);
        var trumanId = createdPatients[0].Id;
        var smithId = createdPatients[1].Id;

        // Create observations for both patients
        var trumanObs = CreateObservation(tag, trumanId, loincCode, loincSystem);
        var smithObs = CreateObservation(tag, smithId, loincCode, loincSystem);
        await Harness.CreateResourcesAsync([trumanObs, smithObs]);

        // Act - search for Truman patient with revinclude
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_revinclude=Observation:patient&family=Truman");

        // Assert - should only have Truman patient and their observations
        var resources = bundle.Entry.Where(e => e.Resource is not null).Select(e => e.Resource!).ToList();

        var patients = resources.Where(r => r.ResourceType == "Patient").ToList();
        patients.Should().HaveCount(1);
        patients[0].Id.Should().Be(trumanId);

        var observations = resources.Where(r => r.ResourceType == "Observation").ToList();
        observations.Should().AllSatisfy(obs =>
        {
            var subjectRef = obs.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Contain(trumanId);
        });
    }

    /// <summary>
    /// Tests _revinclude with multiple includes.
    /// Ported from: GivenARevIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameters("Patient", "family");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";
        var loincCode = "4548-4";
        var loincSystem = "http://loinc.org";

        // Create patient
        var trumanPatient = CreatePatientWithReferences(tag, "Truman");
        var createdPatient = await Harness.CreateResourceAsync(trumanPatient);

        // Create observations
        var snomedObs = CreateObservation(tag, createdPatient.Id, snomedCode, snomedSystem);
        var loincObs = CreateObservation(tag, createdPatient.Id, loincCode, loincSystem);
        var createdObs = await Harness.CreateResourcesAsync([snomedObs, loincObs]);

        // Create diagnostic reports
        var snomedReport = CreateDiagnosticReport(tag, createdPatient.Id, snomedCode, snomedSystem, createdObs[0].Id);
        var loincReport = CreateDiagnosticReport(tag, createdPatient.Id, loincCode, loincSystem, createdObs[1].Id);
        await Harness.CreateResourcesAsync([snomedReport, loincReport]);

        // Act - multiple revinclude parameters
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_revinclude=DiagnosticReport:patient&_revinclude=Observation:patient&family=Truman");

        // Assert
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("DiagnosticReport");
        resourceTypes.Should().Contain("Observation");
    }

    /// <summary>
    /// Tests _revinclude does not include deleted resources.
    /// Ported from: GivenAnRevIncludeSearchExpression_WhenSearched_DoesnotIncludeDeletedResources
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpression_WhenSearched_DoesNotIncludeDeletedResources()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient
        var patient = CreatePatientWithReferences(tag, "TestPatient");
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create two observations referencing the patient
        var obs1 = CreateObservation(tag, createdPatient.Id, "4548-4", "http://loinc.org");
        var obs2 = CreateObservation(tag, createdPatient.Id, "4548-4", "http://loinc.org");
        var createdObs = await Harness.CreateResourcesAsync([obs1, obs2]);

        // Delete one observation
        var deleteResponse = await Client.DeleteAsync($"/Observation/{createdObs[1].Id}");
        deleteResponse.EnsureSuccessStatusCode();

        // Act
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_revinclude=Observation:patient");

        // Assert - should not include deleted observation
        var observationIds = bundle.Entry
            .Where(e => e.Resource?.ResourceType == "Observation")
            .Select(e => e.Resource!.Id)
            .ToList();

        observationIds.Should().Contain(createdObs[0].Id);
        observationIds.Should().NotContain(createdObs[1].Id);
    }

    /// <summary>
    /// Tests _revinclude when no references exist.
    /// Ported from: GivenARevIncludeSearchExpressionWithNoReferences_WhenSearched_ThenCorrectBundleWithOnlyMatchesShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithNoReferences_WhenSearched_ThenCorrectBundleWithOnlyMatchesShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patients without any appointments referencing them
        var patients = new[]
        {
            CreatePatientWithReferences(tag, "Patient1"),
            CreatePatientWithReferences(tag, "Patient2")
        };
        await Harness.CreateResourcesAsync(patients);

        // Act - revinclude Appointment:actor but no appointments exist
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_tag={tag}&_revinclude=Appointment:actor");

        // Assert - should only have patients, no includes
        bundle.Entry.Should().HaveCount(2);
        bundle.Entry.Should().AllSatisfy(e => e.Resource?.ResourceType.Should().Be("Patient"));
        bundle.Entry.Should().AllSatisfy(e => e.Search?.Mode.Should().Be("match"));
    }
}
