// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Represents a search index entry.
/// </summary>
public class SearchIndexEntry : IEquatable<SearchIndexEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexEntry"/> class.
    /// </summary>
    /// <param name="searchParameter">The search parameter</param>
    /// <param name="value">The searchable value.</param>
    public SearchIndexEntry(SearchParameterInfo searchParameter, ISearchValue value)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.IsNotNull(value, nameof(value));

        SearchParameter = searchParameter;
        Value = value;
    }

    /// <summary>
    /// Gets the search parameter
    /// </summary>
    public SearchParameterInfo SearchParameter { get; }

    /// <summary>
    /// Gets the searchable value.
    /// </summary>
    public ISearchValue Value { get; }

    public bool Equals(SearchIndexEntry other)
    {
        if (other == null) return false;

        if (other.SearchParameter.Url == SearchParameter.Url &&
            other.SearchParameter.Code.Equals(SearchParameter.Code, StringComparison.OrdinalIgnoreCase) &&
            other.Value == Value)
            return true;

        return false;
    }

    public static bool operator ==(SearchIndexEntry left, SearchIndexEntry right)
    {
        if ((object)left == null && (object)right == null)
            return true;
        else if ((object)left == null)
            return false;
        else
            return left.Equals(right);
    }

    public static bool operator !=(SearchIndexEntry left, SearchIndexEntry right)
    {
        return !(left == right);
    }

    public override bool Equals(object obj)
    {
        if (obj == null) return false;

        var other = obj as SearchIndexEntry;
        if (other == null)
            return false;
        else
            return Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            SearchParameter.Code.GetHashCode(StringComparison.OrdinalIgnoreCase),
            SearchParameter.Url?.GetHashCode(),
            Value.GetHashCode());
    }
}
