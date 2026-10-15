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
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
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
        Definitions = new SearchParameterDefinitionManager(schema, loggerFactory.CreateLogger<SearchParameterDefinitionManager>());

        // A local folder has no service base URI, so absolute references are kept verbatim. The same parser instance
        // serves the query side (see SearchQueryParser), so index and queries agree on reference forms.
        ReferenceParser = new ReferenceSearchValueParser(schema, NullFhirBaseUriProvider.Instance);

        // Ignixa's own indexer plus the in-memory-only entries: _lastUpdated, and the canonicals of reference parameters
        // (R5 _profile), which no stored backend indexes. The indexer keeps only its loggers, so the (non-owning) factory
        // wrapper can be disposed right away; both report through the ElementSearchIndexer category the capture listens on.
        using ILoggerFactory capturingFactory = ExtractionFailureCapture.Wrap(loggerFactory, typeof(ElementSearchIndexer).FullName!);
        _indexer = new LastUpdatedSearchIndexer(
            new CanonicalReferenceSearchIndexer(
                SearchIndexerFactory.CreateInstance(schema, capturingFactory, Definitions, NullFhirBaseUriProvider.Instance),
                Definitions),
            Definitions,
            capturingFactory.CreateLogger<ElementSearchIndexer>());
    }

    /// <summary>The search parameter definitions the indexer extracts with; the same instances its entries refer to.</summary>
    public SearchParameterDefinitionManager Definitions { get; }

    /// <summary>Parses reference values for both indexing and queries.</summary>
    public ReferenceSearchValueParser ReferenceParser { get; }

    /// <summary>
    /// Indexes every catalogued resource of <paramref name="resourceType"/>. Extraction runs in parallel
    /// (the indexer is shared across concurrent requests in the server too); results are assembled in
    /// catalog order, so the index and its diagnostics are identical from run to run. The files holding
    /// the type's resources are held in memory for the duration of the build.
    /// </summary>
    /// <param name="catalog">The folder's catalog.</param>
    /// <param name="bundleReferences">
    /// Rewrites references to Bundle entries by fullUrl before each resource is indexed; the type's <c>urn:</c> references it
    /// cannot resolve are reported in one <see cref="BundleReferenceResolver.UnresolvedCode"/> diagnostic, after the others.
    /// </param>
    /// <param name="resourceType">The type to index.</param>
    /// <param name="cancellationToken">Cancels extraction.</param>
    internal ResourceTypeIndex Build(FolderCatalog catalog, BundleReferenceResolver bundleReferences, string resourceType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bundleReferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var stopwatch = Stopwatch.StartNew();
        CatalogEntry[] entries = [.. catalog.Entries.Where(entry => string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal))];
        Dictionary<string, byte[]> files = entries
            .Select(entry => entry.FilePath)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(catalog.RootPath, path)), StringComparer.Ordinal);

        var results = new (IndexedResource? Resource, List<CatalogDiagnostic> Diagnostics, IReadOnlyList<string> CrossScope)[entries.Length];
        Parallel.For(
            0,
            entries.Length,
            new ParallelOptions { CancellationToken = cancellationToken },
            i => results[i] = TryIndex(entries[i], files[entries[i].FilePath], bundleReferences));

        var resources = new List<IndexedResource>(entries.Length);
        var diagnostics = new List<CatalogDiagnostic>();
        var unresolved = new List<(IndexedResource Resource, string Reference)>();
        var crossScope = new List<(IndexedResource Resource, string Reference)>();
        foreach (var (resource, entryDiagnostics, crossScopeReferences) in results)
        {
            diagnostics.AddRange(entryDiagnostics);
            if (resource is not null)
            {
                resources.Add(resource);
                unresolved.AddRange(resource.BundleReferences.UnresolvedUrns.Select(urn => (resource, urn)));
                crossScope.AddRange(crossScopeReferences.Select(reference => (resource, reference)));
            }
        }

        if (unresolved.Count > 0)
        {
            diagnostics.Add(BundleDiagnostic(
                BundleReferenceResolver.UnresolvedCode,
                unresolved,
                $"{unresolved.Count} urn: references in {resourceType} resources name no Bundle entry fullUrl in the folder, or name more than one resource, so they are indexed as written and reference searches, chains, includes and compartments do not follow them",
                item => $"{Describe(item.Resource)} -> {item.Reference}"));
        }

        if (crossScope.Count > 0)
        {
            diagnostics.Add(BundleDiagnostic(
                BundleReferenceResolver.CrossScopeCode,
                crossScope,
                $"{crossScope.Count} references in {resourceType} resources were resolved through a fullUrl outside their own Bundle (FHIR resolves Bundle references only within the Bundle; this relies on the urn:uuid or URL being unique across the folder)",
                item => $"{Describe(item.Resource)} -> {item.Reference} = {item.Resource.BundleReferences.Resolved[item.Reference]}"));
        }

        return new ResourceTypeIndex(resourceType, resources, diagnostics, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds, FromCache: false);
    }

    private (IndexedResource? Resource, List<CatalogDiagnostic> Diagnostics, IReadOnlyList<string> CrossScope) TryIndex(CatalogEntry entry, byte[] fileBytes, BundleReferenceResolver bundleReferences)
    {
        if (entry.Offset + entry.Length > fileBytes.Length)
        {
            return (null, [Stale(entry, $"the file is now {fileBytes.Length} bytes, shorter than the catalogued range ending at {entry.Offset + entry.Length}")], []);
        }

        ReadOnlyMemory<byte> json = fileBytes.AsMemory((int)entry.Offset, entry.Length);
        ResourceJsonNode node;
        try
        {
            // The file may have changed since it was catalogued; confirm the range still holds the catalogued resource.
            var (foundType, foundId) = ResourceSniffer.Sniff(json.Span);
            if (!string.Equals(foundType, entry.ResourceType, StringComparison.Ordinal)
                || (!entry.HasSyntheticId && !string.Equals(foundId, entry.Id, StringComparison.Ordinal)))
            {
                string expected = entry.HasSyntheticId ? entry.ResourceType : $"{entry.ResourceType}/{entry.Id}";
                string found = entry.HasSyntheticId ? foundType ?? "(no resourceType)" : $"{foundType ?? "(no resourceType)"}/{foundId ?? "(no id)"}";
                return (null, [Stale(entry, $"expected {expected} but found {found}")], []);
            }

            node = JsonSourceNodeFactory.Parse(json);
        }
        catch (JsonException ex)
        {
            return (null, [new CatalogDiagnostic("invalid-resource", entry.FilePath, entry.Line, $"{entry.ResourceType}/{entry.Id}: {ex.Message}")], []);
        }

        // Rewritten in the tool's own parse, before conversion, so every parameter sees the resolved reference.
        var (resolution, crossScope) = bundleReferences.Rewrite(entry, ((IMutableJsonNode)node).MutableNode);
        IElement element = node.ToElement(_schema);
        var (index, diagnostics) = ExtractionFailureCapture.Capture(entry, () => _indexer.Extract(element), IndexedCanonicals);
        return (new IndexedResource(new ResourceKey(entry.ResourceType, entry.Id), entry, index, resolution), diagnostics, crossScope);
    }

    /// <summary>One diagnostic for a type's Bundle references of one kind: how many (distinct per resource), located at the first, naming the first few.</summary>
    private static CatalogDiagnostic BundleDiagnostic(
        string code,
        List<(IndexedResource Resource, string Reference)> items,
        string summary,
        Func<(IndexedResource Resource, string Reference), string> sample)
    {
        CatalogEntry first = items[0].Resource.Entry;
        string more = items.Count > BundleReferenceResolver.SampleLimit ? $" (first {BundleReferenceResolver.SampleLimit})" : string.Empty;
        return new CatalogDiagnostic(code, first.FilePath, first.Line, $"{summary}{more}: {string.Join(", ", items.Take(BundleReferenceResolver.SampleLimit).Select(sample))}.");
    }

    private static string Describe(IndexedResource resource) => $"{resource.Key.ResourceType}/{resource.Key.Id}";

    /// <summary>
    /// The reference parameters whose canonicals <see cref="CanonicalReferenceSearchIndexer"/> indexed on this resource, so
    /// the stock indexer's "element 'canonical' is not supported" warning for them is not reported; a canonical it did not
    /// cover keeps its warning.
    /// </summary>
    private static HashSet<string> IndexedCanonicals(IReadOnlyCollection<SearchIndexEntry> index) =>
        [.. index
            .Where(indexed => indexed.SearchParameter.Type == SearchParamType.Reference && indexed.Value is UriSearchValue)
            .Select(indexed => indexed.SearchParameter.Url.ToString())];

    private static CatalogDiagnostic Stale(CatalogEntry entry, string detail) => new(
        "catalog-stale",
        entry.FilePath,
        entry.Line,
        $"{entry.ResourceType}/{entry.Id} is no longer where the catalog recorded it ({detail}); the file changed after it was catalogued, so the resource was not indexed.");
}
