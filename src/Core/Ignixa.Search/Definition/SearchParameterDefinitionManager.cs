// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Specification;
using Ignixa.Search.Generated;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;

namespace Ignixa.Search.Definition;

/// <summary>
/// Provides mechanism to access search parameter definition.
/// </summary>
public partial class SearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly IFhirSchemaProvider _modelInfoProvider;
    private readonly ConcurrentDictionary<string, string> _resourceTypeSearchParameterHashMap;
    private readonly object _syncRoot = new();
    private volatile RegistrySnapshot _snapshot = null!;

    public SearchParameterDefinitionManager(
        IFhirSchemaProvider modelInfoProvider,
        ILogger<SearchParameterDefinitionManager> logger)
    {
        EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _modelInfoProvider = modelInfoProvider;
        _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
        TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
        UrlLookup = new ConcurrentDictionary<Uri, SearchParameterInfo>(SearchParameterUriComparer.Instance);

        // Load pre-generated search parameters for instant initialization (<5ms vs 50-200ms)
        SearchParameterInfo[] baseParameters = modelInfoProvider.Version switch
        {
            FhirVersion.R4 => R4SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirVersion.R4B => R4BSearchParameterDefinitions.GetBaseSearchParameters(),
            FhirVersion.R5 => R5SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirVersion.R6 => R6SearchParameterDefinitions.GetBaseSearchParameters(),
            FhirVersion.Stu3 => STU3SearchParameterDefinitions.GetBaseSearchParameters(),
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

        // CRITICAL: Resolve composite search parameter components after loading pre-generated parameters
        // Pre-generated parameters have Component[].DefinitionUrl set, but ResolvedSearchParameter is null
        // We must resolve these references by looking up the component parameters in UrlLookup
        ResolveCompositeComponents(baseParameters, logger);

        ReferenceIdentifierSearchParameterRegistrar.Register(UrlLookup, TypeLookup);
        CalculateSearchParameterHash();
        PublishSnapshot();
    }

    /// <summary>
    /// Expands abstract base resource types to their concrete implementations.
    /// For example, "Resource" expands to all concrete resource types,
    /// "DomainResource" expands to all DomainResource-derived types.
    /// Also includes the base type itself ("Resource", "DomainResource") to support
    /// system-wide searches that use the base type for search parameter lookup.
    /// </summary>
    private IEnumerable<string> ExpandBaseResourceTypes(IReadOnlyList<string> baseResourceTypes, IReadOnlySet<string> concreteResourceTypes)
    {
        var expanded = new HashSet<string>();

        foreach (var baseType in baseResourceTypes)
        {
            if (baseType == "Resource")
            {
                // "Resource" applies to all resource types
                // Also add "Resource" itself to support system-wide search parameter lookup
                expanded.Add("Resource");
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
                // Also add "DomainResource" itself to support compartment searches
                expanded.Add("DomainResource");
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

    /// <summary>
    /// Resolves composite search parameter component references after loading pre-generated parameters.
    /// Pre-generated parameters have Component[].DefinitionUrl set, but ResolvedSearchParameter is null.
    /// This method populates ResolvedSearchParameter by looking up component parameters in UrlLookup.
    /// </summary>
    private void ResolveCompositeComponents(SearchParameterInfo[] parameters, ILogger logger)
    {
        int resolvedCount = 0;
        int unresolvedCount = 0;

        foreach (var parameter in parameters)
        {
            // Skip non-composite parameters
            if (parameter.Component == null || parameter.Component.Count == 0)
            {
                continue;
            }

            // Resolve each component
            for (int i = 0; i < parameter.Component.Count; i++)
            {
                var component = parameter.Component[i];

                // Skip if already resolved (shouldn't happen with pre-generated params, but defensive check)
                if (component.ResolvedSearchParameter != null)
                {
                    continue;
                }

                // Look up component parameter by definition URL
                if (component.DefinitionUrl != null && UrlLookup.TryGetValue(component.DefinitionUrl, out var componentParameter))
                {
                    component.ResolvedSearchParameter = componentParameter;
                    resolvedCount++;
                }
                else
                {
                    unresolvedCount++;
                    logger.LogWarning(
                        "Composite search parameter '{ParameterCode}' (URL: {ParameterUrl}) component {ComponentIndex} " +
                        "references unknown definition URL: {DefinitionUrl}",
                        parameter.Code,
                        parameter.Url,
                        i,
                        component.DefinitionUrl);
                }
            }
        }

        LogCompositeComponentsResolved(logger, resolvedCount, unresolvedCount);
    }

    internal ConcurrentDictionary<Uri, SearchParameterInfo> UrlLookup { get; set; }

    // TypeLookup key is: Resource type, the inner dictionary key is the Search Parameter code.
    internal ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> TypeLookup { get; }

    public IEnumerable<SearchParameterInfo> AllSearchParameters
        => _snapshot.AllSearchParameters;

    /// <summary>
    /// Gets all concrete resource type names that have search parameters defined.
    /// This includes all resource types expanded from abstract base types (Resource, DomainResource).
    /// </summary>
    public IEnumerable<string> ResourceTypeNames
        => _snapshot.ResourceTypeNames;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap => _snapshot.SearchParameterHashes;

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        if (_snapshot.ByResourceType.TryGetValue(resourceType, out ResourceTypeSnapshot value))
        {
            return value.Parameters;
        }

        throw new SearchResourceNotSupportedException(resourceType);
    }

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        searchParameters = null;

        if (_snapshot.ByResourceType.TryGetValue(resourceType, out ResourceTypeSnapshot value))
        {
            searchParameters = value.Parameters;
            return true;
        }

        return false;
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        if (_snapshot.ByResourceType.TryGetValue(resourceType, out ResourceTypeSnapshot lookup) &&
            lookup.ByCode.TryGetValue(code, out SearchParameterInfo searchParameter))
        {
            return searchParameter;
        }

        throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        searchParameter = null;

        return _snapshot.ByResourceType.TryGetValue(resourceType, out ResourceTypeSnapshot searchParameters) &&
               searchParameters.ByCode.TryGetValue(code, out searchParameter);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        if (_snapshot.ByUrl.TryGetValue(definitionUri, out SearchParameterInfo value))
        {
            return value;
        }

        throw new SearchParameterNotSupportedException(definitionUri);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        return _snapshot.ByUrl.TryGetValue(definitionUri, out value);
    }

    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        if (_snapshot.SearchParameterHashes.TryGetValue(resourceType, out string hash)) return hash;

        return null;
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        EnsureArg.IsNotNull(updatedSearchParamHashMap, nameof(updatedSearchParamHashMap));

        lock (_syncRoot)
        {
            foreach (KeyValuePair<string, string> kvp in updatedSearchParamHashMap)
                _resourceTypeSearchParameterHashMap.AddOrUpdate(
                    kvp.Key,
                    kvp.Value,
                    (resourceType, existingValue) => kvp.Value);

            PublishSnapshot();
        }
    }

    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true)
    {
        lock (_syncRoot)
        {
            SearchParameterDefinitionBuilder.Build(
                searchParameters,
                UrlLookup,
                TypeLookup,
                _modelInfoProvider);

            ReferenceIdentifierSearchParameterRegistrar.Register(UrlLookup, TypeLookup);
            if (calculateHash) CalculateSearchParameterHash();
            PublishSnapshot();
        }
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        lock (_syncRoot)
        {
            SearchParameterInfo searchParameterInfo = null;

            if (!UrlLookup.TryRemove(new Uri(url), out searchParameterInfo))
            {
                throw new BadSearchRequestException(string.Format(CultureInfo.CurrentCulture, Resources.CustomSearchParameterNotfound, url));
            }

            // for search parameters with a base resource type we need to delete the search parameter
            // from all derived types as well, so we iterate across all resources
            foreach (string resourceType in TypeLookup.Keys) TypeLookup[resourceType].TryRemove(searchParameterInfo.Code, out SearchParameterInfo removedParam);

            if (searchParameterInfo.IsDerived)
            {
                ReferenceIdentifierSearchParameterRegistrar.Register(UrlLookup, TypeLookup);
            }
            else
            {
                ReferenceIdentifierSearchParameterRegistrar.Unregister(searchParameterInfo, UrlLookup, TypeLookup);
            }

            if (calculateHash) CalculateSearchParameterHash();
            PublishSnapshot();
        }
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

    private void PublishSnapshot()
    {
        var byResourceType = TypeLookup.ToFrozenDictionary(
            pair => pair.Key,
            pair =>
            {
                FrozenDictionary<string, SearchParameterInfo> byCode = pair.Value.ToFrozenDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    StringComparer.Ordinal);
                IEnumerable<SearchParameterInfo> parameters = byCode.Values;
                return new ResourceTypeSnapshot(byCode, parameters);
            },
            StringComparer.Ordinal);

        FrozenDictionary<Uri, SearchParameterInfo> byUrl = UrlLookup.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value,
            SearchParameterUriComparer.Instance);
        IEnumerable<SearchParameterInfo> allSearchParameters = byUrl.Values;
        IEnumerable<string> resourceTypeNames = byResourceType.Keys;
        FrozenDictionary<string, string> searchParameterHashes = _resourceTypeSearchParameterHashMap.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        _snapshot = new RegistrySnapshot(
            byResourceType,
            byUrl,
            searchParameterHashes,
            allSearchParameters,
            resourceTypeNames);
    }

    private sealed record ResourceTypeSnapshot(
        FrozenDictionary<string, SearchParameterInfo> ByCode,
        IEnumerable<SearchParameterInfo> Parameters);

    private sealed record RegistrySnapshot(
        FrozenDictionary<string, ResourceTypeSnapshot> ByResourceType,
        FrozenDictionary<Uri, SearchParameterInfo> ByUrl,
        FrozenDictionary<string, string> SearchParameterHashes,
        IEnumerable<SearchParameterInfo> AllSearchParameters,
        IEnumerable<string> ResourceTypeNames);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolved {ResolvedCount} composite search parameter components, {UnresolvedCount} unresolved")]
    private static partial void LogCompositeComponentsResolved(ILogger logger, int resolvedCount, int unresolvedCount);
}
