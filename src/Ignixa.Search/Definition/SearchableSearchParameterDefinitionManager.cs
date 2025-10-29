// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// A SearchParameterDefinitionManager that only returns actively searchable parameters.
/// </summary>
public class SearchableSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly SearchParameterDefinitionManager _inner;

    public SearchableSearchParameterDefinitionManager(SearchParameterDefinitionManager inner)
    {
        EnsureArg.IsNotNull(inner, nameof(inner));

        _inner = inner;
    }

    public IEnumerable<SearchParameterInfo> AllSearchParameters => GetAllSearchParameters();

    public IReadOnlyDictionary<string, string> SearchParameterHashMap => _inner.SearchParameterHashMap;

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        return _inner.GetSearchParameters(resourceType)
            .Where(x => x.IsSearchable);
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

    public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
    {
        _inner.AddNewSearchParameters(searchParameters, calculateHash);
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        _inner.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        _inner.TryGetSearchParameter(definitionUri, out SearchParameterInfo parameter);

        if (parameter.IsSearchable || UsePartialSearchParams(parameter))
        {
            value = parameter;
            return true;
        }

        value = null;
        return false;
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        throw new NotImplementedException();
    }

    private IEnumerable<SearchParameterInfo> GetAllSearchParameters()
    {
        return _inner.AllSearchParameters.Where(x => x.IsSearchable);
    }

    private bool UsePartialSearchParams(SearchParameterInfo parameter)
    {
        return parameter.IsSupported;
    }
}
