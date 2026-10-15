// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>An <see cref="IInMemorySearchSource"/> over R4 resources given as JSON, indexed with <c>_lastUpdated</c>, in the order given.</summary>
internal sealed class IndexedTestSource : IInMemorySearchSource
{
    private static readonly ISearchIndexer WithLastUpdated =
        new LastUpdatedSearchIndexer(SearchCorpus.Indexer, SearchCorpus.Definitions, NullLogger.Instance);

    private readonly List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> _resources;

    public IndexedTestSource(params string[] resources)
    {
        _resources = [.. resources.Select(json => SearchCorpus.Index(WithLastUpdated, json))];
    }

    /// <summary>
    /// When true, every enumeration and resolution hands back a fresh copy of each index collection, as a source that
    /// deserializes its entries on demand would; the contract allows it.
    /// </summary>
    public bool FreshCollections { get; init; }

    /// <summary>How many times each type was enumerated, so tests can see what a search read.</summary>
    public Dictionary<string, int> Enumerations { get; } = new(StringComparer.Ordinal);

    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType)
    {
        Enumerations[resourceType] = Enumerations.GetValueOrDefault(resourceType) + 1;
        return _resources
            .Where(resource => resource.Location.ResourceType == resourceType)
            .Select(resource => FreshCollections ? (resource.Location, (IReadOnlyCollection<SearchIndexEntry>)[.. resource.Index]) : resource);
    }

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id) =>
        _resources.FirstOrDefault(resource => resource.Location.ResourceType == resourceType && resource.Location.Id == id) is { Location: not null } found
            ? found
            : null;

    /// <summary>Searches with every feature enabled and returns the matches' ids in order.</summary>
    public string[] Search(string resourceType, string query) =>
        [.. new InMemorySearchEngine(this, InMemorySearchFeatures.All).Search(SearchCorpus.Options(resourceType, query)).Select(match => match.Location.Id)];

    /// <summary>Searches with every feature enabled and returns the included resources as <c>Type/id</c>, in order.</summary>
    public string[] Included(string resourceType, string query)
    {
        var engine = new InMemorySearchEngine(this, InMemorySearchFeatures.All);
        Ignixa.Search.Models.SearchOptions options = SearchCorpus.Options(resourceType, query);
        return [.. engine.Include(engine.Search(options), options, int.MaxValue).Select(resource => resource.Location.ToString())];
    }
}
