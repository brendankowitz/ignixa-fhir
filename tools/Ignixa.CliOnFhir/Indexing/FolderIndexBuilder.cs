// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
/// Parses every catalogued resource once and extracts its search index with Ignixa's
/// <see cref="ElementSearchIndexer"/>. Each file is read once; resources are parsed from their
/// recorded byte ranges.
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
        _indexer = SearchIndexerFactory.CreateInstance(schema, loggerFactory, definitions, NullFhirBaseUriProvider.Instance);
    }

    /// <summary>
    /// Indexes every catalogued resource. Extraction runs in parallel (the indexer is shared across
    /// concurrent requests in the server too); results are assembled in catalog order, so the index
    /// and its diagnostics are identical from run to run. All catalogued files are held in memory
    /// for the duration of the build.
    /// </summary>
    public FolderIndex Build(FolderCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        IReadOnlyList<CatalogEntry> entries = catalog.Entries;
        Dictionary<string, byte[]> files = entries
            .Select(entry => entry.FilePath)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(catalog.RootPath, path)), StringComparer.Ordinal);

        var results = new (IndexedResource? Resource, CatalogDiagnostic? Diagnostic)[entries.Count];
        Parallel.For(
            0,
            entries.Count,
            new ParallelOptions { CancellationToken = cancellationToken },
            i => results[i] = TryIndex(entries[i], files[entries[i].FilePath]));

        var byType = new Dictionary<string, List<IndexedResource>>(StringComparer.Ordinal);
        var diagnostics = new List<CatalogDiagnostic>();
        foreach (var (resource, diagnostic) in results)
        {
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
                continue;
            }

            if (!byType.TryGetValue(resource!.Key.ResourceType, out List<IndexedResource>? list))
            {
                list = [];
                byType.Add(resource.Key.ResourceType, list);
            }

            list.Add(resource);
        }

        return new FolderIndex(
            catalog,
            byType.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<IndexedResource>)pair.Value, StringComparer.Ordinal),
            diagnostics,
            DateTimeOffset.UtcNow);
    }

    private (IndexedResource? Resource, CatalogDiagnostic? Diagnostic) TryIndex(CatalogEntry entry, byte[] fileBytes)
    {
        if (!_schema.ResourceTypeNames.Contains(entry.ResourceType))
        {
            return (null, new CatalogDiagnostic("unknown-resource-type", entry.FilePath, entry.Line, $"'{entry.ResourceType}' is not a {_schema.Version} resource type."));
        }

        ResourceJsonNode node;
        try
        {
            node = JsonSourceNodeFactory.Parse(fileBytes.AsMemory((int)entry.Offset, entry.Length));
        }
        catch (JsonException ex)
        {
            return (null, new CatalogDiagnostic("invalid-resource", entry.FilePath, entry.Line, $"{entry.ResourceType}/{entry.Id}: {ex.Message}"));
        }

        IElement element = node.ToElement(_schema);
        IReadOnlyCollection<SearchIndexEntry> index = _indexer.Extract(element);
        return (new IndexedResource(new ResourceKey(entry.ResourceType, entry.Id), entry, index), null);
    }
}
