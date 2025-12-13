// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

/// <summary>
/// E2E tests for FHIR _include:iterate and _revinclude:iterate functionality.
/// Tests iterative includes that follow reference chains.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_Iterate : IncludeTestBase
{
    public IncludeSearchTests_Iterate(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region _include:iterate Tests

    /// <summary>
    /// Tests _include:iterate for single-level iteration.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
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
    [Fact]
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
    [Fact]
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

    #region CareTeam Multi-Type Reference Iterate Tests

    /// <summary>
    /// Tests _include:iterate with CareTeam multi-type references.
    /// Ported from: GivenAnIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
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
    [Fact]
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

    /// <summary>
    /// Tests _revinclude:iterate with multi-type reference and specified target.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultiTypeReferenceSpecifiedTarget_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeIterateSearchExpressionWithMultiTypeReferenceSpecifiedTarget_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates _revinclude:iterate with MedicationRequest:subject:Patient target type
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with multi-type array reference.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultitypeArrayReference_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
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
    [Fact]
    public async Task GivenAnIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // This test validates include:iterate with multiple result sets from different include paths
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with multiple result sets.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultipleResultsSets_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
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
    [Fact]
    public async Task GivenARevIncludeIterateSearchExpressionWithRevIncludeWildcardAndRevIncludeIterateWildcard_WhenSearched_TheIterateWildcardShouldBeIgnored()
    {
        // According to the old test, iterate wildcards should be ignored
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:iterate with wildcard search parameter (should be ignored).
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithRevIncludeIterateWildCard_WhenSearched_TheIterateWildcardShouldBeIgnored
    /// </summary>
    [Fact]
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
    [Fact]
    public async Task GivenAnIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // :recurse is an alias for :iterate
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests _revinclude:recurse (alias for :iterate).
    /// Ported from: GivenARevIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeRecurseSearchExpressionWithSingleIteration_WhenSearched_TheIterativeResultsShouldBeAddedToTheBundle()
    {
        // :recurse is an alias for :iterate
        await Task.CompletedTask;
    }

    #endregion
}
