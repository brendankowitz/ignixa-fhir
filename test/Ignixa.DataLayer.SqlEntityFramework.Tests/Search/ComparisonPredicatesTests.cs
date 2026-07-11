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
}
