// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Locks down that every ComparisonPredicates method throws NotSupportedException for a
/// BinaryOperator value outside the enum's six defined members, rather than silently returning
/// an empty/unfiltered result. The switch expression throws before the queryable is enumerated,
/// so an empty in-memory queryable is sufficient input - no EF Core translation involved.
/// </summary>
public class ComparisonPredicatesTests
{
    private const BinaryOperator InvalidOperator = (BinaryOperator)99;

    [Fact]
    public void GivenInvalidOperator_WhenApplySurrogateIdComparison_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<ResourceEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplySurrogateIdComparison(query, InvalidOperator, 1));
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyTtlComparison_ThenThrowsNotSupported()
    {
        var resources = Enumerable.Empty<ResourceEntity>().AsQueryable();
        var ttls = Enumerable.Empty<ResourceTtlEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyTtlComparison(resources, ttls, InvalidOperator, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyDateTimeStartComparison_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<DateTimeSearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyDateTimeStartComparison(query, InvalidOperator, DateTime.UtcNow));
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyDateTimeEndComparison_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<DateTimeSearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyDateTimeEndComparison(query, InvalidOperator, DateTime.UtcNow));
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyNumberRangeComparison_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<NumberSearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyNumberRangeComparison(query, InvalidOperator, 1m));
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyQuantityRangeComparison_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<QuantitySearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyQuantityRangeComparison(query, InvalidOperator, 1m));
    }

    public static IEnumerable<object[]> NumberRangeComparisonCases()
    {
        // Stored range: [Low=10, High=20] - a genuinely fuzzy range, not a point, so overlap vs.
        // containment vs. strict-separation are all distinguishable.
        yield return new object[] { BinaryOperator.GreaterThan, 15m, true };       // High(20) > 15
        yield return new object[] { BinaryOperator.GreaterThan, 25m, false };      // High(20) > 25 is false
        yield return new object[] { BinaryOperator.GreaterThanOrEqual, 20m, true }; // High(20) >= 20
        yield return new object[] { BinaryOperator.GreaterThanOrEqual, 21m, false };
        yield return new object[] { BinaryOperator.LessThan, 15m, true };          // Low(10) < 15
        yield return new object[] { BinaryOperator.LessThan, 5m, false };          // Low(10) < 5 is false
        yield return new object[] { BinaryOperator.LessThanOrEqual, 10m, true };   // Low(10) <= 10
        yield return new object[] { BinaryOperator.LessThanOrEqual, 5m, false };
        yield return new object[] { BinaryOperator.StartsAfter, 5m, true };        // Low(10) > 5
        yield return new object[] { BinaryOperator.StartsAfter, 15m, false };      // Low(10) > 15 is false - distinguishes Sa from Gt (Gt(15) matches, Sa(15) must not)
        yield return new object[] { BinaryOperator.EndsBefore, 25m, true };        // High(20) < 25
        yield return new object[] { BinaryOperator.EndsBefore, 15m, false };       // High(20) < 15 is false - distinguishes Eb from Lt (Lt(15) matches, Eb(15) must not)
    }

    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredRange_WhenApplyNumberRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[] { new NumberSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, LowValue = 10m, HighValue = 20m } }.AsQueryable();

        var results = ComparisonPredicates.ApplyNumberRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredRange_WhenApplyQuantityRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[] { new QuantitySearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, LowValue = 10m, HighValue = 20m } }.AsQueryable();

        var results = ComparisonPredicates.ApplyQuantityRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredCompositeRange_WhenApplyQuantityRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[]
        {
            new TokenQuantityCompositeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, Code1 = "code", LowValue = 10m, HighValue = 20m }
        }.AsQueryable();

        var results = ComparisonPredicates.ApplyQuantityRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyQuantityRangeComparisonOnComposite_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<TokenQuantityCompositeSearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyQuantityRangeComparison(query, InvalidOperator, 1m));
    }

    [Fact]
    public void GivenStoredRange_WhenApplyDateTimeStartComparisonStartsAfter_ThenMatchesStrictSeparation()
    {
        var stored = new[]
        {
            new DateTimeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, StartDateTime = new DateTime(2020, 1, 10), EndDateTime = new DateTime(2020, 1, 20) }
        }.AsQueryable();

        ComparisonPredicates.ApplyDateTimeStartComparison(stored, BinaryOperator.StartsAfter, new DateTime(2020, 1, 5)).Count().ShouldBe(1);
        ComparisonPredicates.ApplyDateTimeStartComparison(stored, BinaryOperator.StartsAfter, new DateTime(2020, 1, 15)).Count().ShouldBe(0);
    }

    [Fact]
    public void GivenStoredRange_WhenApplyDateTimeEndComparisonEndsBefore_ThenMatchesStrictSeparation()
    {
        var stored = new[]
        {
            new DateTimeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, StartDateTime = new DateTime(2020, 1, 10), EndDateTime = new DateTime(2020, 1, 20) }
        }.AsQueryable();

        ComparisonPredicates.ApplyDateTimeEndComparison(stored, BinaryOperator.EndsBefore, new DateTime(2020, 1, 25)).Count().ShouldBe(1);
        ComparisonPredicates.ApplyDateTimeEndComparison(stored, BinaryOperator.EndsBefore, new DateTime(2020, 1, 15)).Count().ShouldBe(0);
    }
}
