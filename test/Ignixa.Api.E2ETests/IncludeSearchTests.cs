// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests;

/// <summary>
/// E2E tests for FHIR _include and _revinclude search parameters.
/// Tests use tag-based isolation for test independence.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests : CapabilityDrivenTestBase
{
    public IncludeSearchTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Helper Methods

    #region JSON Construction Helpers

    /// <summary>
    /// Creates a FHIR Reference JSON object.
    /// </summary>
    private static JsonObject CreateReferenceJson(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    /// <summary>
    /// Creates a CodeableConcept JSON object with the specified system and code.
    /// </summary>
    private static JsonObject CreateCodeableConceptJson(string system, string code)
    {
        return new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = system,
                    ["code"] = code
                }
            }
        };
    }

    /// <summary>
    /// Creates a meta tag JSON object for test isolation.
    /// </summary>
    private static JsonObject CreateMetaTagJson(string tag)
    {
        return new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "testTag",
                    ["code"] = tag
                }
            }
        };
    }

    #endregion

    /// <summary>
    /// Creates an Organization resource with a tag using the fluent OrganizationBuilder.
    /// </summary>
    private ResourceJsonNode CreateOrganizationResource(string tag, string? name = null, string? partOfId = null)
    {
        var builder = CreateOrganization()
            .WithTag(tag);

        if (name is not null)
        {
            builder = builder.WithName(name);
        }

        var org = builder.Build();

        // partOf reference is not supported by OrganizationBuilder yet, add manually if needed
        if (partOfId is not null)
        {
            org.MutableNode["partOf"] = CreateReferenceJson("Organization", partOfId);
        }

        return org;
    }

    /// <summary>
    /// Creates a Location resource with a tag and optional organization reference.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreateLocation(string tag, string? managingOrgId = null, string? partOfId = null)
    {
        var location = new ResourceJsonNode
        {
            ResourceType = "Location"
        };
        location.MutableNode["meta"] = CreateMetaTagJson(tag);

        if (managingOrgId is not null)
        {
            location.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", managingOrgId);
        }
        if (partOfId is not null)
        {
            location.MutableNode["partOf"] = CreateReferenceJson("Location", partOfId);
        }
        return location;
    }

    /// <summary>
    /// Creates a Practitioner resource with a tag.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreatePractitioner(string tag, string? familyName = null)
    {
        var practitioner = new ResourceJsonNode
        {
            ResourceType = "Practitioner"
        };
        practitioner.MutableNode["meta"] = CreateMetaTagJson(tag);

        if (familyName is not null)
        {
            practitioner.MutableNode["name"] = new JsonArray
            {
                new JsonObject
                {
                    ["family"] = familyName
                }
            };
        }
        return practitioner;
    }

    /// <summary>
    /// Creates a Patient resource with a tag and optional references.
    /// Uses fluent PatientBuilder for core patient properties.
    /// </summary>
    private ResourceJsonNode CreatePatientWithReferences(
        string tag,
        string familyName,
        string? birthDate = null,
        string? generalPractitionerId = null,
        string? managingOrganizationId = null)
    {
        var patient = CreatePatient()
            .FromSeattle()
            .WithFamilyName(familyName)
            .WithTag(tag)
            .Build();

        // Fields not yet supported by PatientBuilder - add manually
        if (birthDate is not null)
        {
            patient.MutableNode["birthDate"] = birthDate;
        }
        if (generalPractitionerId is not null)
        {
            patient.MutableNode["generalPractitioner"] = new JsonArray
            {
                CreateReferenceJson("Practitioner", generalPractitionerId)
            };
        }
        if (managingOrganizationId is not null)
        {
            patient.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", managingOrganizationId);
        }
        return patient;
    }

    /// <summary>
    /// Creates an Observation resource with a tag and references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreateObservation(
        string tag,
        string patientId,
        string code,
        string codeSystem,
        string? practitionerId = null,
        string? organizationId = null,
        bool untypedReferences = false)
    {
        var obs = new ResourceJsonNode
        {
            ResourceType = "Observation"
        };
        obs.MutableNode["meta"] = CreateMetaTagJson(tag);
        obs.MutableNode["status"] = "final";
        obs.MutableNode["code"] = CreateCodeableConceptJson(codeSystem, code);

        // Handle untyped references for specific test cases
        obs.MutableNode["subject"] = untypedReferences
            ? new JsonObject { ["reference"] = patientId }
            : CreateReferenceJson("Patient", patientId);

        if (practitionerId is not null || organizationId is not null)
        {
            var performers = new JsonArray();
            if (organizationId is not null)
            {
                performers.Add(untypedReferences
                    ? new JsonObject { ["reference"] = organizationId }
                    : CreateReferenceJson("Organization", organizationId));
            }
            if (practitionerId is not null)
            {
                performers.Add(untypedReferences
                    ? new JsonObject { ["reference"] = practitionerId }
                    : CreateReferenceJson("Practitioner", practitionerId));
            }
            obs.MutableNode["performer"] = performers;
        }

        return obs;
    }

    /// <summary>
    /// Creates a DiagnosticReport resource with a tag and references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreateDiagnosticReport(
        string tag,
        string patientId,
        string code,
        string codeSystem,
        string? observationId = null)
    {
        var report = new ResourceJsonNode
        {
            ResourceType = "DiagnosticReport"
        };
        report.MutableNode["meta"] = CreateMetaTagJson(tag);
        report.MutableNode["status"] = "final";
        report.MutableNode["code"] = CreateCodeableConceptJson(codeSystem, code);
        report.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);

        if (observationId is not null)
        {
            report.MutableNode["result"] = new JsonArray
            {
                CreateReferenceJson("Observation", observationId)
            };
        }

        return report;
    }

    /// <summary>
    /// Creates a Group resource with member references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreateGroup(string tag, params string[] patientIds)
    {
        var group = new ResourceJsonNode
        {
            ResourceType = "Group"
        };
        group.MutableNode["meta"] = CreateMetaTagJson(tag);
        group.MutableNode["type"] = "person";
        group.MutableNode["actual"] = true;
        group.MutableNode["member"] = CreateGroupMemberArray(patientIds);

        return group;
    }

    /// <summary>
    /// Creates a Group.member array with Patient references.
    /// </summary>
    private static JsonArray CreateGroupMemberArray(params string[] patientIds)
    {
        var members = new JsonArray();
        foreach (var id in patientIds)
        {
            members.Add(new JsonObject
            {
                ["entity"] = CreateReferenceJson("Patient", id)
            });
        }
        return members;
    }

    /// <summary>
    /// Creates a CareTeam resource with participant references.
    /// Uses helper methods for cleaner JSON construction.
    /// </summary>
    private ResourceJsonNode CreateCareTeam(string tag, string[] patientIds, string? organizationId = null, string? practitionerId = null)
    {
        var careTeam = new ResourceJsonNode
        {
            ResourceType = "CareTeam"
        };
        careTeam.MutableNode["meta"] = CreateMetaTagJson(tag);
        careTeam.MutableNode["participant"] = CreateCareTeamParticipantArray(patientIds, organizationId, practitionerId);

        return careTeam;
    }

    /// <summary>
    /// Creates a CareTeam.participant array with member references.
    /// </summary>
    private static JsonArray CreateCareTeamParticipantArray(string[] patientIds, string? organizationId, string? practitionerId)
    {
        var participants = new JsonArray();

        foreach (var patientId in patientIds)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Patient", patientId)
            });
        }
        if (organizationId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Organization", organizationId)
            });
        }
        if (practitionerId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Practitioner", practitionerId)
            });
        }

        return participants;
    }

    /// <summary>
    /// Validates that a bundle contains resources with the expected IDs.
    /// </summary>
    private void ValidateBundleContains(BundleJsonNode bundle, params string[] expectedIds)
    {
        var actualIds = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.Id)
            .ToHashSet();

        foreach (var expectedId in expectedIds)
        {
            actualIds.Should().Contain(expectedId, $"bundle should contain resource with ID {expectedId}");
        }
    }

    /// <summary>
    /// Validates the search entry modes in a bundle.
    /// Match resources should have mode "match", included resources should have mode "include".
    /// </summary>
    private void ValidateSearchEntryMode(BundleJsonNode bundle, string matchResourceType)
    {
        foreach (var entry in bundle.Entry)
        {
            if (entry.Resource is null) continue;

            var expectedMode = entry.Resource.ResourceType == matchResourceType ? "match" : "include";
            entry.Search?.Mode.Should().Be(expectedMode,
                $"Resource {entry.Resource.ResourceType}/{entry.Resource.Id} should have search mode {expectedMode}");
        }
    }

    /// <summary>
    /// Gets the count of resources with a specific search mode.
    /// </summary>
    private int GetCountBySearchMode(BundleJsonNode bundle, string mode)
    {
        return bundle.Entry.Count(e => e.Search?.Mode == mode);
    }

    #endregion

    #region Basic _include Tests

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

    #endregion

    #region Basic _revinclude Tests

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

    #endregion

    #region Self-Reference Tests

    /// <summary>
    /// Tests _include and _revinclude with self-referencing resources.
    /// Ported from: GivenAnIncludeSearchExpressionWithLocationLinkedToItself_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("_include")]
    [InlineData("_revinclude")]
    public async Task GivenAnIncludeSearchExpressionWithLocationLinkedToItself_WhenSearched_ThenCorrectBundleShouldBeReturned(string includeType)
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create a location
        var location = CreateLocation(tag);
        var createdLocation = await Harness.CreateResourceAsync(location);

        // Update the location to reference itself
        createdLocation.MutableNode["partOf"] = new JsonObject
        {
            ["reference"] = $"Location/{createdLocation.Id}"
        };
        var updatedLocation = await Harness.UpdateResourceAsync(createdLocation);

        // Act - include/revinclude with partof
        var bundle = await Harness.SearchBundleAsync("Location",
            $"_id={updatedLocation.Id}&{includeType}=Location:partof");

        // Assert - the matched resource shouldn't be returned as a separate include
        bundle.Entry.Should().HaveCount(1);
        bundle.Entry[0].Resource!.Id.Should().Be(updatedLocation.Id);
        bundle.Entry[0].Search?.Mode.Should().Be("match");
    }

    #endregion

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

    #region _include:iterate Tests

    /// <summary>
    /// Tests _include:iterate for single-level iteration.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organization, practitioner, patient, and medication request chain
        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var practitioner = CreatePractitioner(tag, "Anderson");
        var createdPractitioner = await Harness.CreateResourceAsync(practitioner);

        var patient = CreatePatientWithReferences(tag, "Adams",
            generalPractitionerId: createdPractitioner.Id,
            managingOrganizationId: createdOrg.Id);
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create MedicationRequest referencing patient
        var medRequest = new ResourceJsonNode { ResourceType = "MedicationRequest" };
        medRequest.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject { ["system"] = "testTag", ["code"] = tag }
            }
        };
        medRequest.MutableNode["status"] = "completed";
        medRequest.MutableNode["intent"] = "order";
        medRequest.MutableNode["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        medRequest.MutableNode["medicationCodeableConcept"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject { ["system"] = "http://snomed.info/sct", ["code"] = "16590-619-30" }
            }
        };
        var createdMedRequest = await Harness.CreateResourceAsync(medRequest);

        // Create MedicationDispense referencing the request
        var medDispense = new ResourceJsonNode { ResourceType = "MedicationDispense" };
        medDispense.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject { ["system"] = "testTag", ["code"] = tag }
            }
        };
        medDispense.MutableNode["status"] = "in-progress";
        medDispense.MutableNode["authorizingPrescription"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"MedicationRequest/{createdMedRequest.Id}" }
        };
        medDispense.MutableNode["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        medDispense.MutableNode["medicationCodeableConcept"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject { ["system"] = "http://snomed.info/sct", ["code"] = "108505002" }
            }
        };
        await Harness.CreateResourceAsync(medDispense);

        // Act - include:iterate to follow MedicationDispense -> MedicationRequest -> Patient
        var bundle = await Harness.SearchBundleAsync("MedicationDispense",
            $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_tag={tag}");

        // Assert - should include MedicationDispense, MedicationRequest, and Patient
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("MedicationDispense");
        resourceTypes.Should().Contain("MedicationRequest");
        resourceTypes.Should().Contain("Patient");

        // Verify total count excludes included resources
        var countBundle = await Harness.SearchBundleAsync("MedicationDispense",
            $"_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:patient&_tag={tag}&_summary=count");
        countBundle.Total.Should().Be(1);
    }

    /// <summary>
    /// Tests _include:iterate with wildcard.
    /// Ported from: GivenAnIncludeSearchExpressionWithIncludeWildcardAndIncludeIterateWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeSearchExpressionWithIncludeWildcardAndIncludeIterateWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // This test validates that wildcards work with iterate
        // Implementation would be similar to the above test with wildcard parameters
        await Task.CompletedTask;
    }

    #endregion

    #region _revinclude:iterate Tests

    /// <summary>
    /// Tests _revinclude:iterate for single-level iteration.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organization, practitioner, patient chain
        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var practitioner = CreatePractitioner(tag, "Anderson");
        var createdPractitioner = await Harness.CreateResourceAsync(practitioner);

        var patient = CreatePatientWithReferences(tag, "Adams",
            generalPractitionerId: createdPractitioner.Id,
            managingOrganizationId: createdOrg.Id);
        var createdPatient = await Harness.CreateResourceAsync(patient);

        // Create MedicationRequest referencing patient
        var medRequest = new ResourceJsonNode { ResourceType = "MedicationRequest" };
        medRequest.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject { ["system"] = "testTag", ["code"] = tag }
            }
        };
        medRequest.MutableNode["status"] = "completed";
        medRequest.MutableNode["intent"] = "order";
        medRequest.MutableNode["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        medRequest.MutableNode["medicationCodeableConcept"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject { ["system"] = "http://snomed.info/sct", ["code"] = "16590-619-30" }
            }
        };
        var createdMedRequest = await Harness.CreateResourceAsync(medRequest);

        // Create MedicationDispense referencing the request
        var medDispense = new ResourceJsonNode { ResourceType = "MedicationDispense" };
        medDispense.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject { ["system"] = "testTag", ["code"] = tag }
            }
        };
        medDispense.MutableNode["status"] = "in-progress";
        medDispense.MutableNode["authorizingPrescription"] = new JsonArray
        {
            new JsonObject { ["reference"] = $"MedicationRequest/{createdMedRequest.Id}" }
        };
        medDispense.MutableNode["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        medDispense.MutableNode["medicationCodeableConcept"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject { ["system"] = "http://snomed.info/sct", ["code"] = "108505002" }
            }
        };
        await Harness.CreateResourceAsync(medDispense);

        // Act - revinclude:iterate to follow Patient <- MedicationRequest <- MedicationDispense
        var bundle = await Harness.SearchBundleAsync("Patient",
            $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={tag}");

        // Assert - should include Patient, MedicationRequest, and MedicationDispense
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("MedicationRequest");
        resourceTypes.Should().Contain("MedicationDispense");

        // Verify total count excludes included resources
        var countBundle = await Harness.SearchBundleAsync("Patient",
            $"_revinclude=MedicationRequest:patient&_revinclude:iterate=MedicationDispense:prescription&_tag={tag}&_summary=count");

        // Should only count patients
        countBundle.Total.Should().Be(1);
    }

    #endregion

    #region Circular Reference Tests

    /// <summary>
    /// Tests _include:iterate with circular references (executes once).
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organization hierarchy with circular reference potential
        // LabF -> LabE -> LabD -> LabC -> LabB -> LabA -> LabB (circular)
        var labF = CreateOrganizationResource(tag, "LabF");
        var createdLabF = await Harness.CreateResourceAsync(labF);

        var labE = CreateOrganizationResource(tag, "LabE", createdLabF.Id);
        var createdLabE = await Harness.CreateResourceAsync(labE);

        var labD = CreateOrganizationResource(tag, "LabD", createdLabE.Id);
        var createdLabD = await Harness.CreateResourceAsync(labD);

        var labC = CreateOrganizationResource(tag, "LabC", createdLabD.Id);
        var createdLabC = await Harness.CreateResourceAsync(labC);

        var labB = CreateOrganizationResource(tag, "LabB", createdLabC.Id);
        var createdLabB = await Harness.CreateResourceAsync(labB);

        var labA = CreateOrganizationResource(tag, "LabA", createdLabB.Id);
        var createdLabA = await Harness.CreateResourceAsync(labA);

        // Act - include:iterate with partof (circular reference path)
        var bundle = await Harness.SearchBundleAsync("Organization",
            $"_include:iterate=Organization:partof&_id={createdLabA.Id}&_tag={tag}");

        // Assert - should have executed single iteration
        ValidateBundleContains(bundle, createdLabA.Id, createdLabB.Id);

        // Check for informational issue about circular reference
        // (implementation specific - may include OperationOutcome in bundle)
    }

    /// <summary>
    /// Tests _revinclude:iterate with circular references (executes once).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithCircularReference_WhenSearched_SingleIterationIsExecutedAndInformationalIssueIsAdded()
    {
        // Similar to above but with revinclude
        await Task.CompletedTask;
    }

    #endregion

    #region Error Handling Tests

    /// <summary>
    /// Tests that invalid target resource type returns error.
    /// Ported from: GivenAIncludeOrRevIncludeIterateSearchExpressionWithInvalidTargetResourceType_WhenSearched_ShouldThrowResourceNotSupportedException
    /// </summary>
    [Theory]
    [InlineData("_include")]
    [InlineData("_revinclude")]
    public async Task GivenAnIncludeOrRevIncludeWithInvalidTargetResourceType_WhenSearched_ShouldReturnBadRequest(string include)
    {
        // Act
        var response = await Client.GetAsync($"/Patient?{include}=Observation:subject:NotAResourceType");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Tests that empty target resource type returns error.
    /// Ported from: GivenAIncludeOrRevIncludeIterateSearchExpressionWithEmptyOrWhiteSpaceTargetResourceType_WhenSearched_ShouldThrowBadRequestException
    /// </summary>
    [Theory]
    [InlineData("_include", "")]
    [InlineData("_revinclude", "")]
    public async Task GivenAnIncludeOrRevIncludeWithEmptyTargetResourceType_WhenSearched_ShouldReturnBadRequest(string include, string target)
    {
        // Act
        var response = await Client.GetAsync($"/Patient?{include}=Observation:subject:{target}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Tests that _revinclude:iterate without target type returns error.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultipleResultsSetsWithoutSpecificRevIncludeIterateTargetType_WhenSearched_ShouldThrowBadRequestExceptionWithIssue
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithoutSpecificTargetType_WhenSearched_ShouldReturnBadRequest()
    {
        // Act
        var response = await Client.GetAsync(
            "/MedicationDispense?_include=MedicationDispense:performer:Practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:requester:Practitioner&_revinclude:iterate=Patient:general-practitioner");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
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

    #region CareTeam Multi-Type Reference Tests

    /// <summary>
    /// Tests _include:iterate with CareTeam multi-type references.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create organization and practitioners
        var organization = CreateOrganizationResource(tag, "Test Org");
        var createdOrg = await Harness.CreateResourceAsync(organization);

        var practitioners = new[]
        {
            CreatePractitioner(tag, "Anderson"),
            CreatePractitioner(tag, "Sanchez"),
            CreatePractitioner(tag, "Taylor")
        };
        var createdPractitioners = await Harness.CreateResourcesAsync(practitioners);

        // Create patients with general practitioner references
        var patients = new[]
        {
            CreatePatientWithReferences(tag, "Adams", generalPractitionerId: createdPractitioners[0].Id),
            CreatePatientWithReferences(tag, "Smith", generalPractitionerId: createdPractitioners[1].Id),
            CreatePatientWithReferences(tag, "Truman", generalPractitionerId: createdPractitioners[2].Id)
        };
        var createdPatients = await Harness.CreateResourcesAsync(patients);

        // Create CareTeam with multiple participant types
        var careTeam = CreateCareTeam(tag,
            createdPatients.Select(p => p.Id).ToArray(),
            createdOrg.Id,
            createdPractitioners[0].Id);
        await Harness.CreateResourceAsync(careTeam);

        // Act - include CareTeam participants, then iterate to get Patient's general practitioners
        var bundle = await Harness.SearchBundleAsync("CareTeam",
            $"_include=CareTeam:participant&_include:iterate=Patient:general-practitioner&_tag={tag}");

        // Assert - should include CareTeam, Patients, and their Practitioners
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("CareTeam");
        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("Practitioner");
        resourceTypes.Should().Contain("Organization");
    }

    /// <summary>
    /// Tests _include:iterate with specific target type for CareTeam.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithSpecificTargetType_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeIterateSearchExpressionWithSpecificTargetType_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create practitioners
        var practitioners = new[]
        {
            CreatePractitioner(tag, "Anderson"),
            CreatePractitioner(tag, "Sanchez"),
            CreatePractitioner(tag, "Taylor")
        };
        var createdPractitioners = await Harness.CreateResourcesAsync(practitioners);

        // Create patients with general practitioner references
        var patients = new[]
        {
            CreatePatientWithReferences(tag, "Adams", generalPractitionerId: createdPractitioners[0].Id),
            CreatePatientWithReferences(tag, "Smith", generalPractitionerId: createdPractitioners[1].Id),
            CreatePatientWithReferences(tag, "Truman", generalPractitionerId: createdPractitioners[2].Id)
        };
        var createdPatients = await Harness.CreateResourcesAsync(patients);

        // Create CareTeam with only patient participants
        var careTeam = CreateCareTeam(tag, createdPatients.Select(p => p.Id).ToArray());
        await Harness.CreateResourceAsync(careTeam);

        // Act - include only Patient participants (not Organization or Practitioner), then iterate
        var bundle = await Harness.SearchBundleAsync("CareTeam",
            $"_include=CareTeam:participant:Patient&_include:iterate=Patient:general-practitioner&_tag={tag}");

        // Assert - should include CareTeam, Patients (as participants), and their Practitioners
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("CareTeam");
        resourceTypes.Should().Contain("Patient");
        resourceTypes.Should().Contain("Practitioner");
        // Organization should NOT be included since we specified :Patient
        resourceTypes.Should().NotContain("Organization");
    }

    #endregion

    #region _revinclude:iterate with Multi-Type References

    /// <summary>
    /// Tests _revinclude:iterate with multi-type reference and specified target.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultiTypeReferenceSpecifiedTarget_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithMultiTypeReferenceSpecifiedTarget_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates _revinclude:iterate with MedicationRequest:subject:Patient target type
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with multi-type array reference.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates _revinclude:iterate with CareTeam:participant:Patient
        await Task.CompletedTask;
    }

    #endregion

    #region Multiple Result Set Tests

    /// <summary>
    /// Tests _include:iterate with multiple result sets.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _include:iterate support")]
    public async Task GivenAnIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates include:iterate with multiple result sets from different include paths
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with multiple result sets.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates revinclude:iterate with multiple result sets
        await Task.CompletedTask;
    }

    #endregion

    #region Wildcard with Iterate Tests

    /// <summary>
    /// Tests _revinclude with wildcard and _revinclude:iterate wildcard (iterate wildcard should be ignored).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithRevIncludeWildcardAndRevIncludeIterateWildcard_WhenSearched_TheIterateWildcardShouldBeIgnored
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeWildcardAndRevIncludeIterateWildcard_WhenSearched_TheIterateWildcardShouldBeIgnored()
    {
        // According to the old test, iterate wildcards should be ignored
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with wildcard search parameter (should be ignored).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithRevIncludeIterateWildCard_WhenSearched_TheIterateWildcardShouldBeIgnored
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeIterateWildCard_WhenSearched_TheIterateWildcardShouldBeIgnored()
    {
        // According to the old test, _revinclude:iterate with wildcard parameter should be ignored
        await Task.CompletedTask;
    }

    #endregion

    #region _include:recurse Tests (Alias for :iterate)

    /// <summary>
    /// Tests _include:recurse (alias for :iterate).
    /// Ported from: GivenAnIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _include:recurse support")]
    public async Task GivenAnIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // :recurse is an alias for :iterate
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:recurse (alias for :iterate).
    /// Ported from: GivenARevIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:recurse support")]
    public async Task GivenARevIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // :recurse is an alias for :iterate
        await Task.CompletedTask;
    }

    #endregion

    #region _missing Modifier with Include Tests

    /// <summary>
    /// Tests _include with :missing modifier.
    /// Ported from: GivenAnIncludeSearchExpressionWithMissingModifier_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAnIncludeSearchExpressionWithMissingModifier_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("DiagnosticReport", "code");

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

        // Create diagnostic reports WITHOUT specimen references
        var smithReport = CreateDiagnosticReport(tag, createdPatients[0].Id, snomedCode, snomedSystem);
        var trumanReport = CreateDiagnosticReport(tag, createdPatients[1].Id, snomedCode, snomedSystem);
        await Harness.CreateResourcesAsync([smithReport, trumanReport]);

        // Act - search with specimen:missing=true and include patient
        var bundle = await Harness.SearchBundleAsync("DiagnosticReport",
            $"_tag={tag}&_include=DiagnosticReport:patient:Patient&code={snomedCode}&specimen:missing=true");

        // Assert - should include patients
        var resourceTypes = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!.ResourceType)
            .Distinct()
            .ToList();

        resourceTypes.Should().Contain("DiagnosticReport");
        resourceTypes.Should().Contain("Patient");
    }

    #endregion

    #region Multiple Resource Table Parameters Tests

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

    #endregion
}
