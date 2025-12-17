// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;

namespace Ignixa.Api.E2ETests.Search.Modifiers;

/// <summary>
/// E2E tests for FHIR :of-type search modifier on identifier parameters.
/// The :of-type modifier filters identifiers by their type code in addition to value.
/// Per FHIR spec, the format is: identifier:of-type=[type-system]|[type-code]|[identifier-value]
/// </summary>
/// <remarks>
/// Test Coverage (HIGH priority per e2e-test-gap-analysis.md):
/// - Basic of-type search with Medical Record Number (MR)
/// - Multiple identifier types on same patient (MR, SSN, DL)
/// - Type AND value matching (both must match)
/// - Same value, different type (should NOT match)
/// - Same type, different value (should NOT match)
/// - OR logic with multiple of-type values
/// - Missing type system (system|code|value vs just code|value)
/// - Common identifier types from v2-0203 code system
/// </remarks>
[Collection(E2ETestCollection.Name)]
[Trait("Category", "SkipCI")]
public class IdentifierOfTypeTests : CapabilityDrivenTestBase
{
    private const string IdentifierTypeSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";

    public IdentifierOfTypeTests(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    #region Basic of-type Search Tests

    /// <summary>
    /// Tests basic identifier:of-type search with Medical Record Number (MR).
    /// Format: identifier:of-type=http://terminology.hl7.org/CodeSystem/v2-0203|MR|12345
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenPatientsWithTypedIdentifiers_WhenSearchingByOfTypeMedicalRecord_ThenReturnsOnlyMatchingPatient()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR identifier "12345"
        var patientWithMR = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with different MR identifier
        var patientWithDifferentMR = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("99999", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with same value but different type (SSN)
        var patientWithSSN = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientWithMR, patientWithDifferentMR, patientWithSSN]);

        // Act - Search for MR with value "12345"
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|12345&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with MR type AND value 12345 should be returned");
        results[0].Id.ShouldBe(patientWithMR.Id);

        // Verify the identifier structure
        var identifiers = results[0].MutableNode["identifier"]?.AsArray();
        identifiers.ShouldNotBeNull();
        var mrIdentifier = identifiers!.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == "12345");
        mrIdentifier.ShouldNotBeNull();

        var typeCodings = mrIdentifier?["type"]?["coding"]?.AsArray();
        typeCodings.ShouldNotBeNull();

        var matchingCoding = typeCodings!.FirstOrDefault(c =>
            c != null &&
            c["system"]?.GetValue<string>() == IdentifierTypeSystem &&
            c["code"]?.GetValue<string>() == "MR");
        matchingCoding.ShouldNotBeNull("should have a coding with system and code MR");
    }

    /// <summary>
    /// Tests that identifier:of-type search does NOT match when type is different but value matches.
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenPatientsWithSameValueDifferentType_WhenSearchingByOfType_ThenReturnsOnlyMatchingType()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR identifier "12345"
        var patientWithMR = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with SSN identifier "12345" (same value, different type)
        var patientWithSSN = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTag(tag)
            .Build();

        // Patient with DL identifier "12345" (same value, different type)
        var patientWithDL = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "DL", "Driver's License")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientWithMR, patientWithSSN, patientWithDL]);

        // Act - Search for SSN with value "12345"
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|SS|12345&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with SSN type should be returned");
        results[0].Id.ShouldBe(patientWithSSN.Id);
        results.ShouldNotContain(r => r.Id == patientWithMR.Id, "MR type should not match SSN search");
        results.ShouldNotContain(r => r.Id == patientWithDL.Id, "DL type should not match SSN search");
    }

    /// <summary>
    /// Tests that identifier:of-type search does NOT match when type matches but value is different.
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenPatientsWithSameTypeDifferentValue_WhenSearchingByOfType_ThenReturnsOnlyMatchingValue()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR identifier "12345"
        var patientWithMR12345 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with MR identifier "67890" (same type, different value)
        var patientWithMR67890 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("67890", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with MR identifier "ABCDE" (same type, different value)
        var patientWithMRABCDE = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("ABCDE", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientWithMR12345, patientWithMR67890, patientWithMRABCDE]);

        // Act - Search for MR with value "67890"
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|67890&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only the patient with MR value 67890 should be returned");
        results[0].Id.ShouldBe(patientWithMR67890.Id);
        results.ShouldNotContain(r => r.Id == patientWithMR12345.Id, "different value should not match");
        results.ShouldNotContain(r => r.Id == patientWithMRABCDE.Id, "different value should not match");
    }

    #endregion

    #region Multiple Identifier Types Tests

    /// <summary>
    /// Tests patient with multiple typed identifiers (MR, SSN, DL).
    /// Search should match only when BOTH type and value match.
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenPatientWithMultipleTypedIdentifiers_WhenSearchingByOfType_ThenReturnsCorrectMatches()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with three different typed identifiers
        var patientMultiple = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("MR-12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTypedIdentifier("123-45-6789", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTypedIdentifier("DL-ABC123", IdentifierTypeSystem, "DL", "Driver's License")
            .WithTag(tag)
            .Build();

        // Decoy patient with only MR
        var patientMROnly = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("MR-99999", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientMultiple, patientMROnly]);

        // Act 1 - Search for MR
        var mrResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|MR-12345&_tag={tag}");

        // Assert 1
        mrResults.Length.ShouldBe(1, "patient with MR-12345 should be found");
        mrResults[0].Id.ShouldBe(patientMultiple.Id);

        // Act 2 - Search for SSN
        var ssnResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|SS|123-45-6789&_tag={tag}");

        // Assert 2
        ssnResults.Length.ShouldBe(1, "patient with SSN 123-45-6789 should be found");
        ssnResults[0].Id.ShouldBe(patientMultiple.Id);

        // Act 3 - Search for DL
        var dlResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|DL|DL-ABC123&_tag={tag}");

        // Assert 3
        dlResults.Length.ShouldBe(1, "patient with DL DL-ABC123 should be found");
        dlResults[0].Id.ShouldBe(patientMultiple.Id);

        // Act 4 - Search for wrong value with correct type
        var wrongValueResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|WRONG-VALUE&_tag={tag}");

        // Assert 4
        wrongValueResults.ShouldBeEmpty("wrong value should not match");
    }

    #endregion

    #region OR Logic Tests

    /// <summary>
    /// Tests OR logic with multiple identifier:of-type values.
    /// Format: identifier:of-type=system|code1|value1,system|code2|value2
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenMultipleOfTypeValues_WhenSearchingWithORLogic_ThenReturnsAllMatches()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR "12345"
        var patient1 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with MR "67890"
        var patient2 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("67890", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with MR "ABCDE" (should NOT match)
        var patient3 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("ABCDE", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with SSN "12345" (should NOT match - different type)
        var patient4 = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient1, patient2, patient3, patient4]);

        // Act - Search for MR with value "12345" OR "67890" (comma-separated OR)
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|12345,{IdentifierTypeSystem}|MR|67890&_tag={tag}");

        // Assert
        results.Length.ShouldBe(2, "both patients with MR 12345 and 67890 should be returned");
        results.ShouldContain(r => r.Id == patient1.Id, "patient with MR 12345 should be included");
        results.ShouldContain(r => r.Id == patient2.Id, "patient with MR 67890 should be included");
        results.ShouldNotContain(r => r.Id == patient3.Id, "patient with MR ABCDE should not be included");
        results.ShouldNotContain(r => r.Id == patient4.Id, "patient with SSN 12345 should not be included");
    }

    /// <summary>
    /// Tests OR logic with different identifier types.
    /// Search for MR|12345 OR SS|67890
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenMultipleOfTypeValuesWithDifferentTypes_WhenSearchingWithORLogic_ThenReturnsAllMatches()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR "12345"
        var patientMR = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with SSN "67890"
        var patientSSN = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("67890", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTag(tag)
            .Build();

        // Patient with DL "99999" (should NOT match)
        var patientDL = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("99999", IdentifierTypeSystem, "DL", "Driver's License")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientMR, patientSSN, patientDL]);

        // Act - Search for MR|12345 OR SS|67890
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|12345,{IdentifierTypeSystem}|SS|67890&_tag={tag}");

        // Assert
        results.Length.ShouldBe(2, "both MR and SSN patients should be returned");
        results.ShouldContain(r => r.Id == patientMR.Id, "patient with MR 12345 should be included");
        results.ShouldContain(r => r.Id == patientSSN.Id, "patient with SSN 67890 should be included");
        results.ShouldNotContain(r => r.Id == patientDL.Id, "patient with DL should not be included");
    }

    #endregion

    #region Missing Type System Tests

    /// <summary>
    /// Tests identifier:of-type with missing type system (just code|value).
    /// Per FHIR spec, system can be omitted: identifier:of-type=|MR|12345
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenOfTypeSearchWithoutTypeSystem_WhenSearching_ThenMatchesOnCodeAndValueOnly()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with MR identifier using standard v2-0203 system
        var patientStandardSystem = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with MR identifier using custom system
        var patientCustomSystem = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", "http://example.org/identifier-types", "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with different type code
        var patientDifferentCode = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "SS", "Social Security Number")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientStandardSystem, patientCustomSystem, patientDifferentCode]);

        // Act - Search without type system (should match both MR identifiers regardless of system)
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type=|MR|12345&_tag={tag}");

        // Assert
        results.Length.ShouldBe(2, "both MR identifiers should match regardless of system");
        results.ShouldContain(r => r.Id == patientStandardSystem.Id);
        results.ShouldContain(r => r.Id == patientCustomSystem.Id);
        results.ShouldNotContain(r => r.Id == patientDifferentCode.Id, "different type code should not match");
    }

    #endregion

    #region Common Identifier Types Tests

    /// <summary>
    /// Tests common identifier types from FHIR v2-0203 code system.
    /// MR = Medical Record, SS = Social Security, DL = Driver's License, PPN = Passport, EN = Employer Number
    /// </summary>
    [Theory(Skip = "Not implemented")]
    [InlineData("MR", "Medical Record", "MRN-12345")]
    [InlineData("SS", "Social Security Number", "123-45-6789")]
    [InlineData("DL", "Driver's License", "DL-ABC123")]
    [InlineData("PPN", "Passport Number", "US-123456789")]
    [InlineData("EN", "Employer Number", "EIN-12-3456789")]
    public async Task GivenCommonIdentifierTypes_WhenSearchingByOfType_ThenReturnsCorrectPatient(
        string typeCode,
        string typeDisplay,
        string identifierValue)
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with specified identifier type
        var patient = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier(identifierValue, IdentifierTypeSystem, typeCode, typeDisplay)
            .WithTag(tag)
            .Build();

        // Decoy patient with different type
        var decoyPatient = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("DECOY-VALUE", IdentifierTypeSystem, "OTHER", "Other")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patient, decoyPatient]);

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|{typeCode}|{identifierValue}&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, $"only patient with {typeCode} type should be returned");
        results[0].Id.ShouldBe(patient.Id);

        // Verify identifier structure
        var identifiers = results[0].MutableNode["identifier"]?.AsArray();
        identifiers.ShouldNotBeNull();
        var matchedIdentifier = identifiers!.FirstOrDefault(i =>
            i?["value"]?.GetValue<string>() == identifierValue);
        matchedIdentifier.ShouldNotBeNull();

        var typeCodings = matchedIdentifier?["type"]?["coding"]?.AsArray();
        typeCodings.ShouldNotBeNull();

        var matchingCoding = typeCodings!.FirstOrDefault(c =>
            c != null && c["code"]?.GetValue<string>() == typeCode);
        matchingCoding.ShouldNotBeNull($"should have a coding with code {typeCode}");
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Tests that patient with no typed identifiers is NOT returned.
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenPatientWithoutTypedIdentifiers_WhenSearchingByOfType_ThenPatientNotReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with typed identifier
        var patientWithType = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("12345", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with identifier but NO type
        var patientWithoutType = CreatePatient()
            .FromSeattle()
            .WithTag(tag)
            .Build();

        // Add plain identifier without type
        patientWithoutType.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://hospital.example.org/identifiers",
                ["value"] = "12345"
            }
        };

        await Harness.CreateResourcesAsync([patientWithType, patientWithoutType]);

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|12345&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1, "only patient with typed identifier should be returned");
        results[0].Id.ShouldBe(patientWithType.Id);
        results.ShouldNotContain(r => r.Id == patientWithoutType.Id,
            "patient with plain identifier (no type) should not match");
    }

    /// <summary>
    /// Tests case sensitivity of identifier value matching.
    /// Per FHIR spec, token values are case-sensitive.
    /// </summary>
    [Fact(Skip = "Not implemented")]
    public async Task GivenIdentifierWithDifferentCase_WhenSearchingByOfType_ThenSearchIsCaseSensitive()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Patient with lowercase identifier
        var patientLower = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("abc123", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        // Patient with uppercase identifier
        var patientUpper = CreatePatient()
            .FromSeattle()
            .WithTypedIdentifier("ABC123", IdentifierTypeSystem, "MR", "Medical Record")
            .WithTag(tag)
            .Build();

        await Harness.CreateResourcesAsync([patientLower, patientUpper]);

        // Act - Search for lowercase
        var lowerResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|abc123&_tag={tag}");

        // Assert - Only lowercase should match
        lowerResults.Length.ShouldBe(1, "search should be case-sensitive");
        lowerResults[0].Id.ShouldBe(patientLower.Id);

        // Act - Search for uppercase
        var upperResults = await Harness.SearchAsync("Patient",
            $"identifier:of-type={IdentifierTypeSystem}|MR|ABC123&_tag={tag}");

        // Assert - Only uppercase should match
        upperResults.Length.ShouldBe(1, "search should be case-sensitive");
        upperResults[0].Id.ShouldBe(patientUpper.Id);
    }

    #endregion
}
