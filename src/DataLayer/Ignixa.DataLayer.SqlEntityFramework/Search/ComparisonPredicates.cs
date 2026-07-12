// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Shared BinaryOperator-to-EF-predicate dispatch. Each overload's Where() calls use literal
/// lambda bodies (required for EF Core's LINQ-to-SQL translator) rather than a shared delegate,
/// but the operator dispatch itself - previously duplicated nine times in SearchParameterQueryGenerator -
/// is written once per entity/value-type combination and called from every site that needs it.
/// </summary>
public static class ComparisonPredicates
{
    public static IQueryable<Entities.ResourceEntity> ApplySurrogateIdComparison(
        IQueryable<Entities.ResourceEntity> query, BinaryOperator op, long targetId) => op switch
    {
        BinaryOperator.Equal => query.Where(r => r.ResourceSurrogateId == targetId),
        BinaryOperator.NotEqual => query.Where(r => r.ResourceSurrogateId != targetId),
        BinaryOperator.GreaterThan => query.Where(r => r.ResourceSurrogateId > targetId),
        BinaryOperator.GreaterThanOrEqual => query.Where(r => r.ResourceSurrogateId >= targetId),
        BinaryOperator.LessThan => query.Where(r => r.ResourceSurrogateId < targetId),
        BinaryOperator.LessThanOrEqual => query.Where(r => r.ResourceSurrogateId <= targetId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for surrogate ID comparison"),
    };

    /// <summary>
    /// Joins resources to their TTL row and applies the comparison, owning the join itself rather than
    /// accepting a pre-joined queryable: EF Core's translator cannot flatten a Where() applied to a member
    /// access on a freshly-projected record/tuple (verified - it throws "could not be translated" for both),
    /// so each arm below writes out the same join+where literally, matching the six-Where-calls pattern used
    /// by the other ComparisonPredicates methods but at the join-query granularity instead of a single field.
    /// </summary>
    public static IQueryable<Entities.ResourceEntity> ApplyTtlComparison(
        IQueryable<Entities.ResourceEntity> resources,
        IQueryable<Entities.ResourceTtlEntity> ttls,
        BinaryOperator op,
        DateTimeOffset targetValue) => op switch
    {
        BinaryOperator.Equal =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt == targetValue
            select r,
        BinaryOperator.NotEqual =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt != targetValue
            select r,
        BinaryOperator.GreaterThan =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt > targetValue
            select r,
        BinaryOperator.GreaterThanOrEqual =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt >= targetValue
            select r,
        BinaryOperator.LessThan =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt < targetValue
            select r,
        BinaryOperator.LessThanOrEqual =>
            from r in resources
            join ttl in ttls on new { r.ResourceTypeId, r.ResourceId } equals new { ttl.ResourceTypeId, ttl.ResourceId }
            where ttl.ExpiresAt <= targetValue
            select r,
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for TTL comparison"),
    };

    public static IQueryable<long> ApplyDateTimeStartComparison(
        IQueryable<Entities.DateTimeSearchParamEntity> query, BinaryOperator op, DateTime value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.StartDateTime == value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.NotEqual => query.Where(sp => sp.StartDateTime != value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThan => query.Where(sp => sp.StartDateTime > value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.StartDateTime >= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThan => query.Where(sp => sp.StartDateTime < value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.StartDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.StartsAfter => query.Where(sp => sp.StartDateTime > value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime start comparison"),
    };

    public static IQueryable<long> ApplyDateTimeEndComparison(
        IQueryable<Entities.DateTimeSearchParamEntity> query, BinaryOperator op, DateTime value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.EndDateTime == value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.NotEqual => query.Where(sp => sp.EndDateTime != value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThan => query.Where(sp => sp.EndDateTime > value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.EndDateTime >= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThan => query.Where(sp => sp.EndDateTime < value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.EndDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.EndsBefore => query.Where(sp => sp.EndDateTime < value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime end comparison"),
    };

    /// <summary>
    /// Applies a range-encoded ("fuzzy") comparison used by both Number and Quantity search parameters,
    /// where each indexed value is stored as a [LowValue, HighValue] range rather than a single value.
    /// </summary>
    public static IQueryable<Entities.NumberSearchParamEntity> ApplyNumberRangeComparison(
        IQueryable<Entities.NumberSearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Number comparison"),
    };

    /// <summary>
    /// Applies a range-encoded ("fuzzy") comparison used by both Number and Quantity search parameters,
    /// where each indexed value is stored as a [LowValue, HighValue] range rather than a single value.
    /// </summary>
    public static IQueryable<Entities.QuantitySearchParamEntity> ApplyQuantityRangeComparison(
        IQueryable<Entities.QuantitySearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Quantity comparison"),
    };

    /// <summary>
    /// Applies a range-encoded ("fuzzy") comparison used by both Number and Quantity search parameters,
    /// where each indexed value is stored as a [LowValue, HighValue] range rather than a single value.
    /// </summary>
    public static IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> ApplyQuantityRangeComparison(
        IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Quantity comparison"),
    };
}
