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
    /// reindexed - are admitted alongside searchable ones. The index behind such a parameter is half
    /// populated, which makes the result set wrong in both directions rather than merely short: a positive
    /// filter misses resources whose rows do not exist yet, while a negated one (<c>:not</c>,
    /// <c>:missing=true</c>) lowers to the negation of that same presence set and so returns those
    /// not-yet-reindexed resources as matches. Nothing in the response distinguishes such a bundle from a
    /// complete one. Defaults to refusing them.
    /// <para>
    /// No production code constructs this class today - the resolver registrations in
    /// SearchServicesRegistration and SearchOptionsBuilderFactory hand back the raw
    /// <see cref="SearchParameterDefinitionManager"/>, which applies no visibility filter at all - so this
    /// switch currently reaches only tests and direct callers.
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
        return _inner.GetSearchParameters(resourceType).Where(IsVisible);
    }

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        searchParameters = null;

        if (_inner.TryGetSearchParameters(resourceType, out IEnumerable<SearchParameterInfo> innerSearchParameters))
        {
            searchParameters = innerSearchParameters.Where(IsVisible);
            return true;
        }

        return false;
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        searchParameter = null;

        if (_inner.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter) && IsVisible(parameter))
        {
            searchParameter = parameter;

            return true;
        }

        return false;
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(resourceType, code);

        if (IsVisible(parameter))
        {
            return parameter;
        }

        throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        SearchParameterInfo parameter = _inner.GetSearchParameter(definitionUri);

        if (IsVisible(parameter)) return parameter;

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

        if (_inner.TryGetSearchParameter(definitionUri, out SearchParameterInfo parameter) && IsVisible(parameter))
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
        return _inner.AllSearchParameters.Where(IsVisible);
    }

    /// <summary>The single visibility rule behind every accessor. <see cref="SearchParameterInfo.IsSearchable"/>
    /// and <see cref="SearchParameterInfo.IsSupported"/> are independent flags, so any accessor that tested one
    /// without the other would disagree with the rest about the same parameter.</summary>
    private bool IsVisible(SearchParameterInfo parameter)
    {
        return parameter.IsSearchable || (_includePartiallyIndexedSearchParameters() && parameter.IsSupported);
    }
}
