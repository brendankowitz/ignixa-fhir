// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Orders matches for <c>_sort</c> from their index entries, without reading the resources.
/// </summary>
/// <remarks>
/// <para>
/// A resource with several values for a sort parameter sorts by its lowest value ascending and its highest value
/// descending; for a date that is the start of its earliest range ascending and the end of its latest range
/// descending, and for a number or quantity the low and high bound. Strings compare case- and accent-insensitively,
/// tokens by code, uris ordinally. A resource without a value for a sort key comes after every resource with one,
/// in either direction, as in the SQL backend.
/// </para>
/// <para>
/// Ties on every key are broken by <c>_lastUpdated</c> (oldest first, resources without one last) and then by id, so
/// the order is total and repeatable whatever order the source enumerates in.
/// </para>
/// </remarks>
internal static class InMemorySortOrder
{
    private static readonly Comparer<object> KeyOrder = Comparer<object>.Create((x, y) => CompareMissingLast(x, y, descending: false));

    /// <exception cref="SearchOperationNotSupportedException">A sort parameter's type cannot be ordered.</exception>
    public static IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Order(
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> matches,
        IReadOnlyList<SortExpression> sorts)
    {
        foreach (SortExpression sort in sorts)
        {
            EnsureSortable(sort.Parameter);
        }

        var keyed = matches
            .Select(match => (
                Match: match,
                Keys: sorts.Select(sort => SortKey(match, sort)).ToArray(),
                LastUpdated: LastUpdated(match.Index)))
            .ToList();

        keyed.Sort((x, y) => Compare(x.Keys, y.Keys, x.LastUpdated, y.LastUpdated, x.Match.Location, y.Match.Location, sorts));
        return [.. keyed.Select(row => row.Match)];
    }

    private static void EnsureSortable(SearchParameterInfo parameter)
    {
        if (parameter.Code is SearchParameterNames.Id ||
            parameter.Type is SearchParamType.Date or SearchParamType.String or SearchParamType.Token
                or SearchParamType.Number or SearchParamType.Quantity or SearchParamType.Uri)
        {
            return;
        }

        throw new SearchOperationNotSupportedException($"_sort by {parameter.Type} parameter '{parameter.Code}' is not supported in in-memory search.");
    }

    private static int Compare(
        object?[] xKeys,
        object?[] yKeys,
        DateTimeOffset? xLastUpdated,
        DateTimeOffset? yLastUpdated,
        ResourceKey x,
        ResourceKey y,
        IReadOnlyList<SortExpression> sorts)
    {
        for (int i = 0; i < sorts.Count; i++)
        {
            int comparison = CompareMissingLast(xKeys[i], yKeys[i], sorts[i].SortOrder == Expressions.SortOrder.Descending);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        int byLastUpdated = CompareMissingLast(xLastUpdated, yLastUpdated, descending: false);
        if (byLastUpdated != 0)
        {
            return byLastUpdated;
        }

        int byType = string.CompareOrdinal(x.ResourceType, y.ResourceType);
        return byType != 0 ? byType : string.CompareOrdinal(x.Id, y.Id);
    }

    private static int CompareMissingLast(object? x, object? y, bool descending)
    {
        if (x is null || y is null)
        {
            return (x is null).CompareTo(y is null);
        }

        int comparison = x is string xs && y is string ys
            ? string.CompareOrdinal(xs, ys)
            : ((IComparable)x).CompareTo(y);
        return descending ? -comparison : comparison;
    }

    /// <summary>The value <paramref name="match"/> sorts by for <paramref name="sort"/>, or null when it has none.</summary>
    private static object? SortKey((ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) match, SortExpression sort)
    {
        // _id has no index entries; the key holds it.
        if (sort.Parameter.Code == SearchParameterNames.Id)
        {
            return match.Location.Id;
        }

        bool descending = sort.SortOrder == Expressions.SortOrder.Descending;
        IEnumerable<object> values = match.Index
            .Where(entry => entry.SearchParameter.Name == sort.Parameter.Name)
            .Select(entry => Value(entry.Value, descending))
            .OfType<object>();

        return descending
            ? values.Max(KeyOrder)
            : values.Min(KeyOrder);
    }

    private static object? Value(ISearchValue value, bool descending) =>
        value switch
        {
            DateTimeSearchValue date => descending ? date.End : date.Start,
            NumberSearchValue number => descending ? number.High ?? number.Low : number.Low ?? number.High,
            QuantitySearchValue quantity => descending ? quantity.High ?? quantity.Low : quantity.Low ?? quantity.High,
            StringSearchValue s => SearchIndexEntryMatcher.Fold(s.String).ToUpperInvariant(),
            TokenSearchValue token => string.IsNullOrEmpty(token.Code) ? null : token.Code,
            UriSearchValue uri => uri.Uri,
            _ => throw new SearchOperationNotSupportedException($"_sort cannot order by a {value.GetType().Name} index value in in-memory search."),
        };

    private static DateTimeOffset? LastUpdated(IReadOnlyCollection<SearchIndexEntry> index) =>
        index
            .Where(entry => entry.SearchParameter.Code == SearchParameterNames.LastUpdated)
            .Select(entry => entry.Value)
            .OfType<DateTimeSearchValue>()
            .Select(date => (DateTimeOffset?)date.Start)
            .Min();
}
