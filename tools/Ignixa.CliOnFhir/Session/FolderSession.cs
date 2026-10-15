// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
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
    private readonly Lazy<FolderIndexBuilder> _indexBuilder;
    private readonly ConcurrentDictionary<string, Lazy<ResourceTypeIndex>> _indexes = new(StringComparer.Ordinal);

    public FolderSession(string rootPath, IFhirSchemaProvider schema, ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        RootPath = Path.GetFullPath(rootPath);
        Schema = schema;
        _catalog = new Lazy<(FolderCatalog, BuildStats)>(() => BuildCatalog(RootPath, schema), LazyThreadSafetyMode.ExecutionAndPublication);

        // Loading the search parameter definitions is the dominant fixed cost of indexing, so it waits for the first index request.
        _indexBuilder = new Lazy<FolderIndexBuilder>(() => new FolderIndexBuilder(schema, loggerFactory), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string RootPath { get; }

    public IFhirSchemaProvider Schema { get; }

    public FolderCatalog Catalog => _catalog.Value.Catalog;

    public BuildStats CatalogStats => _catalog.Value.Stats;

    /// <summary>Resource types whose index has been built so far, in ordinal order.</summary>
    public IReadOnlyList<string> BuiltIndexTypes => [.. _indexes
        .Where(pair => pair.Value.IsValueCreated)
        .Select(pair => pair.Key)
        .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Returns the index for <paramref name="resourceType"/>, building it on the first request. A type
    /// with no catalogued resources yields an empty index.
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
            .GetOrAdd(resourceType, type => new Lazy<ResourceTypeIndex>(
                () => _indexBuilder.Value.Build(Catalog, type, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
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
