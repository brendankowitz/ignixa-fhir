// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for token overflow handling in search parameters.
/// Tests FHIR token search with values exceeding database column limits (450+ characters).
/// Validates that overflow tokens are correctly indexed and searchable.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.TokenOverflowTests
/// </summary>
/// <remarks>
/// Token Overflow Context:
/// Database columns for token values typically have a maximum length (e.g., 450 characters).
/// When token values exceed this limit, they are stored with an "overflow" indicator.
/// These tests verify that:
/// - Overflow tokens are correctly indexed and searchable
/// - Searches for overflow tokens return correct results
/// - Composite searches with overflow tokens work properly
///
/// Test Data:
/// PatientA: identifier with overflow (500+ chars, prefix "A"), birthdate: 1990-01-15
/// PatientB: identifier with overflow (500+ chars, prefix "B"), birthdate: 1985-06-20
/// PatientC: identifier at max length (450 chars, prefix "C"), birthdate: 1992-03-10
/// PatientD: identifier short (350 chars, prefix "D"), birthdate: 1988-11-05
///
/// Note: If token overflow is not implemented, these tests will be skipped.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class TokenOverflowTests : CapabilityDrivenTestBase, IClassFixture<TokenOverflowFixture>
{
    private readonly TokenOverflowFixture _fixture;

    public TokenOverflowTests(IgnixaApiFixture apiFixture, TokenOverflowFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests basic token search with overflow values.
    /// Verifies that resources with identifiers exceeding max token length are searchable.
    /// Ported from: TokenOverflowTests.GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByToken_VerifyCorrectSerachResults
    /// </summary>
    [Theory]
    [InlineData(0)] // PatientA - overflow
    [InlineData(1)] // PatientB - overflow
    [InlineData(2)] // PatientC - max no overflow
    [InlineData(3)] // PatientD - short no overflow
    public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByToken_ThenCorrectResultsReturned(int patientIndex)
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange
        var patient = _fixture.Patients[patientIndex];
        var identifierValue = patientIndex switch
        {
            0 => _fixture.IdentifierA,
            1 => _fixture.IdentifierB,
            2 => _fixture.IdentifierC,
            3 => _fixture.IdentifierD,
            _ => throw new ArgumentOutOfRangeException(nameof(patientIndex))
        };

        // Act - Search by exact identifier value
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&identifier={TokenOverflowFixture.IdentifierSystem}|{identifierValue}");

        // Assert - Should find exactly this patient
        results.Length.ShouldBe(1, $"Should find exactly 1 patient with identifier at index {patientIndex}");
        results[0].Id.ShouldBe(patient.Id, $"Found patient should match expected patient {patient.Id}");
    }

    /// <summary>
    /// Tests composite token-string search with token overflow.
    /// Verifies composite search (identifier + family name) works with overflow tokens.
    /// Ported from: TokenOverflowTests.TokenString
    /// </summary>
    [Theory]
    [InlineData(0, "TestA")] // PatientA overflow + family name
    [InlineData(1, "TestB")] // PatientB overflow + family name
    [InlineData(2, "TestC")] // PatientC max + family name
    [InlineData(3, "TestD")] // PatientD short + family name
    public async Task GivenTokenOverflowAndString_WhenCompositeSearched_ThenCorrectResultsReturned(int patientIndex, string familyName)
    {
        // Capability check
        RequireSearchParameters("Patient", "identifier", "family");

        // Arrange
        var patient = _fixture.Patients[patientIndex];
        var identifierValue = patientIndex switch
        {
            0 => _fixture.IdentifierA,
            1 => _fixture.IdentifierB,
            2 => _fixture.IdentifierC,
            3 => _fixture.IdentifierD,
            _ => throw new ArgumentOutOfRangeException(nameof(patientIndex))
        };

        // Act - Search by identifier AND family name
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&identifier={TokenOverflowFixture.IdentifierSystem}|{identifierValue}&family={familyName}");

        // Assert
        results.Length.ShouldBe(1, "Should find exactly 1 patient matching both identifier and family name");
        results[0].Id.ShouldBe(patient.Id);
    }

    /// <summary>
    /// Tests composite token-datetime search with token overflow.
    /// Verifies composite search (identifier + birthdate) works with overflow tokens.
    /// Ported from: TokenOverflowTests.TokenDateTime
    /// </summary>
    [Fact]
    public async Task GivenTokenOverflowAndDateTime_WhenCompositeSearched_ThenCorrectResultsReturned()
    {
        // Capability check
        RequireSearchParameters("Patient", "identifier", "birthdate");

        // Arrange
        var patientA = _fixture.Patients[0];

        // Act - Search by identifier AND birthdate
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&identifier={TokenOverflowFixture.IdentifierSystem}|{_fixture.IdentifierA}&birthdate=1990-01-15");

        // Assert
        results.Length.ShouldBe(1, "Should find exactly 1 patient matching both identifier and birthdate");
        results[0].Id.ShouldBe(patientA.Id);
    }

    /// <summary>
    /// Tests composite search with token overflow + token (both tokens overflow).
    /// Verifies that when both components of a composite search have overflow, results are correct.
    /// Ported from: TokenOverflowTests.TokenOverflowToken
    /// </summary>
    [Fact(Skip = "Requires custom SearchParameter support - not in base FHIR spec. See: custom-parameters investigation.")]
    public async Task GivenTokenOverflowAndToken_WhenBothOverflow_ThenCorrectResultsReturned()
    {
        // BLOCKED BY: Custom SearchParameter management (POST SearchParameter + $reindex)
        // This composite (token+token on identifier+telecom) is NOT in base FHIR R4 spec.
        // Microsoft test uses: CompositeCustomTokenOverflowTokenSearchParameter.json
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests composite search with token + token overflow (first token normal, second overflow).
    /// Ported from: TokenOverflowTests.TokenTokenOverflow
    /// </summary>
    [Fact(Skip = "Requires custom SearchParameter support - not in base FHIR spec. See: custom-parameters investigation.")]
    public async Task GivenTokenAndTokenOverflow_WhenFirstNormalSecondOverflow_ThenCorrectResultsReturned()
    {
        // BLOCKED BY: Custom SearchParameter management (POST SearchParameter + $reindex)
        // This composite (token+token on telecom+identifier) is NOT in base FHIR R4 spec.
        // Microsoft test uses: CompositeCustomTokenTokenOverflowSearchParameter.json
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests composite reference-token search with token overflow.
    /// Verifies that reference + overflow token composite searches work correctly.
    /// Ported from: TokenOverflowTests.ReferenceToken
    /// </summary>
    [Fact(Skip = "Requires custom SearchParameter support - not in base FHIR spec. See: custom-parameters investigation.")]
    public async Task GivenReferenceAndTokenOverflow_WhenCompositeSearched_ThenCorrectResultsReturned()
    {
        // BLOCKED BY: Custom SearchParameter management (POST SearchParameter + $reindex)
        // This composite (reference+token on managingOrganization+identifier) is NOT in base FHIR R4 spec.
        // Microsoft test uses: CompositeCustomReferenceTokenSearchParameter.json
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests composite token-quantity search with token overflow.
    /// Uses Observation or similar resource type with coded values and quantities.
    /// Ported from: TokenOverflowTests.TokenQuantity
    /// </summary>
    [Fact(Skip = "Requires custom SearchParameter support - ChargeItem not in base spec. See: custom-parameters investigation.")]
    public async Task GivenTokenOverflowAndQuantity_WhenCompositeSearched_ThenCorrectResultsReturned()
    {
        // BLOCKED BY: Custom SearchParameter management (POST SearchParameter + $reindex)
        // This composite (token+quantity on ChargeItem identifier+quantity) is NOT in base FHIR R4 spec.
        // Microsoft test uses: CompositeCustomTokenQuantitySearchParameter.json on ChargeItem resource
        // Note: Observation code-value-quantity IS in base spec but uses code, not identifier overflow
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests composite token-number-number search with token overflow.
    /// Uses RiskAssessment or similar resource with multiple numeric components.
    /// Ported from: TokenOverflowTests.TokenNumberNumber
    /// </summary>
    [Fact(Skip = "Requires custom SearchParameter support - RiskAssessment 3-component composite not in base spec. See: custom-parameters investigation.")]
    public async Task GivenTokenOverflowAndNumberAndNumber_WhenCompositeSearched_ThenCorrectResultsReturned()
    {
        // BLOCKED BY: Custom SearchParameter management (POST SearchParameter + $reindex)
        // This composite (token+number+number on RiskAssessment) is NOT in base FHIR R4 spec.
        // Microsoft test uses: CompositeCustomTokenNumberNumberSearchParameter.json
        // Components: identifier + prediction[0].probability + prediction[1].probability
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tests that searching for a non-existent overflow token returns no results.
    /// </summary>
    [Fact]
    public async Task GivenNonExistentOverflowToken_WhenSearched_ThenNoResultsReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Arrange - Create a token value that doesn't exist (but is overflow length)
        var nonExistentValue = "Z".PadRight(500, 'z');

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&identifier={TokenOverflowFixture.IdentifierSystem}|{nonExistentValue}");

        // Assert
        results.ShouldBeEmpty("Should not find any patients with non-existent overflow identifier");
    }

    /// <summary>
    /// Tests that searching by system only returns all patients in fixture (overflow and non-overflow).
    /// </summary>
    [Fact]
    public async Task GivenTokenSystemOnly_WhenSearched_ThenAllPatientsReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "identifier");

        // Act - Search by system only (should match all patients)
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&identifier={TokenOverflowFixture.IdentifierSystem}|");

        // Assert - Should find all 4 patients
        results.Length.ShouldBe(4, "Should find all patients with the same identifier system");

        // Verify all expected patients are in results
        foreach (var patient in _fixture.Patients)
        {
            results.ShouldContain(r => r.Id == patient.Id,
                $"Expected patient {patient.Id} should be in results");
        }
    }
}
