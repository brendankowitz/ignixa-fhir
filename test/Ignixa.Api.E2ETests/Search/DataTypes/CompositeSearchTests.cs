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
/// E2E tests for composite search parameters.
/// Tests FHIR composite search with code-value-quantity, component-code-value-quantity,
/// code-value-concept (token-token), code-value-string (token-string), and relationship (reference-token) composites.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.CompositeSearchTests
/// </summary>
/// <remarks>
/// FHIR Composite Search (http://hl7.org/fhir/search.html#composite):
/// Composite search parameters combine multiple values with the "$" separator.
/// Format: param=value1$value2 or param=system|code$prefix123
///
/// Common composite types:
/// - code-value-quantity: code (token) $ value (quantity) - e.g., APGAR score
/// - component-code-value-quantity: component code $ component value - e.g., Blood Pressure
/// - code-value-concept: code (token) $ value (token) - e.g., TPMT diplotype
/// - code-value-string: code (token) $ value (string) - e.g., Eye color
/// - relationship: reference $ token - e.g., DocumentReference relatesTo
///
/// Test Data Fixture:
/// Observations:
///   [0] = APGAR 1-minute score=10
///   [1] = APGAR 1-minute score=20
///   [2] = APGAR 20-minute score=10
///   [3] = Body Temperature 100 Cel
///   [4] = TPMT diplotype *1/*1
///   [5] = Blood Pressure systolic=107 diastolic=60
///   [6] = Eye color blue
///   [7] = Eye color hazel (long text)
///
/// DocumentReferences:
///   [0] = relatesTo: replaces document1
///   [1] = relatesTo: transforms document2
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class CompositeSearchTests : CapabilityDrivenTestBase, IClassFixture<CompositeSearchFixture>
{
    private readonly CompositeSearchFixture _fixture;

    public CompositeSearchTests(IgnixaApiFixture apiFixture, CompositeSearchFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests composite search parameter with token and quantity components (code-value-quantity).
    /// Uses the APGAR score observations as test data.
    /// Ported from: CompositeSearchTests.GivenACompositeSearchParameterWithTokenAndQuantity_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    public static readonly object[][] TokenQuantityData =
    [
        // Basic exact match
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$10", new[] { 0 } },
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$20", new[] { 1 } },

        // Different code, same value
        new object[] { "code-value-quantity", "http://loinc.org|9271-8$10", new[] { 2 } },

        // No match - wrong value
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$30", Array.Empty<int>() },

        // Quantity with comparison operators
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$lt15", new[] { 0 } },
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$gt15", new[] { 1 } },
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$le10", new[] { 0 } },
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$ge20", new[] { 1 } },

        // Multiple parameters (AND logic)
        new object[] { "code-value-quantity", "http://loinc.org|9272-6$ge10&code-value-quantity=http://loinc.org|9272-6$le20", new[] { 0, 1 } },

        // Code without system (just code value)
        new object[] { "code-value-quantity", "9272-6$10", new[] { 0 } },

        // Component-code-value-quantity (Blood Pressure)
        // Tests composite search on component values
        new object[] { "component-code-value-quantity", "http://loinc.org|8480-6$107", new[] { 5 } },  // Systolic BP
        new object[] { "component-code-value-quantity", "http://loinc.org|8462-4$60", new[] { 5 } },   // Diastolic BP
        new object[] { "component-code-value-quantity", "http://loinc.org|8480-6$gt100", new[] { 5 } },
        new object[] { "component-code-value-quantity", "http://loinc.org|8480-6$lt100", Array.Empty<int>() },
    ];

    [Theory]
    [MemberData(nameof(TokenQuantityData))]
    public async Task GivenACompositeSearchParameterWithTokenAndQuantity_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string paramName,
        string queryValue,
        int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", paramName);

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&{paramName}={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Search {paramName}={queryValue} should return {expectedObservations.Length} results");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} (index in test data) should be in results");
        }
    }

    /// <summary>
    /// Tests composite search parameter with two token components (code-value-concept).
    /// Uses TPMT diplotype observation for testing token-token composites.
    /// Ported from: CompositeSearchTests.GivenACompositeSearchParameterWithTokenAndToken_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    public static readonly object[][] TokenTokenData =
    [
        // Exact match
        new object[] { "code-value-concept", "http://loinc.org|79713-3$http://pharmvar.org/haplotype|*1/*1", new[] { 4 } },

        // Code without system in first component
        new object[] { "code-value-concept", "79713-3$http://pharmvar.org/haplotype|*1/*1", new[] { 4 } },

        // Value without system in second component
        new object[] { "code-value-concept", "http://loinc.org|79713-3$*1/*1", new[] { 4 } },

        // No match - wrong value code
        new object[] { "code-value-concept", "http://loinc.org|79713-3$http://pharmvar.org/haplotype|*2/*2", Array.Empty<int>() },

        // No match - wrong observation code
        new object[] { "code-value-concept", "http://loinc.org|wrong-code$http://pharmvar.org/haplotype|*1/*1", Array.Empty<int>() },
    ];

    [Theory]
    [MemberData(nameof(TokenTokenData))]
    public async Task GivenACompositeSearchParameterWithTokenAndToken_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string paramName,
        string queryValue,
        int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", paramName);

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&{paramName}={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Search {paramName}={queryValue} should return {expectedObservations.Length} results");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }
    }

    /// <summary>
    /// Tests composite search parameter with token and string components (code-value-string).
    /// Uses eye color observations for testing token-string composites.
    /// Ported from: CompositeSearchTests.GivenACompositeSearchParameterWithTokenAndString_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    public static readonly object[][] TokenStringData =
    [
        // Exact match
        new object[] { "code-value-string", "http://example.org|eye-color$blue-eyed", new[] { 6 } },

        // Prefix match (FHIR default string search is StartsWith, not Contains)
        // "hazel eyes..." starts with "hazel" so it matches
        new object[] { "code-value-string", "http://example.org|eye-color$hazel", new[] { 7 } },
        // Note: FHIR composite search doesn't support modifiers, so :contains is not available
        // "hazel eyes..." doesn't start with "eyes" so it shouldn't match
        new object[] { "code-value-string", "http://example.org|eye-color$eyes", Array.Empty<int>() },

        // Code without system
        new object[] { "code-value-string", "eye-color$blue-eyed", new[] { 6 } },

        // No match - wrong string
        new object[] { "code-value-string", "http://example.org|eye-color$green", Array.Empty<int>() },

        // No match - wrong code
        new object[] { "code-value-string", "http://example.org|wrong-code$blue-eyed", Array.Empty<int>() },
    ];

    [Theory]
    [MemberData(nameof(TokenStringData))]
    public async Task GivenACompositeSearchParameterWithTokenAndString_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string paramName,
        string queryValue,
        int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", paramName);

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&{paramName}={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Search {paramName}={queryValue} should return {expectedObservations.Length} results");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }
    }

    /// <summary>
    /// Tests composite search parameter with reference and token components.
    /// Uses DocumentReference relatesTo for testing reference-token composites.
    /// Ported from: CompositeSearchTests.GivenACompositeSearchParameterWithTokenAndReference_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    /// <remarks>
    /// NOTE: Microsoft FHIR Server has some tests marked with Skip for GitHub issue #523.
    /// Those tests are included here but may need adjustment based on Ignixa's implementation.
    /// </remarks>
    public static readonly object[][] ReferenceTokenData =
    [
        // The "relationship" search parameter has components: relatesto (Reference), relation (Token)
        // Query format must be: Reference$Token (e.g., DocumentReference/document1$replaces)
        // NOT Token$Reference - the component order must match the search parameter definition

        // Exact match with relatesTo reference and relation code
        new object[] { "relationship", "DocumentReference/document1$replaces", new[] { 0 } },
        new object[] { "relationship", "DocumentReference/document2$transforms", new[] { 1 } },

        // No match - wrong code
        new object[] { "relationship", "DocumentReference/document1$appends", Array.Empty<int>() },

        // No match - wrong reference
        new object[] { "relationship", "DocumentReference/wrong-document$replaces", Array.Empty<int>() },
    ];

    [Theory]
    [MemberData(nameof(ReferenceTokenData))]
    public async Task GivenACompositeSearchParameterWithTokenAndReference_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string paramName,
        string queryValue,
        int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("DocumentReference", paramName);

        // Act
        var results = await Harness.SearchAsync("DocumentReference", $"_tag={_fixture.Tag}&{paramName}={queryValue}");

        // Assert
        var expectedDocuments = expectedIndices.Select(i => _fixture.DocumentReferences[i]).ToArray();

        results.Length.ShouldBe(expectedDocuments.Length,
            $"Search {paramName}={queryValue} should return {expectedDocuments.Length} results");

        foreach (var expected in expectedDocuments)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected document reference {expected.Id} should be in results");
        }
    }

    /// <summary>
    /// Tests that composite search correctly handles missing components.
    /// Composite search requires ALL components to be present in the resource.
    /// </summary>
    [Fact]
    public async Task GivenCompositeSearchWithMissingComponent_WhenSearched_ThenNoResultsReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code-value-quantity");

        // Act - Search for temperature observation with APGAR code (mismatch)
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&code-value-quantity=http://loinc.org|9272-6$100");

        // Assert - Should not match temperature observation (wrong code)
        results.ShouldBeEmpty("Composite search should not match when code component doesn't match");
    }

    /// <summary>
    /// Tests that composite search with invalid format is handled gracefully.
    /// </summary>
    [Fact(Skip = "Error handling for malformed composite queries not yet specified")]
    public async Task GivenMalformedCompositeQuery_WhenSearched_ThenErrorReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "code-value-quantity");

        // Act & Assert - Missing second component should result in error
        var exception = await Should.ThrowAsync<Exception>(async () =>
        {
            await Harness.SearchAsync("Observation",
                $"_tag={_fixture.Tag}&code-value-quantity=http://loinc.org|9272-6$");
        });

        // Error handling specifics depend on implementation
    }
}
