// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Parses the catalogued resources of one type and extracts their search index with Ignixa's
/// <see cref="ElementSearchIndexer"/>. Only the files that contain resources of that type are read, and
/// resources are parsed from their recorded byte ranges.
/// </summary>
public sealed class FolderIndexBuilder
{
    private readonly IFhirSchemaProvider _schema;
    private readonly ISearchIndexer _indexer;

    public FolderIndexBuilder(IFhirSchemaProvider schema, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _schema = schema;
        var definitions = new SearchParameterDefinitionManager(schema, loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // A local folder has no service base URI, so absolute references are kept verbatim.
        // The indexer keeps only its logger, so the (non-owning) factory wrapper can be disposed right away.
        using ILoggerFactory capturingFactory = ExtractionFailureCapture.Wrap(loggerFactory, typeof(ElementSearchIndexer).FullName!);
        _indexer = SearchIndexerFactory.CreateInstance(schema, capturingFactory, definitions, NullFhirBaseUriProvider.Instance);
    }

    /// <summary>
    /// Indexes every catalogued resource of <paramref name="resourceType"/>. Extraction runs in parallel
    /// (the indexer is shared across concurrent requests in the server too); results are assembled in
    /// catalog order, so the index and its diagnostics are identical from run to run. The files holding
    /// the type's resources are held in memory for the duration of the build.
    /// </summary>
    public ResourceTypeIndex Build(FolderCatalog catalog, string resourceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var stopwatch = Stopwatch.StartNew();
        CatalogEntry[] entries = [.. catalog.Entries.Where(entry => string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal))];
        Dictionary<string, byte[]> files = entries
            .Select(entry => entry.FilePath)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(catalog.RootPath, path)), StringComparer.Ordinal);

        var results = new (IndexedResource? Resource, List<CatalogDiagnostic> Diagnostics)[entries.Length];
        Parallel.For(
            0,
            entries.Length,
            new ParallelOptions { CancellationToken = cancellationToken },
            i => results[i] = TryIndex(entries[i], files[entries[i].FilePath]));

        var resources = new List<IndexedResource>(entries.Length);
        var diagnostics = new List<CatalogDiagnostic>();
        foreach (var (resource, entryDiagnostics) in results)
        {
            diagnostics.AddRange(entryDiagnostics);
            if (resource is not null)
            {
                resources.Add(resource);
            }
        }

        return new ResourceTypeIndex(resourceType, resources, diagnostics, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds);
    }

    private (IndexedResource? Resource, List<CatalogDiagnostic> Diagnostics) TryIndex(CatalogEntry entry, byte[] fileBytes)
    {
        ResourceJsonNode node;
        try
        {
            node = JsonSourceNodeFactory.Parse(fileBytes.AsMemory((int)entry.Offset, entry.Length));
        }
        catch (JsonException ex)
        {
            return (null, [new CatalogDiagnostic("invalid-resource", entry.FilePath, entry.Line, $"{entry.ResourceType}/{entry.Id}: {ex.Message}")]);
        }

        IElement element = node.ToElement(_schema);
        var (index, diagnostics) = ExtractionFailureCapture.Capture(entry, () => _indexer.Extract(element));
        return (new IndexedResource(new ResourceKey(entry.ResourceType, entry.Id), entry, index), diagnostics);
    }
}
