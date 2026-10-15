// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Session;

/// <summary>
/// One folder opened by the tool. The catalog is built on first use and each resource type's index on
/// the first request for that type; both are shared between every CLI verb and MCP tool call in the
/// process, and are safe to request concurrently.
/// </summary>
public sealed class FolderSession
{
    private readonly Lazy<(FolderCatalog Catalog, BuildStats Stats)> _catalog;
    private readonly Lazy<(FolderIndexBuilder Builder, long LoadMilliseconds)> _indexBuilder;
    private readonly Lazy<FolderIndexCache> _cache;
    private readonly ConcurrentDictionary<string, Lazy<ResourceTypeIndex>> _indexes = new(StringComparer.Ordinal);

    /// <param name="rootPath">The folder of FHIR data. It is only ever read.</param>
    /// <param name="schema">The FHIR version the folder is read as.</param>
    /// <param name="loggerFactory">Receives the search indexer's own log output.</param>
    /// <param name="cacheOptions">Where to persist indexes between processes; null (the default) keeps them in memory only.</param>
    public FolderSession(string rootPath, IFhirSchemaProvider schema, ILoggerFactory loggerFactory, IndexCacheOptions? cacheOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        RootPath = Path.GetFullPath(rootPath);
        Schema = schema;
        _catalog = new Lazy<(FolderCatalog, BuildStats)>(() => BuildCatalog(RootPath, schema), LazyThreadSafetyMode.ExecutionAndPublication);

        // Loading the search parameter definitions is the dominant fixed cost of indexing, so it waits for the
        // first index request and is timed separately from the per-type builds.
        _indexBuilder = new Lazy<(FolderIndexBuilder, long)>(
            () =>
            {
                var stopwatch = Stopwatch.StartNew();
                var builder = new FolderIndexBuilder(schema, loggerFactory);
                return (builder, stopwatch.ElapsedMilliseconds);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Validating the cache needs the catalog and the definitions, so it also waits for the first index request.
        IndexCacheOptions options = cacheOptions ?? IndexCacheOptions.Disabled;
        _cache = new Lazy<FolderIndexCache>(
            () => new FolderIndexCache(options, Catalog, schema.Version.ToString(), _indexBuilder.Value.Builder.Definitions),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string RootPath { get; }

    public IFhirSchemaProvider Schema { get; }

    public FolderCatalog Catalog => _catalog.Value.Catalog;

    public BuildStats CatalogStats => _catalog.Value.Stats;

    /// <summary>
    /// Wall time spent loading the search parameter definitions and creating the indexer, paid once on the first
    /// index request and not included in any type's <see cref="ResourceTypeIndex.BuildMilliseconds"/>; null until then.
    /// </summary>
    public long? IndexerLoadMilliseconds => _indexBuilder.IsValueCreated ? _indexBuilder.Value.LoadMilliseconds : null;

    /// <summary>
    /// Counts the folder's files that differ from what the catalog recorded when it was walked (modified, removed or
    /// newly added), by listing the folder and comparing each file's size and last-write time again. Cheap, but it
    /// is a fresh look at the disk each call, so it reports edits made while a long-running session is open.
    /// </summary>
    public int CountStaleFiles() => FolderCatalogBuilder.CountStaleFiles(Catalog);

    /// <summary>
    /// The search parameter definitions every index entry of this session refers to. Loading them is the
    /// one-time indexer cost, so reading this starts that load if no index has been requested yet.
    /// </summary>
    public ISearchParameterDefinitionManager Definitions => _indexBuilder.Value.Builder.Definitions;

    /// <summary>
    /// Directory holding this folder's persisted indexes; null when the cache is off or no index has been requested yet.
    /// </summary>
    public string? CacheDirectory => _cache.IsValueCreated ? _cache.Value.CacheDirectory : null;

    /// <summary>
    /// Problems met while loading or saving persisted indexes (a discarded or corrupt cache, an unwritable
    /// directory); each also means the affected index was built instead of loaded.
    /// </summary>
    public IReadOnlyList<IssueItem> CacheIssues => _cache.IsValueCreated ? _cache.Value.Issues : [];

    /// <summary>Resource types whose index has been built so far, in ordinal order.</summary>
    public IReadOnlyList<string> BuiltIndexTypes => [.. _indexes
        .Where(pair => pair.Value.IsValueCreated)
        .Select(pair => pair.Key)
        .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Returns the index for <paramref name="resourceType"/>, loading it from the persisted cache when a valid
    /// one exists and building it otherwise, on the first request. A type with no catalogued resources yields
    /// an empty index.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="resourceType"/> is not a resource type of the session's FHIR version.</exception>
    public ResourceTypeIndex GetIndex(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        if (!Schema.ResourceTypeNames.Contains(resourceType))
        {
            throw new ArgumentException($"'{resourceType}' is not a {Schema.Version} resource type.", nameof(resourceType));
        }

        return _indexes
            .GetOrAdd(resourceType, type => new Lazy<ResourceTypeIndex>(() => LoadOrBuild(type), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private ResourceTypeIndex LoadOrBuild(string resourceType)
    {
        FolderIndexBuilder builder = _indexBuilder.Value.Builder;
        FolderIndexCache cache = _cache.Value;
        ResourceTypeIndex? cached = cache.TryLoad(resourceType);
        if (cached is not null)
        {
            return cached;
        }

        ResourceTypeIndex built = builder.Build(Catalog, resourceType, CancellationToken.None);
        return built with { CacheWriteMilliseconds = cache.Store(built) };
    }

    private static (FolderCatalog, BuildStats) BuildCatalog(string rootPath, IFhirSchemaProvider schema)
    {
        var stopwatch = Stopwatch.StartNew();
        FolderCatalog catalog = FolderCatalogBuilder.Build(rootPath, schema.ResourceTypeNames, CancellationToken.None);
        long catalogMilliseconds = stopwatch.ElapsedMilliseconds;

        long heap = GC.GetTotalMemory(forceFullCollection: true);
        long workingSet = Environment.WorkingSet;
        return (catalog, new BuildStats(catalogMilliseconds, heap, workingSet));
    }
}
