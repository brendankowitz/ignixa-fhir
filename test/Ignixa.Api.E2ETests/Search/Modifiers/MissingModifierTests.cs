// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.Api.E2ETests.Search.Modifiers;

/// <summary>
/// E2E tests for FHIR :missing search modifier functionality.
/// The :missing modifier tests whether a search parameter value is present or absent in a resource.
/// Per FHIR spec, :missing=true returns resources where the parameter is absent,
/// and :missing=false returns resources where the parameter is present.
/// </summary>
/// <remarks>
/// Test Coverage (HIGH priority per e2e-test-gap-analysis.md):
/// - :missing modifier with token parameters (gender, active)
/// - :missing modifier with string parameters (telecom, address)
/// - :missing modifier with reference parameters (managingOrganization)
/// - :missing=true (field is absent)
/// - :missing=false (field is present)
/// - Multiple search parameters combined with :missing
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class MissingModifierTests : CapabilityDrivenTestBase
{
    public MissingModifierTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Token Parameter Tests

    /// <summary>
    /// Tests that :missing=true returns only resources where the 'gender' field is absent.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutGender_WhenSearchedWithGenderMissingTrue_ThenReturnsOnlyPatientsWithoutGender()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with gender specified
        var patientWithGender = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();

        // Patient without gender (remove after building)
        var patientWithoutGender = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutGender.MutableNode.Remove("gender");

        await Harness.CreateResourcesAsync([patientWithGender, patientWithoutGender]);

        // Act - Search for patients with gender missing
        var results = await Harness.SearchAsync("Patient", $"gender:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without gender should be returned");
        results[0].Id.ShouldBe(patientWithoutGender.Id);
        results[0].MutableNode.ContainsKey("gender").ShouldBeFalse("returned patient should not have gender field");
    }

    /// <summary>
    /// Tests that :missing=false returns only resources where the 'gender' field is present.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutGender_WhenSearchedWithGenderMissingFalse_ThenReturnsOnlyPatientsWithGender()
    {
        // Capability check
        RequireSearchParameter("Patient", "gender");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with gender specified
        var patientWithGender = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Male)
            .WithTag(tag)
            .Build();

        // Patient without gender (remove after building)
        var patientWithoutGender = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutGender.MutableNode.Remove("gender");

        await Harness.CreateResourcesAsync([patientWithGender, patientWithoutGender]);

        // Act - Search for patients with gender present
        var results = await Harness.SearchAsync("Patient", $"gender:missing=false&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with gender should be returned");
        results[0].Id.ShouldBe(patientWithGender.Id);
        results[0].MutableNode.ContainsKey("gender").ShouldBeTrue("returned patient should have gender field");
        results[0].MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
    }

    /// <summary>
    /// Tests that :missing=true returns only resources where the 'active' field is absent.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutActive_WhenSearchedWithActiveMissingTrue_ThenReturnsOnlyPatientsWithoutActive()
    {
        // Capability check
        RequireSearchParameter("Patient", "active");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with active field
        var patientWithActive = CreatePatient()
            .FromSeattle()
            .WithActive(true)
            .WithTag(tag)
            .Build();

        // Patient without active field (remove after building)
        var patientWithoutActive = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutActive.MutableNode.Remove("active");

        await Harness.CreateResourcesAsync([patientWithActive, patientWithoutActive]);

        // Act - Search for patients with active missing
        var results = await Harness.SearchAsync("Patient", $"active:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without active field should be returned");
        results[0].Id.ShouldBe(patientWithoutActive.Id);
        results[0].MutableNode.ContainsKey("active").ShouldBeFalse("returned patient should not have active field");
    }

    /// <summary>
    /// Tests that :missing=false returns only resources where the 'active' field is present.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutActive_WhenSearchedWithActiveMissingFalse_ThenReturnsOnlyPatientsWithActive()
    {
        // Capability check
        RequireSearchParameter("Patient", "active");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with active=false (testing that false value is still "present")
        var patientWithActiveFalse = CreatePatient()
            .FromSeattle()
            .WithActive(false)
            .WithTag(tag)
            .Build();

        // Patient with active=true
        var patientWithActiveTrue = CreatePatient()
            .FromSeattle()
            .WithActive(true)
            .WithTag(tag)
            .Build();

        // Patient without active field (remove after building)
        var patientWithoutActive = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutActive.MutableNode.Remove("active");

        await Harness.CreateResourcesAsync([patientWithActiveFalse, patientWithActiveTrue, patientWithoutActive]);

        // Act - Search for patients with active present
        var results = await Harness.SearchAsync("Patient", $"active:missing=false&_tag={tag}");

        // Assert
        results.Length.ShouldBe(2, "both patients with active field should be returned, regardless of value");
        foreach (var r in results)
        {
            r.MutableNode.ContainsKey("active").ShouldBeTrue("returned patients should have active field");
        }
        results.ShouldContain(r => r.Id == patientWithActiveFalse.Id);
        results.ShouldContain(r => r.Id == patientWithActiveTrue.Id);
    }

    #endregion

    #region ContactPoint/Telecom Parameter Tests

    /// <summary>
    /// Tests that :missing=true returns only resources where the 'telecom' field is absent.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutTelecom_WhenSearchedWithTelecomMissingTrue_ThenReturnsOnlyPatientsWithoutTelecom()
    {
        // Capability check
        RequireSearchParameter("Patient", "telecom");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with telecom (PatientBuilder adds telecom when area code is set)
        var patientWithTelecom = CreatePatient()
            .FromSeattle()  // Seattle sets area code, which generates telecom
            .WithTag(tag)
            .Build();

        // Patient without telecom (remove after building)
        var patientWithoutTelecom = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutTelecom.MutableNode.Remove("telecom");

        await Harness.CreateResourcesAsync([patientWithTelecom, patientWithoutTelecom]);

        // Act - Search for patients with telecom missing
        var results = await Harness.SearchAsync("Patient", $"telecom:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without telecom should be returned");
        results[0].Id.ShouldBe(patientWithoutTelecom.Id);
        results[0].MutableNode.ContainsKey("telecom").ShouldBeFalse("returned patient should not have telecom field");
    }

    /// <summary>
    /// Tests that :missing=false returns only resources where the 'telecom' field is present.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutTelecom_WhenSearchedWithTelecomMissingFalse_ThenReturnsOnlyPatientsWithTelecom()
    {
        // Capability check
        RequireSearchParameter("Patient", "telecom");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with telecom (PatientBuilder adds telecom when area code is set)
        var patientWithTelecom = CreatePatient()
            .FromSeattle()  // Seattle sets area code, which generates telecom
            .WithTag(tag)
            .Build();

        // Patient without telecom (remove after building)
        var patientWithoutTelecom = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutTelecom.MutableNode.Remove("telecom");

        await Harness.CreateResourcesAsync([patientWithTelecom, patientWithoutTelecom]);

        // Act - Search for patients with telecom present
        var results = await Harness.SearchAsync("Patient", $"telecom:missing=false&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with telecom should be returned");
        results[0].Id.ShouldBe(patientWithTelecom.Id);
        results[0].MutableNode.ContainsKey("telecom").ShouldBeTrue("returned patient should have telecom field");
    }

    #endregion

    #region Address Parameter Tests

    /// <summary>
    /// Tests that :missing=true returns only resources where the 'address' field is absent.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutAddress_WhenSearchedWithAddressMissingTrue_ThenReturnsOnlyPatientsWithoutAddress()
    {
        // Capability check
        RequireSearchParameter("Patient", "address");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with address (PatientBuilder adds address when city is set)
        var patientWithAddress = CreatePatient()
            .FromSeattle()  // Seattle sets city, which generates address
            .WithTag(tag)
            .Build();

        // Patient without address (remove after building)
        var patientWithoutAddress = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutAddress.MutableNode.Remove("address");

        await Harness.CreateResourcesAsync([patientWithAddress, patientWithoutAddress]);

        // Act - Search for patients with address missing
        var results = await Harness.SearchAsync("Patient", $"address:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without address should be returned");
        results[0].Id.ShouldBe(patientWithoutAddress.Id);
        results[0].MutableNode.ContainsKey("address").ShouldBeFalse("returned patient should not have address field");
    }

    /// <summary>
    /// Tests that :missing=false returns only resources where the 'address' field is present.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutAddress_WhenSearchedWithAddressMissingFalse_ThenReturnsOnlyPatientsWithAddress()
    {
        // Capability check
        RequireSearchParameter("Patient", "address");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with address (PatientBuilder adds address when city is set)
        var patientWithAddress = CreatePatient()
            .FromSeattle()  // Seattle sets city, which generates address
            .WithTag(tag)
            .Build();

        // Patient without address (remove after building)
        var patientWithoutAddress = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutAddress.MutableNode.Remove("address");

        await Harness.CreateResourcesAsync([patientWithAddress, patientWithoutAddress]);

        // Act - Search for patients with address present
        var results = await Harness.SearchAsync("Patient", $"address:missing=false&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with address should be returned");
        results[0].Id.ShouldBe(patientWithAddress.Id);
        results[0].MutableNode.ContainsKey("address").ShouldBeTrue("returned patient should have address field");
    }

    #endregion

    #region Reference Parameter Tests

    /// <summary>
    /// Tests that :missing=true returns only resources where the 'organization' reference is absent.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutOrganization_WhenSearchedWithOrganizationMissingTrue_ThenReturnsOnlyPatientsWithoutOrganization()
    {
        // Capability check
        RequireSearchParameter("Patient", "organization");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create an organization first
        var organization = CreateOrganization()
            .WithName("Test Hospital")
            .WithTag(tag)
            .Build();
        var createdOrg = await Harness.CreateResourceAsync(organization);

        // Patient with organization reference
        var patientWithOrg = CreatePatient()
            .FromSeattle()
            .WithManagingOrganization(createdOrg.Id!)
            .WithTag(tag)
            .Build();

        // Patient without organization reference
        var patientWithoutOrg = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        // Note: PatientBuilder doesn't add managingOrganization by default, so no need to remove

        await Harness.CreateResourcesAsync([patientWithOrg, patientWithoutOrg]);

        // Act - Search for patients with organization missing
        var results = await Harness.SearchAsync("Patient", $"organization:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without organization should be returned");
        results[0].Id.ShouldBe(patientWithoutOrg.Id);
        results[0].MutableNode.ContainsKey("managingOrganization").ShouldBeFalse("returned patient should not have managingOrganization field");
    }

    /// <summary>
    /// Tests that :missing=false returns only resources where the 'organization' reference is present.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithAndWithoutOrganization_WhenSearchedWithOrganizationMissingFalse_ThenReturnsOnlyPatientsWithOrganization()
    {
        // Capability check
        RequireSearchParameter("Patient", "organization");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create an organization first
        var organization = CreateOrganization()
            .WithName("Test Hospital")
            .WithTag(tag)
            .Build();
        var createdOrg = await Harness.CreateResourceAsync(organization);

        // Patient with organization reference
        var patientWithOrg = CreatePatient()
            .FromSeattle()
            .WithManagingOrganization(createdOrg.Id!)
            .WithTag(tag)
            .Build();

        // Patient without organization reference
        var patientWithoutOrg = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientWithOrg, patientWithoutOrg]);

        // Act - Search for patients with organization present
        var results = await Harness.SearchAsync("Patient", $"organization:missing=false&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with organization should be returned");
        results[0].Id.ShouldBe(patientWithOrg.Id);
        results[0].MutableNode.ContainsKey("managingOrganization").ShouldBeTrue("returned patient should have managingOrganization field");
    }

    #endregion

    #region Combination Tests

    /// <summary>
    /// Tests combining :missing modifier with regular search parameters.
    /// This tests AND logic: gender=female AND active:missing=true.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithVariousGenderAndActive_WhenSearchedWithGenderAndActiveMissing_ThenReturnsOnlyMatchingPatients()
    {
        // Capability check
        RequireSearchParameters("Patient", "gender", "active");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Female patient without active field - SHOULD MATCH
        var femaleWithoutActive = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();
        femaleWithoutActive.MutableNode.Remove("active");

        // Female patient with active field - should NOT match
        var femaleWithActive = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithActive(true)
            .WithTag(tag)
            .Build();

        // Male patient without active field - should NOT match (wrong gender)
        var maleWithoutActive = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Male)
            .WithTag(tag)
            .Build();
        maleWithoutActive.MutableNode.Remove("active");

        // Male patient with active field - should NOT match (wrong gender and has active)
        var maleWithActive = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Male)
            .WithActive(true)
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([femaleWithoutActive, femaleWithActive, maleWithoutActive, maleWithActive]);

        // Act - Search for female patients without active field
        var results = await Harness.SearchAsync("Patient", $"gender=female&active:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the female patient without active field should be returned");
        results[0].Id.ShouldBe(femaleWithoutActive.Id);
        results[0].MutableNode["gender"]?.GetValue<string>().ShouldBe("female");
        results[0].MutableNode.ContainsKey("active").ShouldBeFalse();
    }

    /// <summary>
    /// Tests multiple :missing modifiers in a single query.
    /// This tests AND logic: gender:missing=true AND address:missing=true.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithVariousFields_WhenSearchedWithMultipleMissingModifiers_ThenReturnsOnlyPatientsMatchingAll()
    {
        // Capability check
        RequireSearchParameters("Patient", "gender", "address");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient without gender AND without address - SHOULD MATCH
        var patientWithoutBoth = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutBoth.MutableNode.Remove("gender");
        patientWithoutBoth.MutableNode.Remove("address");

        // Patient without gender but WITH address - should NOT match
        var patientWithoutGenderWithAddress = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();
        patientWithoutGenderWithAddress.MutableNode.Remove("gender");

        // Patient WITH gender but without address - should NOT match
        var patientWithGenderWithoutAddress = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Female)
            .WithTag(tag)
            .Build();
        patientWithGenderWithoutAddress.MutableNode.Remove("address");

        // Patient WITH both gender and address - should NOT match
        var patientWithBoth = CreatePatient()
            .FromSeattle()
            .WithGender(g => g.Male)
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientWithoutBoth, patientWithoutGenderWithAddress, patientWithGenderWithoutAddress, patientWithBoth]);

        // Act - Search for patients without gender AND without address
        var results = await Harness.SearchAsync("Patient", $"gender:missing=true&address:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient without both gender and address should be returned");
        results[0].Id.ShouldBe(patientWithoutBoth.Id);
        results[0].MutableNode.ContainsKey("gender").ShouldBeFalse();
        results[0].MutableNode.ContainsKey("address").ShouldBeFalse();
    }

    #endregion

    #region Observation Tests

    /// <summary>
    /// Tests :missing modifier on Observation resource with 'value-quantity' parameter.
    /// Some observations may not have a value (e.g., status=cancelled).
    /// </summary>
    [Fact]
    public async Task GivenObservationsWithAndWithoutValue_WhenSearchedWithValueQuantityMissingTrue_ThenReturnsOnlyObservationsWithoutValue()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Create patient and encounter for context
        var patient = CreatePatient().FromSeattle().WithTag(tag).Build();
        var createdPatient = await Harness.CreateResourceAsync(patient);

        var faker = Harness.CreateFaker().WithTag(tag);

        // Observation with valueQuantity
        var obsWithValue = faker.Generate("Observation");
        obsWithValue.MutableNode["subject"] = new System.Text.Json.Nodes.JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        obsWithValue.MutableNode["code"] = new System.Text.Json.Nodes.JsonObject
        {
            ["coding"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["system"] = "http://loinc.org",
                    ["code"] = "29463-7",
                    ["display"] = "Body Weight"
                }
            }
        };
        obsWithValue.MutableNode["valueQuantity"] = new System.Text.Json.Nodes.JsonObject
        {
            ["value"] = 75,
            ["unit"] = "kg",
            ["system"] = "http://unitsofmeasure.org",
            ["code"] = "kg"
        };

        // Observation without valueQuantity
        var obsWithoutValue = faker.Generate("Observation");
        obsWithoutValue.MutableNode["subject"] = new System.Text.Json.Nodes.JsonObject
        {
            ["reference"] = $"Patient/{createdPatient.Id}"
        };
        obsWithoutValue.MutableNode["code"] = new System.Text.Json.Nodes.JsonObject
        {
            ["coding"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["system"] = "http://loinc.org",
                    ["code"] = "8867-4",
                    ["display"] = "Heart Rate"
                }
            }
        };
        obsWithoutValue.MutableNode.Remove("valueQuantity");
        obsWithoutValue.MutableNode["status"] = "cancelled";  // Reason for no value

        await Harness.CreateResourcesAsync([obsWithValue, obsWithoutValue]);

        // Act - Search for observations without valueQuantity
        var results = await Harness.SearchAsync("Observation", $"value-quantity:missing=true&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the observation without valueQuantity should be returned");
        results[0].Id.ShouldBe(obsWithoutValue.Id);
        results[0].MutableNode.ContainsKey("valueQuantity").ShouldBeFalse("returned observation should not have valueQuantity field");
    }

    #endregion
}
