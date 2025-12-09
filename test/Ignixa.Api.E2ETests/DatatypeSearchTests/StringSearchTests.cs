// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;

namespace Ignixa.Api.E2ETests.DatatypeSearchTests;

/// <summary>
/// E2E tests for string search parameters.
/// Tests string search with modifiers (:exact, :contains), case sensitivity,
/// accent-insensitive matching (SQL Server only), and escaped commas.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.StringSearchTests
/// </summary>
[Collection(E2ETestCollection.Name)]
public class StringSearchTests : CapabilityDrivenTestBase, IClassFixture<StringSearchTestFixture>
{
    private readonly StringSearchTestFixture _fixture;

    public StringSearchTests(IgnixaApiFixture apiFixture, StringSearchTestFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests basic string search parameter with various modifiers and case sensitivity.
    /// String search semantics:
    /// - No modifier: starts-with, case-insensitive
    /// - :exact: exact match, case-sensitive
    /// - :contains: substring match, case-insensitive
    /// Ported from: StringSearchTests.GivenAStringSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("", "seattle", true)]           // No modifier: starts-with, case-insensitive
    [InlineData("", "SEATTLE", true)]
    [InlineData("", "Seattle", true)]
    [InlineData("", "Sea", true)]
    [InlineData("", "sea", true)]
    [InlineData("", "123", false)]
    [InlineData(":exact", "Seattle", true)]     // :exact: case-sensitive
    [InlineData(":exact", "seattle", false)]
    [InlineData(":exact", "SEATTLE", false)]
    [InlineData(":exact", "Sea", false)]
    [InlineData(":contains", "att", true)]      // :contains: substring, case-insensitive
    [InlineData(":contains", "EAT", true)]
    [InlineData(":contains", "123", false)]
    public async Task GivenAStringSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string modifier,
        string valueToSearch,
        bool shouldMatch)
    {
        // Capability check
        RequireSearchParameter("Patient", "address-city");

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&address-city{modifier}={valueToSearch}");

        // Assert
        var expectedPatient = _fixture.Patients[0]; // Patient 0 has city "Seattle"

        if (shouldMatch)
        {
            results.Should().ContainSingle(r => r.Id == expectedPatient.Id,
                $"Patient 0 (Seattle) should match query 'address-city{modifier}={valueToSearch}'");
        }
        else
        {
            results.Should().NotContain(r => r.Id == expectedPatient.Id,
                $"Patient 0 (Seattle) should NOT match query 'address-city{modifier}={valueToSearch}'");
        }
    }

    /// <summary>
    /// Tests string search with long values (500+ characters).
    /// Verifies that long string values are properly indexed and searchable.
    /// Ported from: StringSearchTests.GivenAStringSearchParamAndAResourceWithALongSearchParamValue_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("", "Lorem", true)]                                     // Starts-with
    [InlineData("", "NotLorem", false)]
    [InlineData("", StringSearchTestFixture.LongString, true)]          // Full string starts-with
    [InlineData("", "Not" + StringSearchTestFixture.LongString, false)]
    [InlineData(":exact", StringSearchTestFixture.LongString, true)]    // Exact match
    [InlineData(":exact", StringSearchTestFixture.LongString + "Not", false)]
    [InlineData(":contains", StringSearchTestFixture.LongString, true)] // Contains full string
    [InlineData(":contains", StringSearchTestFixture.LongString + "Not", false)]
    [InlineData(":contains", "Vestibulum", true)]                       // Contains substring
    [InlineData(":contains", "NotInString", false)]
    public async Task GivenAStringSearchParamAndAResourceWithALongSearchParamValue_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string modifier,
        string valueToSearch,
        bool shouldMatch)
    {
        // Capability check
        RequireSearchParameter("Patient", "address-city");

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&address-city{modifier}={valueToSearch}");

        // Assert
        var expectedPatient = _fixture.Patients[3]; // Patient 3 has long city name

        if (shouldMatch)
        {
            results.Should().ContainSingle(r => r.Id == expectedPatient.Id,
                $"Patient 3 (long city) should match query 'address-city{modifier}={valueToSearch.Substring(0, Math.Min(20, valueToSearch.Length))}...'");
        }
        else
        {
            results.Should().NotContain(r => r.Id == expectedPatient.Id,
                $"Patient 3 (long city) should NOT match query 'address-city{modifier}={valueToSearch.Substring(0, Math.Min(20, valueToSearch.Length))}...'");
        }
    }

    /// <summary>
    /// Tests string search with multiple values (OR logic).
    /// Multiple comma-separated values should return resources matching ANY of the values.
    /// Ported from: StringSearchTests.GivenAStringSearchParamWithMultipleValues_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAStringSearchParamWithMultipleValues_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "family");

        // Act: Search for "Smith" OR "Ander" (starts-with, so matches "Anderson")
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&family=Smith,Ander");

        // Assert: Should return Patients[0] (Smith) and Patients[2] (Anderson)
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Id == _fixture.Patients[0].Id,
            "Patient 0 (Smith) should match");
        results.Should().Contain(r => r.Id == _fixture.Patients[2].Id,
            "Patient 2 (Anderson) should match starts-with 'Ander'");
    }

    /// <summary>
    /// Tests string search parameter that covers multiple fields with AND logic.
    /// When the same parameter is specified twice, results should be the intersection.
    /// Ported from: StringSearchTests.GivenAStringSearchParamThatCoversSeveralFields_WhenSpecifiedTwiceInASearch_IntersectsTheTwoResultsProperly
    /// </summary>
    [Fact]
    public async Task GivenAStringSearchParamThatCoversSeveralFields_WhenSpecifiedTwiceInASearch_IntersectsTheTwoResultsProperly()
    {
        // Capability check
        RequireSearchParameter("Patient", "name");

        // Act: Search for patients with BOTH "Bea" AND "Smith" in name fields
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&name=Bea&name=Smith");

        // Assert: Should return only Patient[0] (has both "Bea" given name and "Smith" family)
        // Patient[4] has "Bea" but family is "Richard", so it should NOT match
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[0].Id,
            "Only Patient 0 has both 'Bea' and 'Smith' in name fields");
    }

    /// <summary>
    /// Tests accent-insensitive string search (SQL Server only).
    /// Both "muller" and "müller" should match patients with either spelling.
    /// Ported from: StringSearchTests.GivenAStringSearchParamWithAccentAndAResourceWithAccent_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Theory]
    [InlineData("muller")]
    [InlineData("müller")]
    public async Task GivenAStringSearchParamWithAccentAndAResourceWithAccent_WhenSearched_ThenCorrectBundleShouldBeReturned(string searchText)
    {
        // This test only works on SQL Server due to collation settings
        // Skip if using FileSystem storage (TEST_USE_FILESYSTEM=true)
        if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new SkipException("Accent-insensitive search only supported on SQL Server");
        }

        // Capability check
        RequireSearchParameter("Patient", "name");

        // Act
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&name={searchText}");

        // Assert: Both Patients[5] (Muller) and Patients[6] (Müller) should match
        results.Should().HaveCount(2,
            "Both 'Muller' and 'Müller' patients should match accent-insensitive search");
        results.Should().Contain(r => r.Id == _fixture.Patients[5].Id,
            "Patient 5 (Muller) should match");
        results.Should().Contain(r => r.Id == _fixture.Patients[6].Id,
            "Patient 6 (Müller) should match");
    }

    /// <summary>
    /// Tests escaped comma in string search parameters.
    /// "\," should be treated as a literal comma, not an OR separator.
    /// Ported from: StringSearchTests.GivenAEscapedStringSearchParams_WhenSearched_ThenCorrectBundleShouldBeReturned
    /// </summary>
    [Fact]
    public async Task GivenAEscapedStringSearchParams_WhenSearched_ThenCorrectBundleShouldBeReturned()
    {
        // Capability check
        RequireSearchParameter("Patient", "name");

        // Act: Search for exact family name "Richard,Muller" (with comma)
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&name=Richard\\,Muller");

        // Assert: Should return only Patient[7] with family "Richard,Muller"
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[7].Id,
            "Patient 7 has family name 'Richard,Muller' (literal comma)");
    }
}
