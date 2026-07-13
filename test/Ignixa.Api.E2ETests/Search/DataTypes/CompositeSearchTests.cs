// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;
using Ignixa.FhirFakes.Builders;

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
        // Definition order is Reference$Token (e.g., DocumentReference/document1$replaces). Values whose
        // shape is unambiguous ARE resolved by effective type even when swapped relative to that order -
        // see GivenReferenceTokenCompositeWithValuesSwappedRelativeToDefinition_WhenSearched_ThenResolvesByEffectiveType.
        // Only genuinely ambiguous-shaped values fall back to the definition order or the graceful-empty path.

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
    /// Tests composite search parameter with token and datetime components (code-value-date).
    /// Uses date-valued observations for testing token-datetime composites.
    /// This is the only coverage exercising CompositeSearchParameterQueryGenerator.GenerateTokenDateTimeQueryAsync.
    /// </summary>
    public static readonly object[][] TokenDateTimeData =
    [
        // Exact day match against a valueDateTime observation
        new object[] { $"{CompositeSearchFixture.DateCompositeCodeSystem}|{CompositeSearchFixture.DateSingleCode}$2020-06-15", new[] { 8 } },

        // Code without system - still matches on code alone
        new object[] { $"{CompositeSearchFixture.DateSingleCode}$2020-06-15", new[] { 8 } },

        // No match - wrong date
        new object[] { $"{CompositeSearchFixture.DateCompositeCodeSystem}|{CompositeSearchFixture.DateSingleCode}$2019-01-01", Array.Empty<int>() },

        // No match - wrong code
        new object[] { $"{CompositeSearchFixture.DateCompositeCodeSystem}|wrong-code$2020-06-15", Array.Empty<int>() },
    ];

    [Theory]
    [MemberData(nameof(TokenDateTimeData))]
    public async Task GivenACompositeSearchParameterWithTokenAndDateTime_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string queryValue,
        int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "code-value-date");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&code-value-date={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Search code-value-date={queryValue} should return {expectedObservations.Length} results");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results");
        }
    }

    /// <summary>
    /// Tests that the datetime component of a composite distinguishes gt (greater-than) from sa (starts-after).
    /// Both share code-value-date's DateRangeCode but differ in their stored valuePeriod:
    ///   [9] = 2020-01-01..2020-12-31 (straddles the 2020-06-01 search value)
    ///   [10] = 2020-08-01..2020-09-30 (starts strictly after 2020-06-01)
    /// gt2020-06-01 matches any range whose END is after the search value: both [9] and [10].
    /// sa2020-06-01 matches only ranges whose START is after the search value: [10] only.
    /// This pins the regression where sa fell through to a no-op filter arm (which would return both).
    /// </summary>
    [Fact]
    public async Task GivenACompositeDateRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults()
    {
        // Capability check
        RequireSearchParameter("Observation", "code-value-date");

        var code = $"{CompositeSearchFixture.DateCompositeCodeSystem}|{CompositeSearchFixture.DateRangeCode}";
        var straddling = _fixture.Observations[9];
        var startsAfter = _fixture.Observations[10];

        // Act - gt: range END after 2020-06-01 -> both observations
        var gtResults = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&code-value-date={code}$gt2020-06-01");

        // Act - sa: range START after 2020-06-01 -> only the range that starts after
        var saResults = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&code-value-date={code}$sa2020-06-01");

        // Assert - gt matches both
        gtResults.Length.ShouldBe(2, "gt2020-06-01 should match both the straddling range and the range starting after");
        gtResults.ShouldContain(r => r.Id == straddling.Id);
        gtResults.ShouldContain(r => r.Id == startsAfter.Id);

        // Assert - sa matches only the range whose start is after the search value
        saResults.Length.ShouldBe(1, "sa2020-06-01 should match only the range whose start is after the search value");
        saResults.ShouldContain(r => r.Id == startsAfter.Id);
        saResults.ShouldNotContain(r => r.Id == straddling.Id);
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

    // -------------------------------------------------------------------------------------------------
    // Phase 3 storage-convention characterization tests.
    //
    // These four tests pin down the read/write behavior fixed across Tasks 3-6 of the SQL data-layer
    // cleanup. They run against a real SQL Server (the E2E default storage), where EF.Functions.Collate
    // and the token/string overflow columns actually execute - coverage that the composite unit tests
    // could not provide because EF Core's InMemory provider cannot translate Collate. Each test seeds
    // its own uniquely-tagged data so it does not depend on, or perturb, the shared fixture.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Characterization for the composite token-overflow fix (Task 3): a Token|Token composite whose
    /// value-token code exceeds the 256-character inline width must round-trip through the CodeOverflow
    /// column and still match. Before Phase 3 the write side truncated at the wrong threshold and the
    /// read side never consulted the overflow column, so this returned empty.
    /// </summary>
    [Fact]
    public async Task GivenCompositeTokenValueExceedingInlineWidth_WhenSearched_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-concept");

        var tag = Guid.NewGuid().ToString();
        const string valueSystem = "http://example.org/values";

        // 269 chars > 256 inline width; distinctive tail sits past the boundary so a truncated store
        // (dropped overflow) cannot spuriously match.
        var longValueCode = "overflow-value-" + new string('x', 250) + "-END";

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("token-overflow-code", "http://example.org/composite")
            .WithCodedValue(longValueCode, valueSystem)
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-concept=http://example.org/composite|token-overflow-code${valueSystem}|{longValueCode}");

        results.Length.ShouldBe(1, "composite value token exceeding 256 chars must match via the overflow column");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Characterization for the composite token case-insensitivity fix (Task 5): composite token codes
    /// are compared under a case-insensitive collation, so a resource stored with one casing matches a
    /// search using a different casing. Before Phase 3 the comparison was ordinal/case-sensitive.
    /// </summary>
    [Fact]
    public async Task GivenCompositeTokenValueDifferingOnlyInCase_WhenSearched_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-concept");

        var tag = Guid.NewGuid().ToString();
        const string valueSystem = "http://example.org/values";

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("case-code", "http://example.org/composite")
            .WithCodedValue("MixedCaseValue", valueSystem)
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        // Stored as "MixedCaseValue", searched as upper-case - must still match.
        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-concept=http://example.org/composite|case-code${valueSystem}|MIXEDCASEVALUE");

        results.Length.ShouldBe(1, "composite token comparison must be case-insensitive");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Pins query-time case-insensitive collation matching for composite string components (the read-path
    /// fix from Task 6) as a regression guard for the new collation mechanism: a resource stored with one
    /// casing matches a search using a different casing.
    ///
    /// This test does NOT discriminate the write-side original-case-preservation fix. That fix is
    /// unobservable through the public FHIR search API, because FHIR composite search parameters accept no
    /// modifiers - there is no <c>:exact</c> for composites (see the <c>code-value-string</c> theory data
    /// around line 184, and the FHIR spec). Write-side case preservation is proven instead at the unit tier
    /// by CompositeStringOverflowTests.GivenMixedCaseStringComponent_WhenGenerated_ThenStoresOriginalCaseNotUppercased,
    /// which asserts the row generator's emitted SqlDataRecord column value directly.
    /// </summary>
    [Fact]
    public async Task GivenCompositeStringValueInMixedCase_WhenSearchedWithDifferentCase_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-string");

        var tag = Guid.NewGuid().ToString();

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("family-name", "http://example.org/composite")
            .WithStringValue("Smith")
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        // Stored as "Smith", searched as lower-case - must still match under the case-insensitive collation.
        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-string=http://example.org/composite|family-name$smith");

        results.Length.ShouldBe(1, "composite string comparison must be case-insensitive");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Characterization for the composite string-overflow fix (Task 6): a Token|String composite whose
    /// string component exceeds the 256-character inline width must round-trip through the TextOverflow
    /// column and still match a prefix that extends past the boundary. Before Phase 3 the write side
    /// split at the wrong threshold and the read side never consulted TextOverflow, so this returned empty.
    /// </summary>
    [Fact]
    public async Task GivenCompositeStringValueExceedingInlineWidth_WhenSearchedByOverflowingPrefix_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-string");

        var tag = Guid.NewGuid().ToString();

        // 275 chars > 256 inline width; distinctive tail sits past the boundary.
        var longString = "OverflowStringComposite-" + new string('a', 240) + "-TAILMARKER";

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("overflow-string-code", "http://example.org/composite")
            .WithStringValue(longString)
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        // Prefix longer than 256 chars forces the read path to reconstruct Text + TextOverflow.
        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-string=http://example.org/composite|overflow-string-code${longString}");

        results.Length.ShouldBe(1, "composite string value exceeding 256 chars must match via the overflow column");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Cross-layer integration guard that discriminates the composite token WRITE-side threshold fix
    /// (Task 5): the split moved from a hardcoded 128 chars to the real 256-char inline column width. A
    /// value in the 129-256 range is stored entirely inline (no overflow), so the read path takes the
    /// NON-overflow branch - a successful match proves the write side kept the FULL value in the primary
    /// Code column. Under the old 128-char split the tail past 128 chars was truncated and this returned
    /// empty. The write-side split is also unit-tested directly by CompositeTokenOverflowTests, but that
    /// test cannot prove the read path reconstructs and queries correctly end-to-end at this length.
    /// </summary>
    [Fact]
    public async Task GivenCompositeTokenValueWithinInlineWidth_WhenSearched_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-concept");

        var tag = Guid.NewGuid().ToString();
        const string valueSystem = "http://example.org/values";

        // 200 chars: past the old 128 split, under the 256 inline width, so it stores inline with no overflow.
        var inlineValueCode = "inline-value-" + new string('x', 183) + "-END";

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("token-inline-code", "http://example.org/composite")
            .WithCodedValue(inlineValueCode, valueSystem)
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-concept=http://example.org/composite|token-inline-code${valueSystem}|{inlineValueCode}");

        results.Length.ShouldBe(1, "composite value token between 129 and 256 chars must be stored fully inline and match");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Cross-layer integration guard that discriminates the composite string WRITE-side threshold fix
    /// (Task 6): the split moved from a hardcoded 128 chars to the real 256-char inline column width. A
    /// value in the 129-256 range is stored entirely inline (no overflow), so the read path takes the
    /// NON-overflow branch - a successful prefix match proves the write side kept the FULL value in the
    /// primary Text column. Under the old 128-char split the tail past 128 chars was truncated and a prefix
    /// extending past it returned empty. The write-side split is also unit-tested directly by
    /// CompositeStringOverflowTests, but that test cannot prove the read path reconstructs and queries
    /// correctly end-to-end at this length.
    /// </summary>
    [Fact]
    public async Task GivenCompositeStringValueWithinInlineWidth_WhenSearchedByFullValue_ThenResourceIsReturned()
    {
        RequireSearchParameter("Observation", "code-value-string");

        var tag = Guid.NewGuid().ToString();

        // 200 chars: past the old 128 split, under the 256 inline width, so it stores inline with no overflow.
        var inlineString = "InlineStringComposite-" + new string('a', 167) + "-TAILMARKER";

        var observation = ObservationBuilder.Create(SchemaProvider)
            .WithTag(tag)
            .WithCode("inline-string-code", "http://example.org/composite")
            .WithStringValue(inlineString)
            .WithStatus("final")
            .Build();

        var created = await Harness.CreateResourceAsync(observation);

        // Prefix extends well past the old 128-char boundary; only a full inline store can match it.
        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={tag}&code-value-string=http://example.org/composite|inline-string-code${inlineString}");

        results.Length.ShouldBe(1, "composite string value between 129 and 256 chars must be stored fully inline and match");
        results[0].Id.ShouldBe(created.Id);
    }

    /// <summary>
    /// Pins the OR-of-value-groups union fix for composite search. The pre-fix ComponentIndex extraction
    /// merged components across comma-separated OR groups by index and ANDed them together, so
    /// "9272-6$10,9271-8$10" would demand a single row matching code=9272-6 AND code=9271-8 AND value=10
    /// simultaneously - impossible, always empty. Correct FHIR semantics treat each comma-separated group
    /// as an independent match candidate, unioned together. Expecting exactly [0]+[2] (not one row, not all
    /// rows) is what discriminates the old buggy behavior from the fix. Lives here rather than the composite
    /// unit tests because the read path now uses EF.Functions.Collate, which the InMemory provider cannot
    /// translate.
    /// </summary>
    [Fact]
    public async Task GivenCompositeSearchWithOrOfTwoValueGroups_WhenSearched_ThenUnionsPerGroupResultsInsteadOfAndingAcrossGroups()
    {
        RequireSearchParameter("Observation", "code-value-quantity");

        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={_fixture.Tag}&code-value-quantity=http://loinc.org|9272-6$10,http://loinc.org|9271-8$10");

        results.Length.ShouldBe(2, "each comma-separated value group is an independent match candidate, OR'd together");
        results.ShouldContain(r => r.Id == _fixture.Observations[0].Id); // APGAR 1-minute, score 10
        results.ShouldContain(r => r.Id == _fixture.Observations[2].Id); // APGAR 20-minute, score 10
    }

    /// <summary>
    /// Pins effective-type (not positional) resolution of composite component values. The "relationship"
    /// definition order is [Reference, Token], but this test passes the values swapped: the Token value
    /// first, then the Reference. A "|" separator in the token value is required for the swap to be
    /// exercised at all - the parser's value-shape inference only classifies a value as token-shaped when
    /// it carries the separator, so a bare code would fall back to the static Reference definition and
    /// silently stop testing the swap path. The full system|code form (not the bare |code empty-system
    /// form) is deliberate: relatesTo.code is indexed WITH the document-relationship-type CodeSystem, so
    /// |replaces alone would demand SystemId IS NULL and never match. If GenerateReferenceTokenGroupQueryAsync
    /// resolved by position instead of effective type it would parse the reference value as a token and vice
    /// versa, matching nothing - so a single hit genuinely discriminates the fix. Lives here rather than the
    /// composite unit tests because the read path now uses EF.Functions.Collate, which the InMemory provider
    /// cannot translate.
    /// </summary>
    [Fact]
    public async Task GivenReferenceTokenCompositeWithValuesSwappedRelativeToDefinition_WhenSearched_ThenResolvesByEffectiveType()
    {
        RequireSearchParameter("DocumentReference", "relationship");

        var results = await Harness.SearchAsync(
            "DocumentReference",
            $"_tag={_fixture.Tag}&relationship=http://hl7.org/fhir/CodeSystem/document-relationship-type|replaces$DocumentReference/document1");

        results.Length.ShouldBe(1, "component values are resolved by effective type, not position");
        results[0].Id.ShouldBe(_fixture.DocumentReferences[0].Id);
    }
}
