using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Immutable indexer/schema generation used for one complete extraction.
/// </summary>
public sealed record DefinitionsHandle(
    ISearchIndexer Indexer,
    IFhirSchemaProvider SchemaProvider,
    long DefinitionsEventId,
    long PublicationSequence = 0);
