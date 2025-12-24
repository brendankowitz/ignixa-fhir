// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;

using SearchParamInfo = Ignixa.Search.Models.SearchParameterInfo;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Composite search parameter definition manager that merges base FHIR spec parameters with IG-provided parameters.
/// Uses ConformanceState as the source of truth for package search parameters - conflict resolution is already
/// handled during event replay in ConformanceState, eliminating redundant database queries and resolution logic.
/// </summary>
public class CompositeSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly ISearchParameterDefinitionManager _baseManager;
    private readonly ConformanceState _conformanceState;
    private readonly string? _fhirVersion;
    private readonly ILogger<CompositeSearchParameterDefinitionManager> _logger;
    private readonly SearchParameterResolutionOptions _options;

    private readonly ConcurrentDictionary<Uri, SearchParamInfo> _packageSearchParameterCache = new();
    private readonly ConcurrentDictionary<string, IEnumerable<SearchParamInfo>> _packageSearchParametersByResourceType = new();
    private readonly ConcurrentDictionary<(string, string), SearchParamInfo?> _parameterByCodeCache = new();

    private volatile bool _isInitialized;
    private Lazy<IReadOnlyDictionary<string, string>> _searchParameterHashMapCache;

    public CompositeSearchParameterDefinitionManager(
        ISearchParameterDefinitionManager baseManager,
        ConformanceState conformanceState,
        string? fhirVersion,
        ILogger<CompositeSearchParameterDefinitionManager> logger,
        SearchParameterResolutionOptions options)
    {
        _baseManager = baseManager ?? throw new ArgumentNullException(nameof(baseManager));
        _conformanceState = conformanceState ?? throw new ArgumentNullException(nameof(conformanceState));
        _fhirVersion = fhirVersion;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _searchParameterHashMapCache = new Lazy<IReadOnlyDictionary<string, string>>(
            () => _baseManager.SearchParameterHashMap,
            LazyThreadSafetyMode.ExecutionAndPublication);

        _logger.LogInformation(
            "Created CompositeSearchParameterDefinitionManager for FHIR version {FhirVersion} using ConformanceState as source of truth (Eager loading: {EagerLoad})",
            _fhirVersion ?? "any",
            _options.EagerLoadPackageSearchParameters);
    }

    /// <summary>
    /// Initializes the manager by loading search parameters from ConformanceState.
    /// ConformanceState already has the resolved winners from event replay - no conflict resolution needed.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EagerLoadPackageSearchParameters)
        {
            _logger.LogDebug("Eager loading disabled - skipping initialization");
            _isInitialized = true;
            return;
        }

        if (_isInitialized)
        {
            _logger.LogDebug("Already initialized - skipping");
            return;
        }

        var startTime = DateTime.UtcNow;
        _logger.LogInformation(
            "Loading package search parameters from ConformanceState for FHIR version {FhirVersion}",
            _fhirVersion ?? "any");

        try
        {
            await WaitForConformanceStateInitializationAsync(cancellationToken);

            LoadFromConformanceState();
            _isInitialized = true;

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation(
                "Loaded search parameters from ConformanceState in {ElapsedMs}ms (FHIR version: {FhirVersion})",
                elapsedMs,
                _fhirVersion ?? "any");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Error loading package search parameters from ConformanceState for FHIR version {FhirVersion}",
                _fhirVersion ?? "any");

            if (_options.FailStartupOnEagerLoadError)
            {
                throw;
            }

            _isInitialized = true;
            _logger.LogWarning("Falling back to base search parameters only due to ConformanceState load error");
        }
    }

    private async Task WaitForConformanceStateInitializationAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 50;
        const int retryDelayMs = 100;

        for (int i = 0; i < maxRetries && !_conformanceState.IsInitialized; i++)
        {
            _logger.LogDebug(
                "Waiting for ConformanceState initialization (attempt {Attempt}/{MaxRetries})",
                i + 1,
                maxRetries);
            await Task.Delay(retryDelayMs, cancellationToken);
        }

        if (!_conformanceState.IsInitialized)
        {
            throw new InvalidOperationException(
                $"ConformanceState was not initialized after waiting {maxRetries * retryDelayMs}ms");
        }
    }

    private void LoadFromConformanceState()
    {
        var allParams = _conformanceState.AllSearchParameters;

        if (allParams.Count == 0)
        {
            _logger.LogInformation(
                "No package search parameters found in ConformanceState for FHIR version {FhirVersion}",
                _fhirVersion ?? "any");
            return;
        }

        var baseParameters = _baseManager.AllSearchParameters.ToList();
        var baseByResourceTypeAndCode = baseParameters
            .SelectMany(p => (p.BaseResourceTypes ?? Array.Empty<string>())
                .Select(rt => (ResourceType: rt, Code: p.Code, Param: p)))
            .ToLookup(x => (x.ResourceType, x.Code), x => x.Param,
                new ResourceTypeCodeComparer());

        var packageParamsByResourceType = new Dictionary<string, List<SearchParamInfo>>(StringComparer.OrdinalIgnoreCase);
        var packageCount = 0;
        var resourceTypeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in allParams)
        {
            var asp = kvp.Value;

            if (asp.Status != SearchParameterStatus.Enabled && asp.Status != SearchParameterStatus.Pending)
            {
                continue;
            }

            var searchParamInfo = ConvertToSearchParameterInfo(asp);
            packageCount++;
            resourceTypeSet.Add(asp.ResourceType);

            if (searchParamInfo.Url is not null)
            {
                _packageSearchParameterCache.TryAdd(searchParamInfo.Url, searchParamInfo);
            }

            if (!packageParamsByResourceType.TryGetValue(asp.ResourceType, out var list))
            {
                list = [];
                packageParamsByResourceType[asp.ResourceType] = list;
            }
            list.Add(searchParamInfo);
        }

        var allResourceTypes = baseParameters
            .SelectMany(p => p.BaseResourceTypes ?? Array.Empty<string>())
            .Union(resourceTypeSet, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var resourceType in allResourceTypes)
        {
            var merged = new Dictionary<string, SearchParamInfo>(StringComparer.OrdinalIgnoreCase);

            var baseForType = baseParameters
                .Where(p => p.BaseResourceTypes?.Contains(resourceType, StringComparer.OrdinalIgnoreCase) == true);
            foreach (var baseParam in baseForType)
            {
                merged[baseParam.Code] = baseParam;
            }

            if (packageParamsByResourceType.TryGetValue(resourceType, out var packageParams))
            {
                foreach (var packageParam in packageParams)
                {
                    merged[packageParam.Code] = packageParam;
                }
            }

            _packageSearchParametersByResourceType[resourceType] = merged.Values.ToList();
        }

        _logger.LogInformation(
            "Loaded {PackageCount} package search parameters covering {ResourceTypeCount} resource types from ConformanceState",
            packageCount,
            resourceTypeSet.Count);
    }

    private static SearchParamInfo ConvertToSearchParameterInfo(ActiveSearchParameter asp)
    {
        var components = asp.Components?.Select(c =>
            new SearchParameterComponentInfo(
                string.IsNullOrEmpty(c.DefinitionUrl) ? null : new Uri(c.DefinitionUrl),
                c.Expression)).ToArray()
            ?? Array.Empty<SearchParameterComponentInfo>();

        var searchParamInfo = new SearchParamInfo(
            name: asp.Name ?? asp.Code,
            code: asp.Code,
            searchParamType: asp.ParamType,
            url: new Uri(asp.Canonical),
            components: components,
            expression: asp.Expression,
            targetResourceTypes: asp.TargetResourceTypes,
            baseResourceTypes: [asp.ResourceType],
            description: asp.Description);

        if (!string.IsNullOrEmpty(asp.OverridesCanonical))
        {
            searchParamInfo.OverridesUrl = new Uri(asp.OverridesCanonical);
        }

        return searchParamInfo;
    }

    /// <inheritdoc/>
    public IEnumerable<SearchParamInfo> AllSearchParameters
    {
        get
        {
            if (_options.EagerLoadPackageSearchParameters && _isInitialized)
            {
                return _packageSearchParametersByResourceType.Values
                    .SelectMany(list => list)
                    .GroupBy(p => p.OverridesUrl ?? p.Url)
                    .Select(g => g.First())
                    .ToList();
            }

            return _baseManager.AllSearchParameters;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SearchParameterHashMap => _searchParameterHashMapCache.Value;

    /// <inheritdoc/>
    public IEnumerable<SearchParamInfo> GetSearchParameters(string resourceType)
    {
        if (_packageSearchParametersByResourceType.TryGetValue(resourceType, out var cached))
        {
            return cached;
        }

        if (!_isInitialized || !_conformanceState.IsInitialized)
        {
            return _baseManager.GetSearchParameters(resourceType);
        }

        var baseParameters = _baseManager.GetSearchParameters(resourceType);
        var merged = new Dictionary<string, SearchParamInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var baseParam in baseParameters)
        {
            merged[baseParam.Code] = baseParam;
        }

        foreach (var kvp in _conformanceState.AllSearchParameters)
        {
            var asp = kvp.Value;
            if (string.Equals(asp.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase) &&
                (asp.Status == SearchParameterStatus.Enabled || asp.Status == SearchParameterStatus.Pending))
            {
                var searchParamInfo = ConvertToSearchParameterInfo(asp);
                merged[searchParamInfo.Code] = searchParamInfo;

                if (searchParamInfo.Url is not null)
                {
                    _packageSearchParameterCache.TryAdd(searchParamInfo.Url, searchParamInfo);
                }
            }
        }

        var result = merged.Values.ToList();
        _packageSearchParametersByResourceType[resourceType] = result;
        return result;
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParamInfo> searchParameters)
    {
        try
        {
            searchParameters = GetSearchParameters(resourceType);
            return true;
        }
        catch
        {
            searchParameters = Enumerable.Empty<SearchParamInfo>();
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameter(string resourceType, string code, out SearchParamInfo searchParameter)
    {
        var cacheKey = (resourceType, code);
        if (_parameterByCodeCache.TryGetValue(cacheKey, out var cached))
        {
            searchParameter = cached!;
            return cached is not null;
        }

        var parameters = GetSearchParameters(resourceType);
        searchParameter = parameters.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))!;

        _parameterByCodeCache[cacheKey] = searchParameter;
        return searchParameter is not null;
    }

    /// <inheritdoc/>
    public SearchParamInfo GetSearchParameter(string resourceType, string code)
    {
        if (TryGetSearchParameter(resourceType, code, out var searchParameter))
        {
            return searchParameter;
        }

        throw new InvalidOperationException($"Search parameter '{code}' not found for resource type '{resourceType}'");
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameter(Uri definitionUri, out SearchParamInfo value)
    {
        if (_packageSearchParameterCache.TryGetValue(definitionUri, out value!))
        {
            return true;
        }

        return _baseManager.TryGetSearchParameter(definitionUri, out value!);
    }

    /// <inheritdoc/>
    public SearchParamInfo GetSearchParameter(Uri definitionUri)
    {
        if (TryGetSearchParameter(definitionUri, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Search parameter with URL '{definitionUri}' not found");
    }

    /// <inheritdoc/>
    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        _baseManager.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    }

    /// <inheritdoc/>
    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        return _baseManager.GetSearchParameterHashForResourceType(resourceType);
    }

    /// <inheritdoc/>
    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true)
    {
        throw new NotImplementedException(
            "AddNewSearchParameters is not supported in CompositeSearchParameterDefinitionManager. " +
            "Use base SearchParameterDefinitionManager for runtime parameter addition.");
    }

    /// <inheritdoc/>
    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        throw new NotImplementedException(
            "DeleteSearchParameter is not supported in CompositeSearchParameterDefinitionManager. " +
            "Use base SearchParameterDefinitionManager for runtime parameter deletion.");
    }

    /// <summary>
    /// Clears all caches. Should be called when ConformanceState changes.
    /// Does NOT reset _isInitialized - the state comes from ConformanceState.
    /// </summary>
    public void ClearCache()
    {
        _packageSearchParameterCache.Clear();
        _packageSearchParametersByResourceType.Clear();
        _parameterByCodeCache.Clear();

        _searchParameterHashMapCache = new Lazy<IReadOnlyDictionary<string, string>>(
            () => _baseManager.SearchParameterHashMap,
            LazyThreadSafetyMode.ExecutionAndPublication);

        _logger.LogInformation(
            "Cleared CompositeSearchParameterDefinitionManager cache (FHIR version: {FhirVersion})",
            _fhirVersion ?? "any");
    }

    /// <summary>
    /// Reloads search parameters from ConformanceState after cache is cleared.
    /// </summary>
    public void ReloadFromConformanceState()
    {
        ClearCache();

        if (_conformanceState.IsInitialized)
        {
            LoadFromConformanceState();
        }
    }

    private sealed class ResourceTypeCodeComparer : IEqualityComparer<(string ResourceType, string Code)>
    {
        public bool Equals((string ResourceType, string Code) x, (string ResourceType, string Code) y)
        {
            return string.Equals(x.ResourceType, y.ResourceType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string ResourceType, string Code) obj)
        {
            return HashCode.Combine(
                obj.ResourceType.ToUpperInvariant(),
                obj.Code.ToUpperInvariant());
        }
    }
}
