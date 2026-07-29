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
/// A SearchParameterDefinitionManager that only returns actively searchable parameters.
/// </summary>
public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly SearchParameterDefinitionManager _inner;
    private readonly Func<bool> _includePartiallyIndexedSearchParameters;

    /// <param name="inner">The definition manager holding every known parameter, searchable or not.</param>
    /// <param name="includePartiallyIndexedSearchParameters">
    /// Decides, per call, whether parameters that are merely <em>supported</em> - registered but not yet
    /// reindexed - should be admitted alongside searchable ones. Applying such a parameter filters on an
    /// index that is still being populated, so it returns too few resources; it is only correct when the
    /// caller has explicitly asked for partially indexed results. Defaults to refusing them.
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
        if (_includePartiallyIndexedSearchParameters())
        {
            return _inner.GetSearchParameters(resourceType)
                .Where(x => x.IsSupported);
        }

        return _inner.GetSearchParameters(resourceType)
            .Where(x => x.IsSearchable);
    }

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        searchParameters = null;

        if (_inner.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo> innerSearchParameters))
        {
            searchParameters = _includePartiallyIndexedSearchParameters()
                ? innerSearchParameters.Where(x => x.IsSupported)
                : innerSearchParameters.Where(x => x.IsSearchable);
            return true;
        }

        return false;
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        searchParameter = null;

        if (_inner.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter) &&
            (parameter.IsSearchable || UsePartialSearchParams(parameter)))
        {
            searchParameter = parameter;

            return true;
        }

        return false;
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);

        if (parameter.IsSearchable || UsePartialSearchParams(parameter))
        {
            return parameter;
        }

        throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

        if (parameter.IsSearchable || UsePartialSearchParams(parameter)) return parameter;

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

        if (_inner.TryGetSearchParameter(definitionUri, out SearchParameterInfo parameter) &&
            (parameter.IsSearchable || UsePartialSearchParams(parameter)))
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
        if (_includePartiallyIndexedSearchParameters())
        {
            return _inner.AllSearchParameters.Where(x => x.IsSupported);
        }

        return _inner.AllSearchParameters.Where(x => x.IsSearchable);
    }

    private bool UsePartialSearchParams(SearchParameterInfo parameter)
    {
        return _includePartiallyIndexedSearchParameters() && parameter.IsSupported;
    }
}
