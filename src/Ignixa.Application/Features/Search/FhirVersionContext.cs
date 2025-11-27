// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Provides version-specific FHIR context with caching.
/// Thread-safe singleton that creates and caches schema providers, search indexers, and search parameter definition managers per FHIR version.
/// </summary>
public sealed class FhirVersionContext : IFhirVersionContext, IDisposable
{
    private readonly ConcurrentDictionary<FhirSpecification, IFhirSchemaProvider> _schemaProviders = new();
    private readonly ConcurrentDictionary<(FhirSpecification, int), CompositeStructureDefinitionSummaryProvider> _compositeProviders = new();
    private readonly ConcurrentDictionary<FhirSpecification, ISearchIndexer> _searchIndexers = new();
    private readonly ConcurrentDictionary<(FhirSpecification, int), ISearchIndexer> _tenantSearchIndexers = new();
    private readonly ConcurrentDictionary<FhirSpecification, ISearchParameterDefinitionManager> _searchParamManagers = new();
    private readonly ConcurrentDictionary<(FhirSpecification, int), CompositeSearchParameterDefinitionManager> _compositeSearchParamManagers = new();
    private readonly ConcurrentDictionary<FhirSpecification, ICompartmentDefinitionManager> _compartmentManagers = new();
    private readonly SemaphoreSlim _indexerLock = new(1, 1);
    private readonly SemaphoreSlim _searchParamLock = new(1, 1);
    private readonly SemaphoreSlim _compartmentLock = new(1, 1);
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPackageResourceRepository? _packageResourceRepository;
    private readonly IPackageResourceProvider? _packageResourceProvider;
    private readonly ICompositeSchemaProviderRegistry? _compositeProviderRegistry;
    private readonly SearchParameterResolutionOptions _searchParameterResolutionOptions;
    private readonly ILogger<FhirVersionContext> _logger;
    private bool _disposed;

    public FhirVersionContext(
        ILoggerFactory loggerFactory,
        SearchParameterResolutionOptions searchParameterResolutionOptions,
        IPackageResourceRepository? packageResourceRepository = null,
        IPackageResourceProvider? packageResourceProvider = null,
        ICompositeSchemaProviderRegistry? compositeProviderRegistry = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _searchParameterResolutionOptions = searchParameterResolutionOptions ?? throw new ArgumentNullException(nameof(searchParameterResolutionOptions));
        _packageResourceRepository = packageResourceRepository;
        _packageResourceProvider = packageResourceProvider;
        _compositeProviderRegistry = compositeProviderRegistry;
        _logger = _loggerFactory.CreateLogger<FhirVersionContext>();
    }

    /// <inheritdoc/>
    public IFhirSchemaProvider GetBaseSchemaProvider(FhirSpecification fhirVersion)
    {
        return _schemaProviders.GetOrAdd(fhirVersion, version =>
        {
            return version switch
            {
                FhirSpecification.Stu3 => new STU3CoreSchemaProvider(),
                FhirSpecification.R4 => new R4CoreSchemaProvider(),
                FhirSpecification.R4B => new R4BCoreSchemaProvider(),
                FhirSpecification.R5 => new R5CoreSchemaProvider(),
                FhirSpecification.R6 => new R6CoreSchemaProvider(),
                _ => throw new ArgumentException($"Unsupported FHIR version: {version}")
            };
        });
    }

    /// <inheritdoc/>
    public IFhirSchemaProvider GetSchemaProvider(FhirSpecification fhirVersion, Nullable<int> tenantId)
    {
        // If no tenant ID provided, return base provider
        if (!tenantId.HasValue)
        {
            _logger.LogTrace(
                "Tenant not available - returning base schema provider for {FhirVersion}",
                fhirVersion);
            return GetBaseSchemaProvider(fhirVersion);
        }

        // If package management dependencies not available, return base provider
        if (_packageResourceRepository == null || _packageResourceProvider == null)
        {
            _logger.LogTrace(
                "Package management dependencies not available - returning base schema provider for {FhirVersion}",
                fhirVersion);
            return GetBaseSchemaProvider(fhirVersion);
        }

        // Return cached composite provider or create new one
        return _compositeProviders.GetOrAdd((fhirVersion, tenantId.Value), key =>
        {
            var (version, tenant) = key;

            _logger.LogDebug(
                "Creating composite schema provider for {FhirVersion}, tenant {TenantId}",
                version,
                tenant);

            // Get base provider for this FHIR version
            var baseProvider = GetBaseSchemaProvider(version);

            // Create composite provider that includes base spec + tenant packages
            var fhirVersionString = version.ToVersionString();
            var compositeProvider = new CompositeStructureDefinitionSummaryProvider(
                baseProvider,
                _packageResourceRepository,
                _packageResourceProvider,
                fhirVersionString,
                _loggerFactory.CreateLogger<CompositeStructureDefinitionSummaryProvider>());

            // Register provider for cache invalidation if registry available
            if (_compositeProviderRegistry != null)
            {
                _compositeProviderRegistry.RegisterProvider(tenant, compositeProvider);
            }

            _logger.LogDebug(
                "Composite schema provider created and cached for {FhirVersion}, tenant {TenantId}",
                version,
                tenant);

            return compositeProvider;
        });
    }

    /// <inheritdoc/>
    public ISearchIndexer GetSearchIndexer(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_searchIndexers.TryGetValue(fhirVersion, out var cachedIndexer))
        {
            return cachedIndexer;
        }

        // IMPORTANT: Call dependency methods BEFORE acquiring lock to avoid nested lock acquisition
        // This prevents potential deadlock where:
        // - Thread A: holds _indexerLock, waits for _searchParamLock (in GetSearchParameterDefinitionManager)
        // - Thread B: holds _searchParamLock, waits for _indexerLock
        var schemaProvider = GetBaseSchemaProvider(fhirVersion);
        var searchParamManager = GetSearchParameterDefinitionManager(fhirVersion);

        // Slow path: create new indexer (synchronous factory with lock)
        _indexerLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_searchIndexers.TryGetValue(fhirVersion, out cachedIndexer))
            {
                return cachedIndexer;
            }

            // Create new search indexer
            // Factory initializes synchronously using pre-generated search parameters
            var indexer = SearchIndexerFactory.CreateInstance(schemaProvider, _loggerFactory, searchParamManager);

            // Cache and return
            _searchIndexers.TryAdd(fhirVersion, indexer);
            return indexer;
        }
        finally
        {
            _indexerLock.Release();
        }
    }

    /// <inheritdoc/>
    public ISearchIndexer GetSearchIndexer(FhirSpecification fhirVersion, Nullable<int> tenantId)
    {
        // If no tenant ID provided, return base indexer
        if (!tenantId.HasValue)
        {
            _logger.LogTrace(
                "Tenant not available - returning base search indexer for {FhirVersion}",
                fhirVersion);
            return GetSearchIndexer(fhirVersion);
        }

        // If package management dependencies not available, return base indexer
        if (_packageResourceRepository == null)
        {
            _logger.LogTrace(
                "Package management dependencies not available - returning base search indexer for {FhirVersion}",
                fhirVersion);
            return GetSearchIndexer(fhirVersion);
        }

        // Fast path: check if already cached
        if (_tenantSearchIndexers.TryGetValue((fhirVersion, tenantId.Value), out var cachedIndexer))
        {
            return cachedIndexer;
        }

        // IMPORTANT: Call dependency methods BEFORE acquiring lock to avoid nested lock acquisition
        var schemaProvider = GetSchemaProvider(fhirVersion, tenantId);
        var searchParamManager = GetSearchParameterDefinitionManager(fhirVersion, tenantId);

        // Slow path: create new tenant-specific indexer (synchronous factory with lock)
        _indexerLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_tenantSearchIndexers.TryGetValue((fhirVersion, tenantId.Value), out cachedIndexer))
            {
                return cachedIndexer;
            }

            _logger.LogDebug(
                "Creating tenant-aware search indexer for {FhirVersion}, tenant {TenantId}",
                fhirVersion,
                tenantId.Value);

            // Create new search indexer with tenant-specific search parameter manager
            // This indexer will use IG-provided search parameters from loaded packages
            var indexer = SearchIndexerFactory.CreateInstance(schemaProvider, _loggerFactory, searchParamManager);

            // Cache and return
            _tenantSearchIndexers.TryAdd((fhirVersion, tenantId.Value), indexer);

            _logger.LogDebug(
                "Tenant-aware search indexer created and cached for {FhirVersion}, tenant {TenantId}",
                fhirVersion,
                tenantId.Value);

            return indexer;
        }
        finally
        {
            _indexerLock.Release();
        }
    }

    /// <inheritdoc/>
    public ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_searchParamManagers.TryGetValue(fhirVersion, out var cachedManager))
        {
            return cachedManager;
        }

        // Slow path: create new manager (synchronous with lock)
        _searchParamLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_searchParamManagers.TryGetValue(fhirVersion, out cachedManager))
            {
                return cachedManager;
            }

            // Create new search parameter definition manager
            // Manager initializes synchronously using pre-generated search parameters
            var schemaProvider = GetBaseSchemaProvider(fhirVersion);
            var logger = _loggerFactory.CreateLogger<SearchParameterDefinitionManager>();
            var manager = new SearchParameterDefinitionManager(schemaProvider, logger);

            // Cache and return
            _searchParamManagers.TryAdd(fhirVersion, manager);
            return manager;
        }
        finally
        {
            _searchParamLock.Release();
        }
    }

    /// <inheritdoc/>
    public ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirSpecification fhirVersion, Nullable<int> tenantId)
    {
        // If no tenant ID provided, return base manager
        if (!tenantId.HasValue)
        {
            _logger.LogTrace(
                "Tenant not available - returning base search parameter manager for {FhirVersion}",
                fhirVersion);
            return GetSearchParameterDefinitionManager(fhirVersion);
        }

        // If package management dependencies not available, return base manager
        if (_packageResourceRepository == null)
        {
            _logger.LogTrace(
                "Package management dependencies not available - returning base search parameter manager for {FhirVersion}",
                fhirVersion);
            return GetSearchParameterDefinitionManager(fhirVersion);
        }

        // Return cached composite manager or create new one
        var compositeManager = _compositeSearchParamManagers.GetOrAdd((fhirVersion, tenantId.Value), key =>
        {
            var (version, tenant) = key;

            _logger.LogDebug(
                "Creating composite search parameter manager for {FhirVersion}, tenant {TenantId}",
                version,
                tenant);

            // Get base manager for this FHIR version
            var baseManager = GetSearchParameterDefinitionManager(version);

            // Get base schema provider for JSON parsing
            var baseSchemaProvider = GetBaseSchemaProvider(version);

            // Create conflict resolver with configured options
            var conflictResolver = new SearchParameterConflictResolver(
                _searchParameterResolutionOptions,
                _loggerFactory.CreateLogger<SearchParameterConflictResolver>());

            // Create composite manager that includes base spec + tenant packages
            var fhirVersionString = version.ToVersionString();
            var manager = new CompositeSearchParameterDefinitionManager(
                baseManager,
                _packageResourceRepository,
                baseSchemaProvider,
                fhirVersionString,
                _loggerFactory.CreateLogger<CompositeSearchParameterDefinitionManager>(),
                conflictResolver,
                _searchParameterResolutionOptions);

            // Initialize eagerly if configured
            if (_searchParameterResolutionOptions.EagerLoadPackageSearchParameters)
            {
                try
                {
                    // Use Task.Run to safely execute async initialization in sync context
                    Task.Run(async () => await manager.InitializeAsync(CancellationToken.None)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to eagerly load package search parameters for {FhirVersion}, tenant {TenantId}",
                        version,
                        tenant);

                    if (_searchParameterResolutionOptions.FailStartupOnEagerLoadError)
                    {
                        throw;
                    }
                }
            }

            _logger.LogDebug(
                "Composite search parameter manager created and cached for {FhirVersion}, tenant {TenantId}",
                version,
                tenant);

            return manager;
        });

        return compositeManager;
    }

    /// <inheritdoc/>
    public ICompartmentDefinitionManager GetCompartmentDefinitionManager(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_compartmentManagers.TryGetValue(fhirVersion, out var cachedManager))
        {
            return cachedManager;
        }

        // Slow path: create new manager (synchronous with lock)
        _compartmentLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_compartmentManagers.TryGetValue(fhirVersion, out cachedManager))
            {
                return cachedManager;
            }

            // Create new compartment definition manager
            // Manager initializes synchronously using pre-generated compartment definitions
            var manager = new CompartmentDefinitionManager(fhirVersion);

            // Cache and return
            _compartmentManagers.TryAdd(fhirVersion, manager);
            return manager;
        }
        finally
        {
            _compartmentLock.Release();
        }
    }

    /// <summary>
    /// Disposes the SemaphoreSlim used for thread synchronization.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _indexerLock?.Dispose();
        _searchParamLock?.Dispose();
        _compartmentLock?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
