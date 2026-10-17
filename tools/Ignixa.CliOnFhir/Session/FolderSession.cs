// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Validation;
using Ignixa.Search.Definition;
using Ignixa.Search.InMemory;
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
    private readonly Lazy<SearchQueryParser> _queryParser;
    private readonly Lazy<Dictionary<(string ResourceType, string Id), CatalogEntry>> _entriesByReference;
    private readonly Lazy<Dictionary<(string ResourceType, string Digest), CatalogEntry>> _entriesByDigest;
    private readonly Lazy<BundleReferenceResolver> _bundleReferences;
    private readonly Lazy<InMemoryCompartmentMembership> _compartments;
    private readonly Lazy<HashSet<string>> _catalogTypes;
    private readonly Lazy<CompartmentIndex> _patientCompartments;
    private readonly Lazy<FolderKinds> _kinds;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _staleGate = new();
    private readonly Lock _deliveredGate = new();
    private readonly HashSet<IssueItem> _deliveredCacheEvents = [];
    private StaleFilesCount? _lastStaleCount;
    private long _lastStaleCountTimestamp;
    private readonly ConcurrentDictionary<string, Lazy<ResourceTypeIndex>> _indexes = new(StringComparer.Ordinal);

    /// <param name="rootPath">The folder of FHIR data. It is only ever read.</param>
    /// <param name="schema">The FHIR version the folder is read as.</param>
    /// <param name="loggerFactory">Receives the search indexer's own log output.</param>
    /// <param name="cacheOptions">Where to persist indexes between processes; null (the default) keeps them in memory only.</param>
    /// <param name="timeProvider">
    /// Clock for the freshness count's short reuse window and for <see cref="UtcNow"/>, which time-dependent answers are
    /// anchored to; the system clock by default.
    /// </param>
    /// <param name="terminology">How to find terminology for the terminology tools; <see cref="TerminologyOptions.None"/> (only the folder's own) by default.</param>
    /// <param name="maxResponseBytes">
    /// The most one response may take however large a <c>maxBytes</c> a call asks for; <see cref="EnvelopeJson.MaxBytes"/> by
    /// default. The MCP server lowers it (<see cref="EnvelopeJson.McpMaxBytes"/>), because agent clients move larger results
    /// out of the model's context.
    /// </param>
    /// <param name="asOfOption">The session's <c>--as-of</c> as given (<see cref="AsOfClock"/>); null when there is none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResponseBytes"/> is below <see cref="EnvelopeJson.MinBudget"/> or above <see cref="EnvelopeJson.MaxBytes"/>.</exception>
    public FolderSession(string rootPath, IFhirSchemaProvider schema, ILoggerFactory loggerFactory, IndexCacheOptions? cacheOptions = null, TimeProvider? timeProvider = null, TerminologyOptions? terminology = null, int maxResponseBytes = EnvelopeJson.MaxBytes, string? asOfOption = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResponseBytes, EnvelopeJson.MinBudget);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResponseBytes, EnvelopeJson.MaxBytes);

        MaxResponseBytes = maxResponseBytes;

        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // Query parsing must use the same definitions the index was extracted with, so it shares the index builder's.
        _queryParser = new Lazy<SearchQueryParser>(
            () => new SearchQueryParser(schema, _indexBuilder.Value.Builder.Definitions, _indexBuilder.Value.Builder.ReferenceParser),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _entriesByReference = new Lazy<Dictionary<(string, string), CatalogEntry>>(
            () => Catalog.Entries.ToDictionary(entry => (entry.ResourceType, entry.Id)),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Only ids that are not FHIR ids are named by digest; the first of two that share a digest keeps it, in catalog order.
        _entriesByDigest = new Lazy<Dictionary<(string, string), CatalogEntry>>(
            () => Catalog.Entries
                .Where(entry => !FhirIds.IsValid(entry.Id))
                .GroupBy(entry => (entry.ResourceType, FhirIds.Digest(entry.Id)))
                .ToDictionary(group => group.Key, group => group.First()),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Read with the index's own reference parser, so a fullUrl parses the way a reference to it does. The conditional
        // resolver builds each type's identifier lookup on the first conditional reference to that type.
        _bundleReferences = new Lazy<BundleReferenceResolver>(
            () => new BundleReferenceResolver(Catalog.FullUrls, new ConditionalReferenceResolver(Catalog), _indexBuilder.Value.Builder.ReferenceParser),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _catalogTypes = new Lazy<HashSet<string>>(
            () => new HashSet<string>(Catalog.Entries.Select(entry => entry.ResourceType), StringComparer.Ordinal),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Compartment codes resolve against the same definitions the index was extracted with.
        _compartments = new Lazy<InMemoryCompartmentMembership>(
            () => new InMemoryCompartmentMembership(new CompartmentDefinitionManager(schema.Version), _indexBuilder.Value.Builder.Definitions),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Validating the cache needs the catalog and the definitions, so it also waits for the first index request.
        IndexCacheOptions options = cacheOptions ?? IndexCacheOptions.Disabled;
        _cache = new Lazy<FolderIndexCache>(
            () => new FolderIndexCache(options, Catalog, schema.Version.ToString(), _indexBuilder.Value.Builder.Definitions),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _patientCompartments = new Lazy<CompartmentIndex>(() => LoadOrBuildCompartments("Patient"), LazyThreadSafetyMode.ExecutionAndPublication);
        _kinds = new Lazy<FolderKinds>(() => FolderKinds.From(Catalog), LazyThreadSafetyMode.ExecutionAndPublication);

        Terminology = new TerminologySession(this, terminology ?? TerminologyOptions.None);
        Validators = new ValidatorCache(this);
        AsOfClock = new AsOfClock(this, asOfOption);
    }

    /// <summary>The session's asOf: its <c>--as-of</c>, else the folder's data-pull time, else the clock, worked out once on first use.</summary>
    public AsOfClock AsOfClock { get; }

    public string RootPath { get; }

    /// <summary>The most one response may take; a larger <c>maxBytes</c> is clamped to it.</summary>
    public int MaxResponseBytes { get; }

    public IFhirSchemaProvider Schema { get; }

    public FolderCatalog Catalog => _catalog.Value.Catalog;

    public BuildStats CatalogStats => _catalog.Value.Stats;

    /// <summary>The session's terminology store, loaded on the first terminology call.</summary>
    public TerminologySession Terminology { get; }

    /// <summary>The session's validators and the terminology adapter their binding checks use, built on first use.</summary>
    internal ValidatorCache Validators { get; }

    /// <summary>
    /// Wall time spent loading the search parameter definitions and creating the indexer, paid once on the first
    /// index request and not included in any type's <see cref="ResourceTypeIndex.BuildMilliseconds"/>; null until then.
    /// </summary>
    public long? IndexerLoadMilliseconds => _indexBuilder.IsValueCreated ? _indexBuilder.Value.LoadMilliseconds : null;

    /// <summary>How long a freshness count is reused, so tools answering back to back share one directory walk.</summary>
    public static readonly TimeSpan StaleFilesReuseWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Counts the folder's files that differ from what the catalog recorded when it was walked (modified, removed or
    /// newly added), by listing the folder and comparing each file's size and last-write time again. A fresh look at
    /// the disk, reused for <see cref="StaleFilesReuseWindow"/>, so it reports edits made while a long-running
    /// session is open. A folder that can no longer be listed yields an unavailable count rather than an exception.
    /// </summary>
    public StaleFilesCount CountStaleFiles(CancellationToken cancellationToken = default)
    {
        lock (_staleGate)
        {
            if (_lastStaleCount is not null && _timeProvider.GetElapsedTime(_lastStaleCountTimestamp) < StaleFilesReuseWindow)
            {
                return _lastStaleCount;
            }

            _lastStaleCount = RecountStaleFiles(cancellationToken);
            _lastStaleCountTimestamp = _timeProvider.GetTimestamp();
            return _lastStaleCount;
        }
    }

    private StaleFilesCount RecountStaleFiles(CancellationToken cancellationToken)
    {
        try
        {
            return new StaleFilesCount(FolderCatalogBuilder.CountStaleFiles(Catalog, cancellationToken), Unavailable: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StaleFilesCount(null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The search parameter definitions every index entry of this session refers to. Loading them is the
    /// one-time indexer cost, so reading this starts that load if no index has been requested yet.
    /// </summary>
    public ISearchParameterDefinitionManager Definitions => _indexBuilder.Value.Builder.Definitions;

    /// <summary>
    /// Compartment membership by the session's FHIR version's CompartmentDefinitions, over the session's search parameter
    /// definitions; created on first use (which loads the definitions, as <see cref="Definitions"/> does).
    /// </summary>
    public InMemoryCompartmentMembership Compartments => _compartments.Value;

    /// <summary>
    /// Every Patient's compartment, loaded from the persisted cache when it is valid, else worked out once from the indexes of
    /// every type that can be in one (building or loading them) and persisted. One patient's context then reads no index.
    /// </summary>
    public CompartmentIndex PatientCompartments => _patientCompartments.Value;

    /// <summary>The record kinds of each resource type (profiles, else Encounter class or Observation category), counted from the catalog on first use.</summary>
    public FolderKinds Kinds => _kinds.Value;

    /// <summary>Whether the catalog holds at least one resource of <paramref name="resourceType"/>.</summary>
    public bool HoldsType(string resourceType) => _catalogTypes.Value.Contains(resourceType);

    /// <summary>
    /// The current time by the session's clock. Every time-dependent answer (recent encounters, latest lab values) reads
    /// it once per call and passes that instant on, so a test clock, or a requested point in time, anchors the whole answer.
    /// </summary>
    public DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    /// <summary>
    /// Rewrites a resource's Bundle references to the <c>Type/id</c> the index resolved them to, so FHIRPath over the tool's own
    /// parse sees the same references searches, chains and compartments do; created on first use.
    /// </summary>
    internal BundleReferenceResolver BundleReferences => _bundleReferences.Value;

    /// <summary>The session's FHIR search query parser, created on first use.</summary>
    public SearchQueryParser QueryParser => _queryParser.Value;

    /// <summary>The catalogued resource <paramref name="resourceType"/>/<paramref name="id"/>, or null when the catalog holds none.</summary>
    public CatalogEntry? FindEntry(string resourceType, string id) =>
        _entriesByReference.Value.GetValueOrDefault((resourceType, id));

    /// <summary>
    /// The catalogued resource the tools name <paramref name="resourceType"/>/<paramref name="name"/>: by its id, or, for a
    /// digest (<see cref="FhirIds.Digest"/>), the resource whose id is not a FHIR id and hashes to it; null when none.
    /// </summary>
    public CatalogEntry? FindByName(string resourceType, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return FhirIds.IsDigest(name) ? _entriesByDigest.Value.GetValueOrDefault((resourceType, name)) : FindEntry(resourceType, name);
    }

    /// <summary>
    /// Directory holding this folder's persisted indexes; null when the cache is off or no index has been requested yet.
    /// </summary>
    public string? CacheDirectory => _cache.IsValueCreated ? _cache.Value.CacheDirectory : null;

    /// <summary>
    /// Problems met while loading or saving persisted indexes (a discarded or corrupt cache, an unwritable
    /// directory); each also means the affected index was built instead of loaded.
    /// </summary>
    public IReadOnlyList<IssueItem> CacheIssues => _cache.IsValueCreated ? _cache.Value.Issues : [];

    /// <summary>
    /// The cache problems a response carries: every <c>warning</c> (a corrupt or unavailable cache, which degrades every call
    /// while it lasts), and each <c>information</c> event (the cache was rebuilt) only on the first response after it
    /// occurred, after which it counts as delivered. Safe to call concurrently; each event is delivered once.
    /// </summary>
    public IReadOnlyList<IssueItem> TakeCacheIssues()
    {
        lock (_deliveredGate)
        {
            return [.. CacheIssues.Where(issue => issue.Severity != "information" || _deliveredCacheEvents.Add(issue))];
        }
    }

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

        ResourceTypeIndex built = builder.Build(Catalog, _bundleReferences.Value, resourceType, CancellationToken.None);
        return built with { CacheWriteMilliseconds = cache.Store(built) };
    }

    private CompartmentIndex LoadOrBuildCompartments(string compartmentType)
    {
        FolderIndexCache cache = _cache.Value;
        if (cache.TryLoadCompartments(compartmentType, FindEntry) is { } cached)
        {
            return cached;
        }

        HashSet<string> ids = [.. Catalog.Entries.Where(entry => entry.ResourceType == compartmentType).Select(entry => entry.Id)];
        if (ids.Count == 0)
        {
            return new CompartmentIndex(compartmentType, new Dictionary<string, IReadOnlyList<CatalogEntry>>(StringComparer.Ordinal), [], [], [], Catalog.BuiltAt, FromCache: false);
        }

        // The focal type's index is read first, as a single patient's context always read it, so diagnostics keep that order.
        var folder = new FolderSearchSource(this);
        _ = folder.Resources(compartmentType);
        (IReadOnlyDictionary<string, List<IndexedResource>> members, IReadOnlyList<string> unevaluable) = folder.CompartmentMembersOf(compartmentType, ids);
        var built = new CompartmentIndex(
            compartmentType,
            members.ToDictionary(pair => pair.Key, IReadOnlyList<CatalogEntry> (pair) => [.. pair.Value.Select(member => member.Entry)], StringComparer.Ordinal),
            [.. folder.IndexesRead.Select(index => index.ResourceType)],
            unevaluable,
            [.. folder.IndexesRead.SelectMany(index => index.Diagnostics)],
            folder.IndexesRead.Count > 0 ? folder.IndexesRead.Max(index => index.BuiltAt) : Catalog.BuiltAt,
            FromCache: false);
        cache.StoreCompartments(built);
        return built;
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
