using Ignixa.Search.Indexing;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Immutable search definitions snapshot used for one complete extraction.
/// </summary>
public sealed record DefinitionsHandle(
    ISearchIndexer Indexer,
    long DefinitionsEventId);
