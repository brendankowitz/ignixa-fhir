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
/// E2E tests for date search parameters.
/// Tests date search with various prefixes (eq, ne, lt, gt, le, ge, sa, eb) and precisions.
/// Covers date comparisons with year, month, day, second, and millisecond precision.
/// Tests Period datatype handling and invalid date formats.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.DateSearchTests
/// </summary>
/// <remarks>
/// FHIR Date Search Semantics (http://hl7.org/fhir/search.html#prefix):
/// - eq: the range of the search value fully contains the range of the target value
/// - ne: the range of the search value does not fully contain the range of the target value
/// - gt: the range above the search value intersects with the range of the target value
/// - lt: the range below the search value intersects with the range of the target value
/// - le: the range below the search value intersects with or fully contains the target value
/// - ge: the range above the search value intersects with or fully contains the target value
/// - sa: starts after - the range above the search value contains the range of the target value (no overlap)
/// - eb: ends before - the range below the search value contains the range of the target value (no overlap)
///
/// Date precision: Each date has an implicit range based on its precision:
/// - "1980" = 1980-01-01T00:00:00.0000000 to 1980-12-31T23:59:59.9999999
/// - "1980-05" = 1980-05-01T00:00:00.0000000 to 1980-05-31T23:59:59.9999999
/// - "1980-05-11" = 1980-05-11T00:00:00.0000000 to 1980-05-11T23:59:59.9999999
/// - "1980-05-11T16:32:15" = 1980-05-11T16:32:15.0000000 to 1980-05-11T16:32:15.9999999
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class DateSearchTests : CapabilityDrivenTestBase, IClassFixture<DateSearchTestFixture>
{
    private readonly DateSearchTestFixture _fixture;

    public DateSearchTests(IgnixaApiFixture apiFixture, DateSearchTestFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Test data for date search scenarios with various prefixes and precisions.
    /// Format: [queryValue, expectedIndices...]
    /// Maps to _fixture.Observations array indices.
    /// </summary>
    public static readonly object[][] DateSearchParameterData =
    [
        // ===== eq (equals - search value fully contains target) =====
        // eq is containment, NOT overlap: the search value's range must fully contain the target's range.
        // A narrower search value therefore matches FEWER observations, not more -- an observation stored
        // at a coarser precision (obs[1]="1980", obs[2]="1980-05") is excluded by any search value narrower
        // than itself, because a month cannot contain a year. Each row below names the observations whose
        // stored range fits entirely inside the search value's range.
        //
        // The ne rows further down are the exact complements of these sets and already pass, which is the
        // independent check that these values are right.
        ["1980", 1, 2, 3, 4, 5, 7],                          // 1980 contains everything in 1980; obs[0] is 1979 and obs[6] is 1981
        ["1980-01"],                                          // obs[1] is the whole of 1980, which January cannot contain; nothing else is in January
        ["1980-05", 2, 3, 4, 5, 7],                          // May contains obs[2] (exactly May), obs[3..5] (May 11) and obs[7] (May 16-17); not obs[1], the whole year
        ["1980-05-10"],                                       // nothing is stored on May 10 -- obs[3..5] are May 11, obs[2] is the whole month
        ["1980-05-11", 3, 4, 5],                             // May 11 contains obs[3] (exactly that day) and the two instants inside it; not obs[2], the whole month
        ["1980-05-11T16:32:15", 4, 5],                       // that second contains obs[4] (exactly that second) and obs[5] (.500 within it); not obs[3], the whole day
        ["1980-05-11T16:32:15.500", 5],                      // obs[5] only -- obs[4] spans the whole second, which one millisecond cannot contain
        ["1980-05-11T16:32:15.5000000", 5],                  // same instant as above written to tick precision
        ["1980-05-11T16:32:15.5000001"],                      // one tick past obs[5]'s stored instant, so nothing fits
        ["1980-05-11T16:32:30"],                              // nothing is stored in that second

        // ===== ne (not equals - search value does NOT fully contain target) =====
        ["ne1980", 0, 6],                                     // Not in 1980
        ["ne1980-01", 0, 1, 2, 3, 4, 5, 6, 7],               // Not fully in Jan 1980
        ["ne1980-05", 0, 1, 6],                               // Not fully in May 1980
        ["ne1980-05-10", 0, 1, 2, 3, 4, 5, 6, 7],            // Not fully in May 10
        ["ne1980-05-11", 0, 1, 2, 6, 7],                     // Not fully in May 11
        ["ne1980-05-11T16:32:15", 0, 1, 2, 3, 6, 7],         // Not fully in that second
        ["ne1980-05-11T16:32:15.500", 0, 1, 2, 3, 4, 6, 7],  // Not that exact millisecond
        ["ne1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4, 6, 7],
        ["ne1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5, 6, 7],
        ["ne1980-05-11T16:32:30", 0, 1, 2, 3, 4, 5, 6, 7],

        // ===== lt (less than - target start is before search value start) =====
        ["lt1980", 0],                                        // Before 1980-01-01
        ["lt1980-04", 0, 1],                                  // Before April 1980
        ["lt1980-05", 0, 1],                                  // Before May 1980
        ["lt1980-05-10", 0, 1, 2],                            // Before May 10
        ["lt1980-05-11", 0, 1, 2],                            // Before May 11
        ["lt1980-05-11T16:32:14", 0, 1, 2, 3],                // Before 16:32:14
        ["lt1980-05-11T16:32:15", 0, 1, 2, 3],                // Before 16:32:15
        ["lt1980-05-11T16:32:15.4999999", 0, 1, 2, 3, 4],     // Before .4999999
        ["lt1980-05-11T16:32:15.500", 0, 1, 2, 3, 4],         // Before .500
        ["lt1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4],
        ["lt1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5],  // Before .5000001
        ["lt1980-05-11T16:32:16", 0, 1, 2, 3, 4, 5],          // Before 16:32:16
        ["lt1980-05-12", 0, 1, 2, 3, 4, 5],                   // Before May 12
        ["lt1980-06", 0, 1, 2, 3, 4, 5, 7],                   // Before June
        ["lt1981", 0, 1, 2, 3, 4, 5, 7],                      // Before 1981
        ["lt1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5, 6, 7], // Very specific

        // ===== gt (greater than - target end is after search value end) =====
        ["gt1979-12-31T23:59:59.9999999", 1, 2, 3, 4, 5, 6, 7], // After end of 1979-12-31
        ["gt1980", 6],                                        // After end of 1980
        ["gt1980-04", 1, 2, 3, 4, 5, 6, 7],                   // After end of April
        ["gt1980-05", 1, 6],                                  // After end of May
        ["gt1980-05-11", 1, 2, 6, 7],                         // After end of May 11
        ["gt1980-05-11T16:32:14", 1, 2, 3, 4, 5, 6, 7],       // After 16:32:14
        ["gt1980-05-11T16:32:15", 1, 2, 3, 6, 7],             // After 16:32:15
        ["gt1980-05-11T16:32:15.4999999", 1, 2, 3, 4, 5, 6, 7],
        ["gt1980-05-11T16:32:15.500", 1, 2, 3, 4, 6, 7],      // After .500
        ["gt1980-05-11T16:32:15.5000000", 1, 2, 3, 4, 6, 7],
        ["gt1980-05-11T16:32:15.5000001", 1, 2, 3, 4, 6, 7],
        ["gt1980-05-11T16:32:16", 1, 2, 3, 6, 7],             // After 16:32:16
        ["gt1980-05-12", 1, 2, 6, 7],                         // After May 12
        ["gt1980-06", 1, 6],                                  // After June
        ["gt1981-01-01T00:00:00.0000001", 6],                 // After specific instant

        // ===== le (less than or equal - target start <= search value end) =====
        ["le1980", 0, 1, 2, 3, 4, 5, 7],                      // Start <= end of 1980
        ["le1980-04", 0, 1],                                  // Start <= end of April
        ["le1980-05", 0, 1, 2, 3, 4, 5, 7],                   // Start <= end of May
        ["le1980-05-10", 0, 1, 2],                            // Start <= May 10 end
        ["le1980-05-11", 0, 1, 2, 3, 4, 5],                   // Start <= May 11 end
        ["le1980-05-11T16:32:14", 0, 1, 2, 3],
        ["le1980-05-11T16:32:15", 0, 1, 2, 3, 4, 5],
        ["le1980-05-11T16:32:15.4999999", 0, 1, 2, 3, 4],
        ["le1980-05-11T16:32:15.500", 0, 1, 2, 3, 4, 5],
        ["le1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4, 5],
        ["le1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5],
        ["le1980-05-11T16:32:16", 0, 1, 2, 3, 4, 5],
        ["le1980-05-12", 0, 1, 2, 3, 4, 5],
        ["le1980-06", 0, 1, 2, 3, 4, 5, 7],
        ["le1981", 0, 1, 2, 3, 4, 5, 6, 7],
        ["le1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5, 6, 7],

        // ===== ge (greater than or equal - target end >= search value start) =====
        ["ge1979-12-31T23:59:59.9999999", 0, 1, 2, 3, 4, 5, 6, 7], // End >= start
        ["ge1980", 1, 2, 3, 4, 5, 6, 7],                      // End >= 1980-01-01
        ["ge1980-04", 1, 2, 3, 4, 5, 6, 7],                   // End >= April 1
        ["ge1980-05", 1, 2, 3, 4, 5, 6, 7],                   // End >= May 1
        ["ge1980-05-11", 1, 2, 3, 4, 5, 6, 7],                // End >= May 11
        ["ge1980-05-11T16:32:14", 1, 2, 3, 4, 5, 6, 7],
        ["ge1980-05-11T16:32:15", 1, 2, 3, 4, 5, 6, 7],
        ["ge1980-05-11T16:32:15.4999999", 1, 2, 3, 4, 5, 6, 7],
        ["ge1980-05-11T16:32:15.500", 1, 2, 3, 4, 5, 6, 7],
        ["ge1980-05-11T16:32:15.5000000", 1, 2, 3, 4, 5, 6, 7],
        ["ge1980-05-11T16:32:15.5000001", 1, 2, 3, 4, 6, 7],  // obs[5] ends at .500
        ["ge1980-05-11T16:32:16", 1, 2, 3, 6, 7],             // obs[4,5] end before this
        ["ge1980-05-12", 1, 2, 6, 7],
        ["ge1980-06", 1, 6],
        ["ge1981-01-01T00:00:00.0000001", 6],

        // ===== sa (starts after - target start > search value end, no overlap) =====
        ["sa1980", 6],                                        // Starts after 1980-12-31 end
        ["sa1980-04", 2, 3, 4, 5, 6, 7],                      // Starts after April end
        ["sa1980-05", 6],                                     // Starts after May 31
        ["sa1980-05-10", 3, 4, 5, 6, 7],                      // Starts after May 10
        ["sa1980-05-11", 6, 7],                               // Starts after May 11
        ["sa1980-05-11T16:32:14", 4, 5, 6, 7],                // Starts after 14.999...
        ["sa1980-05-11T16:32:15", 6, 7],                      // Starts after 15.999...
        ["sa1980-05-11T16:32:15.4999999", 5, 6, 7],           // Starts after .4999999
        ["sa1980-05-11T16:32:15.500", 6, 7],                  // Starts after .500
        ["sa1980-05-11T16:32:15.5000000", 6, 7],
        ["sa1980-05-11T16:32:15.5000001", 6, 7],
        ["sa1980-05-11T16:32:16", 6, 7],
        ["sa1980-05-12", 6, 7],
        ["sa1980-06", 6],
        ["sa1981"],                                           // Nothing starts after 1981
        ["sa1981-01-01T00:00:00.0000001"],

        // ===== eb (ends before - target end < search value start, no overlap) =====
        ["eb1979-12-31T23:59:59.9999999"],                    // Nothing ends before this
        ["eb1980", 0],                                        // Ends before 1980-01-01
        ["eb1980-04", 0],                                     // Ends before April 1
        ["eb1980-05", 0],                                     // Ends before May 1
        ["eb1980-05-11", 0],                                  // Ends before May 11
        ["eb1980-05-11T16:32:14", 0],
        ["eb1980-05-11T16:32:15", 0],
        ["eb1980-05-11T16:32:15.4999999", 0],
        ["eb1980-05-11T16:32:15.500", 0],
        ["eb1980-05-11T16:32:15.5000000", 0],
        ["eb1980-05-11T16:32:15.5000001", 0, 5],              // obs[5] ends at .500
        ["eb1980-05-11T16:32:16", 0, 4, 5],                   // obs[4,5] end before 16
        ["eb1980-05-12", 0, 3, 4, 5],                         // Day-level ends before 12
        ["eb1980-06", 0, 2, 3, 4, 5, 7],                      // Month-level ends before June
        ["eb1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5, 7] // Most obs end before this
    ];

    /// <summary>
    /// Tests date search parameter with various prefixes and precisions.
    /// Validates correct date range semantics for eq, ne, lt, gt, le, ge, sa, eb prefixes.
    /// </summary>
    [Theory]
    [MemberData(nameof(DateSearchParameterData))]
    public async Task GivenADateTimeSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string queryValue,
        params int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "date");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&date={queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Query 'date={queryValue}' should return {expectedObservations.Length} results");

        // Verify each expected observation is in results (order may vary)
        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results for date={queryValue}");
        }
    }

    /// <summary>
    /// Tests combining two date search parameters for range queries.
    /// Example: date=gt1980-05-10&amp;date=lt1980-05-12 finds dates in the range.
    /// </summary>
    [Theory]
    [InlineData("gt1980-05-10", "lt1980-05-12", 1, 2, 3, 4, 5)] // Range: after May 10 AND before May 12
    public async Task GivenTwoDateTimeSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(
        string queryValue1,
        string queryValue2,
        params int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "date");

        // Act - multiple date parameters create an AND condition
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&date={queryValue1}&date={queryValue2}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Query 'date={queryValue1}&date={queryValue2}' should return {expectedObservations.Length} results");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} should be in results for date range query");
        }
    }

    /// <summary>
    /// Tests that invalid date formats return BadRequest (400).
    /// </summary>
    [Theory]
    [InlineData("***")]   // Invalid characters
    [InlineData("!")]     // Invalid format
    public async Task GivenAnInvalidDateTimeSearchParam_WhenSearched_ThenExceptionShouldBeThrown(string queryValue)
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Act & Assert - invalid format should throw BadRequest
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await Harness.SearchAsync("Patient", $"birthdate={queryValue}"));

        exception.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest,
            $"Invalid date format '{queryValue}' should return 400 Bad Request");
    }

    /// <summary>
    /// Tests that out-of-range dates return BadRequest (400).
    /// Examples: Invalid day in month, too many decimal places.
    /// </summary>
    [Theory]
    [InlineData("1973-02-30")]                              // Feb 30 doesn't exist
    [InlineData("1973-02-28T01:01:09.999999999999999999")]  // Too many decimal places
    public async Task GivenAnOutOfRangeDateTimeSearchParam_WhenSearched_ThenExceptionShouldBeThrown(string queryValue)
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Act & Assert - out-of-range date should throw BadRequest
        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await Harness.SearchAsync("Patient", $"birthdate={queryValue}"));

        exception.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest,
            $"Out-of-range date '{queryValue}' should return 400 Bad Request");
    }

    /// <summary>
    /// Tests finding a Period-valued observation that covers a given instant.
    /// </summary>
    /// <remarks>
    /// This is deliberately not an <c>eq</c> query. "The row's range covers this instant" is an overlap, and
    /// <c>eq</c> is containment the other way round — a two-day period is never contained in an instant, so
    /// <c>date=&lt;instant&gt;</c> correctly returns nothing here. The prefix pair below is how the spec
    /// expresses overlap: <c>le</c> gives "target starts at or before" and <c>ge</c> "target ends at or
    /// after", and their conjunction is exactly the set of rows spanning the instant. The expected
    /// observations are unchanged from when this asserted an overlap-shaped <c>eq</c>, because the intent
    /// was always overlap; only the query needed to say so.
    /// </remarks>
    [Theory]
    [InlineData("1980-05-16T16:32:15.500", 1, 2, 7)] // Spans the instant: year, month, and period [05-16, 05-17]
    public async Task GivenAnInstant_WhenSearchedAgainstAPeriod_ThenTheCoveringObservationsAreReturned(
        string queryValue,
        params int[] expectedIndices)
    {
        // Capability check
        RequireSearchParameter("Observation", "date");

        // Act
        var results = await Harness.SearchAsync(
            "Observation",
            $"_tag={_fixture.Tag}&date=le{queryValue}&date=ge{queryValue}");

        // Assert
        var expectedObservations = expectedIndices.Select(i => _fixture.Observations[i]).ToArray();

        results.Length.ShouldBe(expectedObservations.Length,
            $"Query 'date={queryValue}' should match Period observation");

        foreach (var expected in expectedObservations)
        {
            results.ShouldContain(r => r.Id == expected.Id,
                $"Expected observation {expected.Id} (including Period) should be in results");
        }
    }
}
