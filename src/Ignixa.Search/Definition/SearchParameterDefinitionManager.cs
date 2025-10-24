// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using EnsureThat;
using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Specification;
using Ignixa.Search.Generated;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// Provides mechanism to access search parameter definition.
/// </summary>
public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly IFhirSchemaProvider _modelInfoProvider;
    private readonly ConcurrentDictionary<string, string> _resourceTypeSearchParameterHashMap;

    public SearchParameterDefinitionManager(
        IFhirSchemaProvider modelInfoProvider,
        ILogger<SearchParameterDefinitionManager> logger)
    {
        EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _modelInfoProvider = modelInfoProvider;
        _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
        TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
        UrlLookup = new ConcurrentDictionary<Uri, SearchParameterInfo>();

        // Load pre-generated search parameters for instant initialization (<5ms vs 50-200ms)
        SearchParameterInfo[] baseParameters = modelInfoProvider.Version switch
        {
            FhirSpecification.R4 => R4SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirSpecification.R4B => R4BSearchParameterDefinitions.GetBaseSearchParameters(),
            FhirSpecification.R5 => R5SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirSpecification.R6 => R6SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirSpecification.Stu3 => STU3SearchParameterDefinitions.GetBaseSearchParameters(),
            _ => throw new NotSupportedException($"FHIR version {modelInfoProvider.Version} is not supported")
        };

        // Populate lookup dictionaries with proper type hierarchy expansion
        var resourceTypes = _modelInfoProvider.ResourceTypeNames;
        foreach (SearchParameterInfo param in baseParameters)
        {
            // Add to URL lookup
            if (param.Url != null)
            {
                UrlLookup.TryAdd(param.Url, param);
            }

            // Add to type lookup - expand base resource types to all applicable concrete types
            if (param.BaseResourceTypes != null)
            {
                if (param.BaseResourceTypes.Any(x => SearchParameterDefinitionBuilder.ShouldExcludeEntry(x, param.Name, modelInfoProvider)))
                {
                    continue;
                }
                
                var applicableTypes = ExpandBaseResourceTypes(param.BaseResourceTypes, resourceTypes);
                foreach (var resourceType in applicableTypes)
                {
                    var typeLookup = TypeLookup.GetOrAdd(resourceType, _ => new ConcurrentDictionary<string, SearchParameterInfo>());
                    typeLookup.TryAdd(param.Code, param);
                }
            }
        }

        CalculateSearchParameterHash();
    }

    /// <summary>
    /// Expands abstract base resource types to their concrete implementations.
    /// For example, "Resource" expands to all concrete resource types,
    /// "DomainResource" expands to all DomainResource-derived types.
    /// </summary>
    private IEnumerable<string> ExpandBaseResourceTypes(IReadOnlyList<string> baseResourceTypes, IReadOnlySet<string> concreteResourceTypes)
    {
        var expanded = new HashSet<string>();

        foreach (var baseType in baseResourceTypes)
        {
            if (baseType == "Resource")
            {
                // "Resource" applies to all resource types
                foreach (var resourceType in concreteResourceTypes)
                {
                    expanded.Add(resourceType);
                }
            }
            else if (baseType == "DomainResource")
            {
                // "DomainResource" applies to all resource types except abstract types
                // In practice, DomainResource covers all concrete clinical resources
                // We exclude only the truly abstract types that don't appear in the concrete list
                foreach (var resourceType in concreteResourceTypes)
                {
                    expanded.Add(resourceType);
                }
            }
            else
            {
                // Concrete type - add as-is
                expanded.Add(baseType);
            }
        }

        return expanded;
    }

    internal ConcurrentDictionary<Uri, SearchParameterInfo> UrlLookup { get; set; }

    // TypeLookup key is: Resource type, the inner dictionary key is the Search Parameter code.
    internal ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> TypeLookup { get; }

    public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

    /// <summary>
    /// Gets all concrete resource type names that have search parameters defined.
    /// This includes all resource types expanded from abstract base types (Resource, DomainResource).
    /// </summary>
    public IEnumerable<string> ResourceTypeNames => TypeLookup.Keys;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap => new ReadOnlyDictionary<string, string>(_resourceTypeSearchParameterHashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> value))
            return value.Values;

        throw new ResourceNotSupportedException(resourceType);
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> lookup) &&
            lookup.TryGetValue(code, out SearchParameterInfo searchParameter))
            return searchParameter;

        throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        searchParameter = null;

        return TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> searchParameters) &&
               searchParameters.TryGetValue(code, out searchParameter);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        if (UrlLookup.TryGetValue(definitionUri, out SearchParameterInfo value)) return value;

        throw new SearchParameterNotSupportedException(definitionUri);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        return UrlLookup.TryGetValue(definitionUri, out value);
    }

    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        if (_resourceTypeSearchParameterHashMap.TryGetValue(resourceType, out string hash)) return hash;

        return null;
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        EnsureArg.IsNotNull(updatedSearchParamHashMap, nameof(updatedSearchParamHashMap));

        foreach (KeyValuePair<string, string> kvp in updatedSearchParamHashMap)
            _resourceTypeSearchParameterHashMap.AddOrUpdate(
                kvp.Key,
                kvp.Value,
                (resourceType, existingValue) => kvp.Value);
    }

    public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
    {
        SearchParameterDefinitionBuilder.Build(
            searchParameters,
            UrlLookup,
            TypeLookup,
            _modelInfoProvider);

        if (calculateHash) CalculateSearchParameterHash();
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        SearchParameterInfo searchParameterInfo = null;

        if (!UrlLookup.TryRemove(new Uri(url), out searchParameterInfo)) throw new ResourceNotFoundException(string.Format(CultureInfo.CurrentCulture, Resources.CustomSearchParameterNotfound, url));

        // for search parameters with a base resource type we need to delete the search parameter
        // from all derived types as well, so we iterate across all resources
        foreach (string resourceType in TypeLookup.Keys) TypeLookup[resourceType].TryRemove(searchParameterInfo.Code, out SearchParameterInfo removedParam);

        if (calculateHash) CalculateSearchParameterHash();
    }

    private void CalculateSearchParameterHash()
    {
        foreach (string resourceName in TypeLookup.Keys)
        {
            string searchParamHash = TypeLookup[resourceName].Values.CalculateSearchParameterHash();
            _resourceTypeSearchParameterHashMap.AddOrUpdate(
                resourceName,
                searchParamHash,
                (resourceType, existingValue) => searchParamHash);
        }
    }
}
