// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Search.Models;
using Ignixa.Search.Definition.BundleNavigators;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Search.Definition;

/// <summary>
/// Composite search parameter definition manager that merges base FHIR spec parameters with IG-provided parameters.
/// Implements conflict resolution: IG parameters override base parameters when they share the same code.
/// Per-tenant caching ensures different tenants can have different IGs loaded.
/// Phase 1: Synchronous loading with Task.Run pattern (like CompositeStructureDefinitionSummaryProvider).
/// </summary>
public class CompositeSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private readonly ISearchParameterDefinitionManager _baseManager;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly string? _fhirVersion;
    private readonly ILogger<CompositeSearchParameterDefinitionManager> _logger;
    private readonly IFhirSchemaProvider? _schemaProvider;
    private readonly SearchParameterConflictResolver _conflictResolver;
    private readonly SearchParameterResolutionOptions _options;

    // Cache: canonical URL -> SearchParameterInfo (IG parameters only)
    private readonly ConcurrentDictionary<Uri, SearchParameterInfo> _packageSearchParameterCache;

    // Cache: resource type -> merged list (base + IG parameters, IG wins on code conflict)
    private readonly ConcurrentDictionary<string, IEnumerable<SearchParameterInfo>> _packageSearchParametersByResourceType;

    // Cache: (resource type, code) -> SearchParameterInfo (for fast TryGetSearchParameter)
    private readonly ConcurrentDictionary<(string, string), SearchParameterInfo?> _parameterByCodeCache;

    // Cache: canonical URL -> package metadata (for conflict resolution)
    private readonly ConcurrentDictionary<string, PackageMetadata> _packageMetadataCache;

    // Eager loading state: all merged search parameters (base + packages)
    private IEnumerable<SearchParameterInfo>? _allSearchParametersCache;
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeSearchParameterDefinitionManager"/> class.
    /// </summary>
    /// <param name="baseManager">Base search parameter definition manager (from base FHIR spec).</param>
    /// <param name="packageRepository">Repository for querying loaded package resources.</param>
    /// <param name="schemaProvider">Schema provider for converting JSON to ITypedElement (optional, for JSON parsing).</param>
    /// <param name="fhirVersion">Optional: FHIR version to filter package resources (e.g., "4.0.1").</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="conflictResolver">Conflict resolver for multi-IG scenarios (REQUIRED - ensures deterministic conflict resolution).</param>
    /// <param name="options">Configuration options for eager loading and conflict resolution.</param>
    public CompositeSearchParameterDefinitionManager(
        ISearchParameterDefinitionManager baseManager,
        IPackageResourceRepository packageRepository,
        IFhirSchemaProvider? schemaProvider,
        string? fhirVersion,
        ILogger<CompositeSearchParameterDefinitionManager> logger,
        SearchParameterConflictResolver conflictResolver,
        SearchParameterResolutionOptions options)
    {
        _baseManager = baseManager ?? throw new ArgumentNullException(nameof(baseManager));
        _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        _schemaProvider = schemaProvider;
        _fhirVersion = fhirVersion;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _packageSearchParameterCache = new ConcurrentDictionary<Uri, SearchParameterInfo>();
        _packageSearchParametersByResourceType = new ConcurrentDictionary<string, IEnumerable<SearchParameterInfo>>();
        _parameterByCodeCache = new ConcurrentDictionary<(string, string), SearchParameterInfo?>();
        _packageMetadataCache = new ConcurrentDictionary<string, PackageMetadata>();

        _logger.LogInformation(
            "Created CompositeSearchParameterDefinitionManager for FHIR version {FhirVersion} with intelligent conflict resolution enabled (Eager loading: {EagerLoad})",
            _fhirVersion ?? "any",
            _options.EagerLoadPackageSearchParameters);
    }

    /// <summary>
    /// Initializes the manager by eagerly loading all package search parameters.
    /// Should be called once after construction if eager loading is enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
            "Eagerly loading package search parameters for FHIR version {FhirVersion}",
            _fhirVersion ?? "any");

        try
        {
            // Load all SearchParameters from packages in one query
            var packageResources = await _packageRepository.GetAllSearchParametersAsync(
                _fhirVersion,
                cancellationToken).ConfigureAwait(false);

            if (packageResources.Count == 0)
            {
                _logger.LogInformation(
                    "No package search parameters found for FHIR version {FhirVersion}",
                    _fhirVersion ?? "any");
                _allSearchParametersCache = _baseManager.AllSearchParameters;
                _isInitialized = true;
                return;
            }

            // Parse all SearchParameters
            var searchParameters = new List<SearchParameterInfo>(packageResources.Count);
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resourceTypeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageResource in packageResources)
            {
                try
                {
                    var searchParameter = ParseSearchParameter(packageResource.ResourceJson, packageResource);
                    if (searchParameter != null)
                    {
                        searchParameters.Add(searchParameter);

                        // Track package metadata
                        packageIds.Add($"{packageResource.PackageId}@{packageResource.PackageVersion}");

                        // Track resource types covered
                        if (searchParameter.BaseResourceTypes != null)
                        {
                            foreach (var resourceType in searchParameter.BaseResourceTypes)
                            {
                                resourceTypeSet.Add(resourceType);
                            }
                        }

                        // Add to URL cache
                        if (searchParameter.Url != null)
                        {
                            _packageSearchParameterCache.TryAdd(searchParameter.Url, searchParameter);

                            // Cache package metadata for conflict resolution
                            _packageMetadataCache.TryAdd(searchParameter.Url.ToString(), new PackageMetadata
                            {
                                PackageId = packageResource.PackageId,
                                PackageVersion = packageResource.PackageVersion,
                                LoadedDate = packageResource.LoadedDate
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to parse SearchParameter from package {PackageId}@{PackageVersion} (resource ID: {ResourceId})",
                        packageResource.PackageId,
                        packageResource.PackageVersion,
                        packageResource.ResourceId);
                }
            }

            // Merge base parameters with package parameters using conflict resolution
            var baseParameters = _baseManager.AllSearchParameters.ToList();
            var merged = MergeAllSearchParameters(baseParameters, searchParameters);

            // Cache merged result
            _allSearchParametersCache = merged;
            _isInitialized = true;

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation(
                "Eagerly loaded {Count} search parameters from {PackageCount} packages covering {ResourceTypeCount} resource types in {ElapsedMs}ms (FHIR version: {FhirVersion})",
                searchParameters.Count,
                packageIds.Count,
                resourceTypeSet.Count,
                elapsedMs,
                _fhirVersion ?? "any");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error eagerly loading package search parameters for FHIR version {FhirVersion}",
                _fhirVersion ?? "any");

            if (_options.FailStartupOnEagerLoadError)
            {
                throw;
            }

            // Fall back to base parameters only
            _allSearchParametersCache = _baseManager.AllSearchParameters;
            _isInitialized = true;

            _logger.LogWarning(
                "Falling back to base search parameters only due to eager load error");
        }
    }

    /// <inheritdoc/>
    public IEnumerable<SearchParameterInfo> AllSearchParameters
    {
        get
        {
            // If eager loading is enabled and initialization is complete, return cached merged list
            if (_options.EagerLoadPackageSearchParameters && _isInitialized && _allSearchParametersCache != null)
            {
                return _allSearchParametersCache;
            }

            // Fall back to base parameters only (lazy loading mode or not yet initialized)
            return _baseManager.AllSearchParameters;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SearchParameterHashMap => _baseManager.SearchParameterHashMap;

    /// <inheritdoc/>
    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        // Check cache first
        if (_packageSearchParametersByResourceType.TryGetValue(resourceType, out var cached))
        {
            return cached;
        }

        // Load from base + packages and merge
        var baseParameters = _baseManager.GetSearchParameters(resourceType);
        var packageParameters = LoadPackageSearchParametersForResourceType(resourceType);

        // Merge with conflict resolution (IG wins on code conflict)
        var merged = MergeSearchParameters(baseParameters, packageParameters, resourceType);

        // Cache and return
        _packageSearchParametersByResourceType[resourceType] = merged;
        return merged;
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        try
        {
            searchParameters = GetSearchParameters(resourceType);
            return true;
        }
        catch
        {
            searchParameters = Enumerable.Empty<SearchParameterInfo>();
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        // Check cache first
        var cacheKey = (resourceType, code);
        if (_parameterByCodeCache.TryGetValue(cacheKey, out var cached))
        {
            searchParameter = cached!;
            return cached != null;
        }

        // Load all parameters for this resource type (triggers merge)
        var parameters = GetSearchParameters(resourceType);

        // Find by code (case-insensitive)
        searchParameter = parameters.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase))!;

        // Cache result (including null)
        _parameterByCodeCache[cacheKey] = searchParameter;

        return searchParameter != null;
    }

    /// <inheritdoc/>
    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        if (TryGetSearchParameter(resourceType, code, out var searchParameter))
        {
            return searchParameter;
        }

        throw new InvalidOperationException($"Search parameter '{code}' not found for resource type '{resourceType}'");
    }

    /// <inheritdoc/>
    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        // Check package cache first (IG parameters)
        if (_packageSearchParameterCache.TryGetValue(definitionUri, out value!))
        {
            return true;
        }

        // Check if this is a package parameter (not yet loaded)
        var packageParameter = TryGetFromPackages(definitionUri);
        if (packageParameter != null)
        {
            _packageSearchParameterCache[definitionUri] = packageParameter;
            value = packageParameter;
            return true;
        }

        // Fall back to base manager
        return _baseManager.TryGetSearchParameter(definitionUri, out value!);
    }

    /// <inheritdoc/>
    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
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
        // Delegate to base manager
        // Phase 2: Could recalculate hash including IG parameters
        _baseManager.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    }

    /// <inheritdoc/>
    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        // Delegate to base manager
        // Phase 2: Could include IG parameters in hash calculation
        return _baseManager.GetSearchParameterHashForResourceType(resourceType);
    }

    /// <inheritdoc/>
    public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
    {
        // Phase 1: Not implemented - delegate to base manager
        // Phase 2: Could support adding custom search parameters at runtime
        throw new NotImplementedException(
            "AddNewSearchParameters is not supported in CompositeSearchParameterDefinitionManager Phase 1. " +
            "Use base SearchParameterDefinitionManager for runtime parameter addition.");
    }

    /// <inheritdoc/>
    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        // Phase 1: Not implemented
        // Phase 2: Could support removing parameters
        throw new NotImplementedException(
            "DeleteSearchParameter is not supported in CompositeSearchParameterDefinitionManager Phase 1. " +
            "Use base SearchParameterDefinitionManager for runtime parameter deletion.");
    }

    /// <summary>
    /// Clears all caches.
    /// Should be called when packages are loaded/unloaded for this tenant.
    /// </summary>
    public void ClearCache()
    {
        _packageSearchParameterCache.Clear();
        _packageSearchParametersByResourceType.Clear();
        _parameterByCodeCache.Clear();
        _packageMetadataCache.Clear();

        _logger.LogInformation(
            "Cleared CompositeSearchParameterDefinitionManager cache (FHIR version: {FhirVersion})",
            _fhirVersion ?? "any");
    }

    /// <summary>
    /// Gets the origin (package ID and version) of the currently active SearchParameter for a given code.
    /// Returns null if parameter is from base spec or not found.
    /// </summary>
    public PackageMetadata? GetSearchParameterOrigin(string resourceType, string code)
    {
        if (!TryGetSearchParameter(resourceType, code, out var param))
        {
            return null;
        }

        if (param.Url == null)
        {
            return null;
        }

        if (_packageMetadataCache.TryGetValue(param.Url.ToString(), out var metadata))
        {
            return metadata;
        }

        return null;
    }

    /// <summary>
    /// Loads SearchParameters from packages for a specific resource type.
    /// Uses Task.Run pattern to bridge async/sync gap (Phase 1 limitation).
    /// </summary>
    private IReadOnlyList<SearchParameterInfo> LoadPackageSearchParametersForResourceType(string resourceType)
    {
        try
        {
            // Use Task.Run to safely block on async operation
            var packageResources = Task.Run(async () =>
                await _packageRepository.GetSearchParametersByResourceTypeAsync(
                    resourceType,
                    _fhirVersion,
                    CancellationToken.None)).GetAwaiter().GetResult();

            if (packageResources.Count == 0)
            {
                return Array.Empty<SearchParameterInfo>();
            }

            var searchParameters = new List<SearchParameterInfo>(packageResources.Count);

            foreach (var packageResource in packageResources)
            {
                try
                {
                    // Parse JSON to SearchParameterInfo
                    var searchParameter = ParseSearchParameter(packageResource.ResourceJson, packageResource);
                    if (searchParameter != null)
                    {
                        searchParameters.Add(searchParameter);

                        // Add to URL cache
                        if (searchParameter.Url != null)
                        {
                            _packageSearchParameterCache.TryAdd(searchParameter.Url, searchParameter);

                            // Cache package metadata for conflict resolution
                            _packageMetadataCache.TryAdd(searchParameter.Url.ToString(), new PackageMetadata
                            {
                                PackageId = packageResource.PackageId,
                                PackageVersion = packageResource.PackageVersion,
                                LoadedDate = packageResource.LoadedDate
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to parse SearchParameter from package {PackageId}@{PackageVersion} (resource ID: {ResourceId})",
                        packageResource.PackageId,
                        packageResource.PackageVersion,
                        packageResource.ResourceId);
                }
            }

            _logger.LogDebug(
                "Loaded {Count} SearchParameters for {ResourceType} from packages (FHIR version: {FhirVersion})",
                searchParameters.Count,
                resourceType,
                _fhirVersion ?? "any");

            return searchParameters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error loading SearchParameters for {ResourceType} from package repository (FHIR version: {FhirVersion})",
                resourceType,
                _fhirVersion ?? "any");
            return Array.Empty<SearchParameterInfo>();
        }
    }

    /// <summary>
    /// Tries to load a SearchParameter by canonical URL from packages.
    /// Returns the newest version if multiple exist.
    /// </summary>
    private SearchParameterInfo? TryGetFromPackages(Uri canonical)
    {
        try
        {
            // Use Task.Run to safely block on async operation
            var packageResources = Task.Run(async () =>
                await _packageRepository.GetSearchParametersByCanonicalAsync(
                    canonical.ToString(),
                    _fhirVersion,
                    CancellationToken.None)).GetAwaiter().GetResult();

            if (packageResources.Count == 0)
            {
                return null;
            }

            // Use the first (newest) version
            var packageResource = packageResources[0];

            var searchParameter = ParseSearchParameter(packageResource.ResourceJson, packageResource);

            if (searchParameter != null)
            {
                _logger.LogDebug(
                    "Loaded SearchParameter {Canonical} from package {PackageId}@{PackageVersion}",
                    canonical,
                    packageResource.PackageId,
                    packageResource.PackageVersion);
            }

            return searchParameter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error loading SearchParameter {Canonical} from package repository (FHIR version: {FhirVersion})",
                canonical,
                _fhirVersion ?? "any");
            return null;
        }
    }

    /// <summary>
    /// Parses a SearchParameter from JSON using SearchParameterNavigator.
    /// Requires a schema provider to convert JSON to ITypedElement.
    /// </summary>
    private SearchParameterInfo? ParseSearchParameter(string resourceJson, object contextForLogging)
    {
        try
        {
            // Parse JSON to ResourceJsonNode (wrapper around JsonNode with FHIR resource semantics)
            var resourceNode = ResourceJsonNode.Parse(resourceJson);
            if (resourceNode == null)
            {
                _logger.LogWarning("Failed to parse SearchParameter JSON: null result");
                return null;
            }

            // Convert to ISourceNode, then to ITypedElement using schema provider
            // Phase 1: If no schema provider available, skip (shouldn't happen in normal usage)
            if (_schemaProvider == null)
            {
                _logger.LogWarning("No schema provider available to parse SearchParameter JSON");
                return null;
            }

            var sourceNode = resourceNode.ToSourceNode();
            var typedElement = sourceNode.ToTypedElement(_schemaProvider);

            // Use SearchParameterNavigator to extract properties
            var navigator = new SearchParameterNavigator(typedElement);

            // Create SearchParameterInfo
            return new SearchParameterInfo(navigator);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SearchParameter JSON");
            return null;
        }
    }

    /// <summary>
    /// Merges base and package search parameters with conflict resolution.
    /// CRITICAL CONFLICT RESOLUTION RULES:
    /// 1. Exact URL match -> use that specific version (both can coexist)
    /// 2. Code match (different URLs) -> Use conflict resolver (priority or semantic version)
    /// 3. New code -> ADD IG parameter
    /// Conflict resolver is REQUIRED - ensures deterministic resolution regardless of load order.
    /// </summary>
    private IEnumerable<SearchParameterInfo> MergeSearchParameters(
        IEnumerable<SearchParameterInfo> baseParameters,
        IReadOnlyList<SearchParameterInfo> packageParameters,
        string resourceType)
    {
        if (packageParameters.Count == 0)
        {
            // No IG parameters, return base only
            return baseParameters;
        }

        // Build lookup: code -> List<SearchParameterInfo> (for conflict detection)
        var parametersByCode = new Dictionary<string, List<SearchParameterInfo>>(StringComparer.OrdinalIgnoreCase);

        // Start with base parameters
        foreach (var baseParam in baseParameters)
        {
            if (!parametersByCode.TryGetValue(baseParam.Code, out var list))
            {
                list = new List<SearchParameterInfo>();
                parametersByCode[baseParam.Code] = list;
            }

            list.Add(baseParam);

            // Ensure base parameters have metadata for conflict resolution logging
            if (baseParam.Url != null && !_packageMetadataCache.ContainsKey(baseParam.Url.ToString()))
            {
                _packageMetadataCache.TryAdd(baseParam.Url.ToString(), new PackageMetadata
                {
                    PackageId = GetBaseFhirPackageId(),
                    PackageVersion = GetBaseFhirPackageVersion(),
                    LoadedDate = DateTimeOffset.MinValue
                });
            }
        }

        // Add IG parameters
        foreach (var packageParam in packageParameters)
        {
            if (!parametersByCode.TryGetValue(packageParam.Code, out var list))
            {
                list = new List<SearchParameterInfo>();
                parametersByCode[packageParam.Code] = list;
            }

            // Check for exact URL match (duplicate)
            var duplicate = list.FirstOrDefault(p =>
                p.Url != null && packageParam.Url != null && p.Url.Equals(packageParam.Url));

            if (duplicate != null)
            {
                _logger.LogDebug(
                    "Skipping duplicate SearchParameter {Code} for {ResourceType} (URL: {Url})",
                    packageParam.Code,
                    resourceType,
                    packageParam.Url);
                continue;
            }

            list.Add(packageParam);
        }

        // Resolve conflicts for each code
        var result = new Dictionary<string, SearchParameterInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, candidates) in parametersByCode)
        {
            if (candidates.Count == 1)
            {
                // No conflict
                result[code] = candidates[0];
            }
            else
            {
                // Conflict: multiple parameters with same code
                // Use intelligent conflict resolution (required - no fallback to legacy behavior)
                var winner = _conflictResolver.ResolveConflict(
                    candidates,
                    code,
                    resourceType,
                    _packageMetadataCache);

                result[code] = winner;
            }
        }

        return result.Values;
    }

    /// <summary>
    /// Merges all base and package search parameters with per-resource-type conflict resolution.
    /// Used during eager loading to create a complete merged list of all search parameters.
    /// CRITICAL: Resolves conflicts PER RESOURCE TYPE (not globally) to match FHIR semantics.
    /// Example: "identifier" conflict on Patient is resolved independently from "identifier" conflict on Organization.
    /// </summary>
    private IEnumerable<SearchParameterInfo> MergeAllSearchParameters(
        IReadOnlyList<SearchParameterInfo> baseParameters,
        IReadOnlyList<SearchParameterInfo> packageParameters)
    {
        if (packageParameters.Count == 0)
        {
            return baseParameters;
        }

        // Build lookup: (code, resourceType) -> List<SearchParameterInfo>
        // This ensures conflicts are resolved independently per resource type
        var parametersByCodeAndResourceType =
            new Dictionary<(string code, string resourceType), List<SearchParameterInfo>>(
                new CodeResourceTypeTupleComparer());

        // Start with base parameters
        foreach (var baseParam in baseParameters)
        {
            // Base parameters can apply to multiple resource types via BaseResourceTypes
            // Add them for each resource type they apply to
            if (baseParam.BaseResourceTypes != null && baseParam.BaseResourceTypes.Count > 0)
            {
                foreach (var resourceType in baseParam.BaseResourceTypes)
                {
                    var key = (baseParam.Code, resourceType);
                    if (!parametersByCodeAndResourceType.TryGetValue(key, out var list))
                    {
                        list = new List<SearchParameterInfo>();
                        parametersByCodeAndResourceType[key] = list;
                    }
                    list.Add(baseParam);
                }
            }

            // Ensure base parameters have metadata for conflict resolution logging
            if (baseParam.Url != null && !_packageMetadataCache.ContainsKey(baseParam.Url.ToString()))
            {
                _packageMetadataCache.TryAdd(baseParam.Url.ToString(), new PackageMetadata
                {
                    PackageId = GetBaseFhirPackageId(),
                    PackageVersion = GetBaseFhirPackageVersion(),
                    LoadedDate = DateTimeOffset.MinValue
                });
            }
        }

        // Add package parameters
        foreach (var packageParam in packageParameters)
        {
            // Package parameters can also apply to multiple resource types
            if (packageParam.BaseResourceTypes != null && packageParam.BaseResourceTypes.Count > 0)
            {
                foreach (var resourceType in packageParam.BaseResourceTypes)
                {
                    var key = (packageParam.Code, resourceType);
                    if (!parametersByCodeAndResourceType.TryGetValue(key, out var list))
                    {
                        list = new List<SearchParameterInfo>();
                        parametersByCodeAndResourceType[key] = list;
                    }

                    // Check for exact URL match (duplicate)
                    var duplicate = list.FirstOrDefault(p =>
                        p.Url != null && packageParam.Url != null && p.Url.Equals(packageParam.Url));

                    if (duplicate != null)
                    {
                        _logger.LogDebug(
                            "Skipping duplicate SearchParameter {Code} for {ResourceType} (URL: {Url})",
                            packageParam.Code,
                            resourceType,
                            packageParam.Url);
                        continue;
                    }

                    list.Add(packageParam);
                }
            }
        }

        // Resolve conflicts per (code, resourceType) tuple
        var result = new List<SearchParameterInfo>(parametersByCodeAndResourceType.Count);

        foreach (var ((code, resourceType), candidates) in parametersByCodeAndResourceType)
        {
            if (candidates.Count == 1)
            {
                result.Add(candidates[0]);
            }
            else
            {
                // Conflict: multiple parameters with same code for same resource type
                // Use intelligent conflict resolution (per-resource-type)
                var winner = _conflictResolver.ResolveConflict(
                    candidates,
                    code,
                    resourceType,  // ✅ PER-RESOURCE-TYPE resolution (not global "All")
                    _packageMetadataCache);

                result.Add(winner);
            }
        }

        return result;
    }

    /// <summary>
    /// Helper comparer for (code, resourceType) tuples with case-insensitive comparison.
    /// </summary>
    private class CodeResourceTypeTupleComparer : EqualityComparer<(string code, string resourceType)>
    {
        public override bool Equals((string code, string resourceType) x, (string code, string resourceType) y)
        {
            return string.Equals(x.code, y.code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.resourceType, y.resourceType, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode((string code, string resourceType) obj)
        {
            return HashCode.Combine(
                obj.code.ToUpperInvariant(),
                obj.resourceType.ToUpperInvariant());
        }
    }

    /// <summary>
    /// Gets the package ID to use for base FHIR parameters.
    /// Returns a canonical package ID based on FHIR version.
    /// </summary>
    private string GetBaseFhirPackageId()
    {
        if (string.IsNullOrEmpty(_fhirVersion))
        {
            return "hl7.fhir.core";
        }

        // Map FHIR version to canonical package ID
        // Note: _fhirVersion can be "4.3" or "4.3.0" depending on source
        var majorMinor = _fhirVersion.Length >= 3 ? _fhirVersion.Substring(0, 3) : _fhirVersion;
        return majorMinor switch
        {
            "3.0" => "hl7.fhir.r3.core",
            "4.0" => "hl7.fhir.r4.core",
            "4.3" => "hl7.fhir.r4b.core",
            "5.0" => "hl7.fhir.r5.core",
            "6.0" => "hl7.fhir.r6.core",
            _ => $"hl7.fhir.r{_fhirVersion}.core"
        };
    }

    /// <summary>
    /// Gets the package version to use for base FHIR parameters.
    /// Returns a proper semantic version string (e.g., "4.3.0" not "4.3").
    /// </summary>
    private string GetBaseFhirPackageVersion()
    {
        if (string.IsNullOrEmpty(_fhirVersion))
        {
            return "0.0.0";
        }

        // Map FHIR version to proper semantic version for package metadata
        // Note: _fhirVersion can be "4.3" or "4.3.0" depending on source
        var majorMinor = _fhirVersion.Length >= 3 ? _fhirVersion.Substring(0, 3) : _fhirVersion;
        return majorMinor switch
        {
            "3.0" => "3.0.2",  // STU3 official version
            "4.0" => "4.0.1",  // R4 official version
            "4.3" => "4.3.0",  // R4B official version
            "5.0" => "5.0.0",  // R5 official version
            "6.0" => "6.0.0",  // R6 official version
            _ => _fhirVersion.Contains('.', StringComparison.Ordinal) ? _fhirVersion : $"{_fhirVersion}.0.0"
        };
    }
}
