// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for token search parameters.
/// Tests token search with various system|code|text patterns, modifiers (:text, :not),
/// and edge cases like case sensitivity and missing values.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.TokenSearchTests
/// </summary>
[Collection(E2ETestCollection.Name)]
public class TokenSearchTests : CapabilityDrivenTestBase, IClassFixture<TokenSearchTestFixture>
{
    private readonly TokenSearchTestFixture _fixture;

    public TokenSearchTests(IgnixaApiFixture apiFixture, TokenSearchTestFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Test data for basic token search scenarios.
    /// Format: [queryValue, expectedIndices...]
    /// Maps to fixture.Observations array indices.
    /// </summary>
    public static readonly object[][] TokenSearchParameterData =
    [
        ["a"],                                    // Empty result (no observations match)
        ["code1", 0, 5, 6],                      // Matches obs 0, 5, 6
        ["code3", 4, 6, 7],                      // Matches obs 4, 6, 7
        ["a|b"],                                 // Empty (no system 'a')
        ["system2|code2", 1],                    // Exact system+code match
        ["|code2"],                              // Any system with code2 (obs 1 has system2|code2)
        ["|code3", 7],                           // Any system with code3 (obs 7 has no system)
        ["a|"],                                  // Empty (system 'a' with any code)
        ["system3|", 4, 5, 6],                   // System3 with any code
        ["code1,system2|code2", 0, 1, 5, 6]     // OR: code1 OR system2|code2
    ];

    /// <summary>
    /// Tests basic token search parameter with various system|code patterns.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameter_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [MemberData(nameof(TokenSearchParameterData))]
    public async Task GivenATokenSearchParameter_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "value-concept");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-concept={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        // Verify each expected observation is in results (order may vary)
        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} (index in test data) should be in results");
        }
    }

    /// <summary>
    /// Tests token search with :text modifier for searching in display/text fields.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameterWithTextModifier_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("code1")]                        // No matches (code1 not in text)
    [InlineData("text", 2, 3, 4, 5, 6)]         // Matches obs with "text" in text or display
    [InlineData("text2", 3, 6)]                 // Matches obs with "text2" in display
    public async Task GivenATokenSearchParameterWithTextModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "value-concept");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-concept:text={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results for :text search");
        }
    }

    /// <summary>
    /// Tests token search with :not modifier to exclude matching resources.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameterWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [MemberData(nameof(TokenSearchParameterData))]
    public async Task GivenATokenSearchParameterWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "value-concept");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-concept:not={queryValue}");

        // Assert
        // Expected: all observations except those in excludeIndices
        var expectedObservations = _fixture.Observations
            .Where((_, i) => !excludeIndices.Contains(i))
            .ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results (not excluded)");
        }

        // Verify excluded observations are NOT in results
        foreach (var excludeIndex in excludeIndices)
        {
            var excludedObs = _fixture.Observations[excludeIndex];
            results.Should().NotContain(r => r.Id == excludedObs.Id,
                $"Observation {excludedObs.Id} (index {excludeIndex}) should be excluded by :not modifier");
        }
    }

    /// <summary>
    /// Tests :not modifier when searching over resources with missing values.
    /// Resources without the searched parameter should be excluded.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameterWithNotModifier_WhenSearchedOverMissingValue_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedOverMissingValue_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Observation", "category");

        // Act - Search for observations where category is NOT "test"
        // Observation[8] has category=system|test, so it should be excluded
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&category:not=test");

        // Assert - All observations except [8]
        var expectedObservations = _fixture.Observations
            .Where((_, i) => i != 8)
            .ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }

        // Verify observation[8] is excluded
        var excludedObs = _fixture.Observations[8];
        results.Should().NotContain(r => r.Id == excludedObs.Id,
            "Observation[8] with category=test should be excluded by :not modifier");
    }

    /// <summary>
    /// Tests multiple token search parameters with :not modifiers (AND logic).
    /// Ported from: TokenSearchTests.GivenMultipleTokenSearchParametersWithNotModifiers_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("code1", 0, 5, 6, 8)]
    public async Task GivenMultipleTokenSearchParametersWithNotModifiers_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
    {
        // Capability check
        RequireSearchParameters("Observation", "category", "value-concept");

        // Act - Search with multiple :not parameters (AND logic)
        // category:not=test excludes [8]
        // value-concept:not=code1 excludes [0, 5, 6]
        // Combined: excludes [0, 5, 6, 8]
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&category:not=test&value-concept:not={queryValue}");

        // Assert
        var expectedObservations = _fixture.Observations
            .Where((_, i) => !excludeIndices.Contains(i))
            .ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }

        // Verify excluded observations are NOT in results
        foreach (var excludeIndex in excludeIndices)
        {
            var excludedObs = _fixture.Observations[excludeIndex];
            results.Should().NotContain(r => r.Id == excludedObs.Id,
                $"Observation {excludedObs.Id} (index {excludeIndex}) should be excluded");
        }
    }

    /// <summary>
    /// Tests _id parameter with :not modifier.
    /// Ported from: TokenSearchTests.GivenIdWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenIdWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(int count)
    {
        // Act - Exclude first 'count' observations by ID
        var idsToExclude = string.Join(",", _fixture.Observations.Take(count).Select(x => x.Id));
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&_id:not={idsToExclude}");

        // Assert - Should return all observations except first 'count'
        var expectedObservations = _fixture.Observations.Skip(count).ToArray();

        results.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            results.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }

        // Verify excluded observations are NOT in results
        foreach (var excludedObs in _fixture.Observations.Take(count))
        {
            results.Should().NotContain(r => r.Id == excludedObs.Id,
                $"Observation {excludedObs.Id} should be excluded by _id:not");
        }
    }

    /// <summary>
    /// Tests _type parameter with :not modifier for cross-resource-type searches.
    /// Ported from: TokenSearchTests.GivenTypeWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("Patient")]
    [InlineData("Patient,Organization")]
    public async Task GivenTypeWithNotModifier_WhenSearched_ThenCorrectBundleShouldBeReturned(string resourceTypes)
    {
        // Act - System-level search excluding specified resource types
        var bundle = await Harness.SearchSystemAsync($"_tag={_fixture.Tag}&_type:not={resourceTypes}");

        // Assert - Should only return Observation resources (our test data)
        var resources = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();

        resources.Should().HaveCount(_fixture.Observations.Count);
        resources.Should().AllSatisfy(r => r.ResourceType.Should().Be("Observation"));

        // Verify all our observations are in results
        foreach (var expected in _fixture.Observations)
        {
            resources.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }
    }

    /// <summary>
    /// Tests token search with :not modifier combined with _type parameter.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameterWithNotModifier_WhenSearchedWithType_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [MemberData(nameof(TokenSearchParameterData))]
    public async Task GivenATokenSearchParameterWithNotModifier_WhenSearchedWithType_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] excludeIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "value-concept");

        // Act - System-level search with _type and :not modifier
        var bundle = await Harness.SearchSystemAsync($"_tag={_fixture.Tag}&_type=Observation&value-concept:not={queryValue}");

        // Assert
        var resources = bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();

        var expectedObservations = _fixture.Observations
            .Where((_, i) => !excludeIndices.Contains(i))
            .ToArray();

        resources.Should().HaveCount(expectedObservations.Length);

        foreach (var expected in expectedObservations)
        {
            resources.Should().Contain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }

        // Verify excluded observations are NOT in results
        foreach (var excludeIndex in excludeIndices)
        {
            var excludedObs = _fixture.Observations[excludeIndex];
            resources.Should().NotContain(r => r.Id == excludedObs.Id,
                $"Observation {excludedObs.Id} (index {excludeIndex}) should be excluded");
        }
    }

    /// <summary>
    /// Tests case-insensitive token search.
    /// Token values that differ only in case should both match.
    /// Ported from: TokenSearchTests.GivenATokenSearchParameterWithTwoValuesThatOnlyDifferInCase_WhenSearchedByEitherValue_ThenTheResourceWillBeReturned
    /// </summary>
    [Theory]
    [InlineData("VALUE")]
    [InlineData("value")]
    public async Task GivenATokenSearchParameterWithTwoValuesThatOnlyDifferInCase_WhenSearchedByEitherValue_ThenTheResourceWillBeReturned(string queryValue)
    {
        // Capability check
        RequireSearchParameter("Observation", "identifier");

        // Act - Search for identifier value (case-insensitive)
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&identifier={queryValue}");

        // Assert - Should find observation[9] which has both "VALUE" and "value" identifiers
        results.Should().HaveCount(1, "observation[9] has both case variants");

        var matchedObs = results.First();
        matchedObs.Id.Should().Be(_fixture.Observations[9].Id);

        // Verify the observation has identifiers with the expected values
        var identifiers = matchedObs.MutableNode["identifier"];
        identifiers.Should().NotBeNull();
    }
}
