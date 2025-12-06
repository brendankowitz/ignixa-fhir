// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;

namespace Ignixa.Api.E2ETests.IncludeTests;

/// <summary>
/// E2E tests for real-world FHIR _include and _revinclude scenarios.
/// Tests use scenario data for cleaner test setup and demonstrate practical use cases.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_Scenarios : IncludeTestBase
{
    public IncludeSearchTests_Scenarios(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests _include with multiple resource table parameters.
    /// Ported from: GivenAnIncludeSearchExpressionWithMultipleResourceTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithMultipleResourceTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var location1 = CreateLocation(tag, createdOrg.Id);
        var createdLocation1 = await Harness.CreateResourceAsync(location1);

        // Small delay to ensure different surrogate IDs
        await Task.Delay(100);

        var location2 = CreateLocation(tag, createdOrg.Id);
        var createdLocation2 = await Harness.CreateResourceAsync(location2);

        // Act - search with _lastUpdated filter (only location1 should match if filtered properly)
        var bundle = await Harness.SearchBundleAsync("Location",
            $"_include=Location:organization:Organization&_tag={tag}");

        // Assert
        ValidateBundleContains(bundle, createdOrg.Id, createdLocation1.Id, createdLocation2.Id);
    }

    /// <summary>
    /// Tests _revinclude with multiple resource table parameters and table parameters.
    /// Ported from: GivenARevIncludeSearchExpressionWithMultipleResourceTableParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeSearchExpressionWithMultipleResourceTableParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code");

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var snomedCode = "429858000";
        var snomedSystem = "http://snomed.info/sct";

        // Create patients
        var patients = new[]
        {
            CreatePatientWithReferences(tag, "Smith"),
            CreatePatientWithReferences(tag, "Truman")
        };
        var createdPatients = await Harness.CreateResourcesAsync(patients);

        // Create observations
        var smithObs = CreateObservation(tag, createdPatients[0].Id, snomedCode, snomedSystem);
        var trumanObs = CreateObservation(tag, createdPatients[1].Id, snomedCode, snomedSystem);
        var createdObs = await Harness.CreateResourcesAsync([smithObs, trumanObs]);

        // Create diagnostic reports
        var smithReport = CreateDiagnosticReport(tag, createdPatients[0].Id, snomedCode, snomedSystem, createdObs[0].Id);
        var trumanReport = CreateDiagnosticReport(tag, createdPatients[1].Id, snomedCode, snomedSystem, createdObs[1].Id);
        await Harness.CreateResourcesAsync([smithReport, trumanReport]);

        // Act - revinclude with code filter
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:result&code={snomedCode}");

        // Assert - should include observations and their diagnostic reports
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("DiagnosticReport");
    }

    /// <summary>
    /// Tests _include with multi-type performer references using scenario data.
    /// Demonstrates using IncludeTestScenario for cleaner test setup.
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeWithMultiTypePerformerReference_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - using scenario builder
        var tag = Guid.NewGuid().ToString();
        var data = await CreateIncludeTestDataAsync(tag);

        // Act - include performer without target type (should include both Practitioner and Organization)
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_include=Observation:performer");

        // Assert
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("Practitioner");
        resourceTypes.Should().Contain("Organization");

        // Verify correct resources are included
        ValidateBundleContains(bundle,
            data.Observation1.Id,
            data.Observation2.Id,
            data.Practitioner.Id,
            data.Organization.Id);
    }

    /// <summary>
    /// Tests _revinclude with DiagnosticReport:result using scenario data.
    /// Demonstrates scenario-based test for revinclude functionality.
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeForDiagnosticReportResult_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - using scenario builder
        var tag = Guid.NewGuid().ToString();
        var data = await CreateIncludeTestDataAsync(tag);

        // Act - revinclude DiagnosticReport:result when searching Observations
        var bundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:result");

        // Assert
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Observation");
        resourceTypes.Should().Contain("DiagnosticReport");

        // Verify all resources are present
        ValidateBundleContains(bundle,
            data.Observation1.Id,
            data.Observation2.Id,
            data.DiagnosticReport.Id);

        // Verify search modes
        ValidateSearchEntryMode(bundle, "Observation");

        // Verify count excludes included resources
        var countBundle = await Harness.SearchBundleAsync("Observation",
            $"_tag={tag}&_revinclude=DiagnosticReport:result&_summary=count");
        countBundle.Total.Should().Be(2, "only match resources (observations) should be counted");
    }

    /// <summary>
    /// Tests _include with Group:member references using scenario data.
    /// Demonstrates group member inclusion pattern.
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeForGroupMember_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - using scenario builder
        var tag = Guid.NewGuid().ToString();
        var data = await CreateIncludeTestDataAsync(tag);

        // Act - include Group:member
        var bundle = await Harness.SearchBundleAsync("Group",
            $"_tag={tag}&_include=Group:member:Patient");

        // Assert
        var matchEntries = bundle.Entry.Where(e => e.Search?.Mode == "match").ToList();
        var includeEntries = bundle.Entry.Where(e => e.Search?.Mode == "include").ToList();

        // Should have 1 match (the group) and 2 includes (both patients)
        matchEntries.Should().HaveCount(1);
        matchEntries[0].Resource!.Id.Should().Be(data.Group.Id);

        includeEntries.Should().HaveCount(2);
        var includedPatientIds = includeEntries.Select(e => e.Resource!.Id).ToHashSet();
        includedPatientIds.Should().Contain(data.Patient1.Id);
        includedPatientIds.Should().Contain(data.Patient2.Id);
    }

    /// <summary>
    /// Tests _include with CareTeam:participant multi-type references using scenario data.
    /// Demonstrates CareTeam participant inclusion with mixed types.
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeForCareTeamParticipant_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - using scenario builder
        var tag = Guid.NewGuid().ToString();
        var data = await CreateIncludeTestDataAsync(tag);

        // Act - include CareTeam:participant without type filter
        var bundle = await Harness.SearchBundleAsync("CareTeam",
            $"_tag={tag}&_include=CareTeam:participant");

        // Assert - should include Patient, Organization, and Practitioner
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("CareTeam");
        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("Organization");
        resourceTypes.Should().Contain("Practitioner");

        // Verify correct participants are included
        ValidateBundleContains(bundle,
            data.CareTeam.Id,
            data.Patient1.Id,
            data.Organization.Id,
            data.Practitioner.Id);
    }

    /// <summary>
    /// Tests _include with Location:partof self-reference using scenario data.
    /// Demonstrates hierarchical self-referencing resource inclusion.
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeForLocationPartOf_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Arrange - using scenario builder
        var tag = Guid.NewGuid().ToString();
        var data = await CreateIncludeTestDataAsync(tag);

        // Act - include Location:partof
        var bundle = await Harness.SearchBundleAsync("Location",
            $"_tag={tag}&_include=Location:partof");

        // Assert - should have both locations
        ValidateBundleContains(bundle, data.Location.Id, data.ChildLocation.Id);

        // Both locations match the tag search, so both should be "match"
        // The parent is both a match (has the tag) and an include (referenced by partOf)
        // but resources are deduplicated, and "match" takes precedence
        var matchCount = bundle.Entry.Count(e => e.Search?.Mode == "match");
        matchCount.Should().Be(2, "both locations have the tag");

        // Verify both locations are present
        bundle.Entry.Select(e => e.Resource?.Id).Should().Contain([data.Location.Id, data.ChildLocation.Id]);
    }
}
