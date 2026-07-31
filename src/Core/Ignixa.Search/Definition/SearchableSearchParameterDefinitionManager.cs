// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// A SearchParameterDefinitionManager that hides parameters whose index is not usable. Searchable parameters
/// always pass; merely <em>supported</em> ones pass only when the caller opts in through the constructor.
/// </summary>
public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly SearchParameterDefinitionManager _inner;
    private readonly Func<bool> _includePartiallyIndexedSearchParameters;

    /// <param name="inner">The definition manager holding every known parameter, searchable or not.</param>
    /// <param name="includePartiallyIndexedSearchParameters">
    /// Decides whether parameters that are merely <em>supported</em> - registered but not yet reindexed - are
    /// admitted alongside searchable ones. The index behind such a parameter is half populated, which makes the
    /// result set wrong in both directions rather than merely short: a positive filter misses resources whose
    /// rows do not exist yet, while a negation (<c>:not</c>, <c>:missing=true</c>) lowers to
    /// <c>Except(every resource of the type, the inner match)</c> and so hands those same not-yet-indexed
    /// resources back as matches. Nothing in the response distinguishes such a bundle from a complete one.
    /// Defaults to refusing them.
    /// <para>
    /// Every public accessor invokes this exactly once and applies the answer to all of its results, so a
    /// deferred sequence cannot change its mind part-way through an enumeration, and a delegate that throws
    /// does so at the accessor call rather than at some later <c>MoveNext</c>.
    /// </para>
    /// </param>
    public SearchableSearchParameterDefinitionManager(
        SearchParameterDefinitionManager inner,
        Func<bool> includePartiallyIndexedSearchParameters = null)
    {
        EnsureArg.IsNotNull(inner, nameof(inner));

        _inner = inner;
        _includePartiallyIndexedSearchParameters = includePartiallyIndexedSearchParameters ?? (() => false);
    }

    public IEnumerable<SearchParameterInfo> AllSearchParameters => GetAllSearchParameters();

    public IReadOnlyDictionary<string, string> SearchParameterHashMap => _inner.SearchParameterHashMap;

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        bool includePartiallyIndexed = _includePartiallyIndexedSearchParameters();

        return _inner.GetSearchParameters(resourceType).Where(p => IsVisible(p, includePartiallyIndexed));
    }

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        searchParameters = null;
        bool includePartiallyIndexed = _includePartiallyIndexedSearchParameters();

        if (_inner.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo> innerSearchParameters))
        {
            searchParameters = innerSearchParameters.Where(p => IsVisible(p, includePartiallyIndexed));
            return true;
        }

        return false;
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        searchParameter = null;

        if (_inner.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter)
            && IsVisible(parameter, _includePartiallyIndexedSearchParameters()))
        {
            searchParameter = parameter;

            return true;
        }

        return false;
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);

        if (IsVisible(parameter, _includePartiallyIndexedSearchParameters()))
        {
            return parameter;
        }

        throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

        if (IsVisible(parameter, _includePartiallyIndexedSearchParameters())) return parameter;

        throw new SearchParameterNotSupportedException(definitionUri);
    }

    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        return _inner.GetSearchParameterHashForResourceType(resourceType);
    }

    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true)
    {
        _inner.AddNewSearchParameters(searchParameters, calculateHash);
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        _inner.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        value = null;

        if (_inner.TryGetSearchParameter(definitionUri, out SearchParameterInfo parameter)
            && IsVisible(parameter, _includePartiallyIndexedSearchParameters()))
        {
            value = parameter;
            return true;
        }

        return false;
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        throw new NotImplementedException();
    }

    private IEnumerable<SearchParameterInfo> GetAllSearchParameters()
    {
        bool includePartiallyIndexed = _includePartiallyIndexedSearchParameters();

        return _inner.AllSearchParameters.Where(p => IsVisible(p, includePartiallyIndexed));
    }

    /// <summary>The single visibility rule behind every accessor. On the opt-in path the answer depends on both
    /// <see cref="SearchParameterInfo.IsSearchable"/> and <see cref="SearchParameterInfo.IsSupported"/>, which are
    /// independent flags that can disagree; routing every accessor through here is what stops one of them from
    /// testing a different combination of the two than the rest.</summary>
    private static bool IsVisible(SearchParameterInfo parameter, bool includePartiallyIndexed)
    {
        return parameter.IsSearchable || (includePartiallyIndexed && parameter.IsSupported);
    }
}
