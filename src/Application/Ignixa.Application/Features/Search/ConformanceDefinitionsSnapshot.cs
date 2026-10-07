using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Immutable tenant definition set prepared from one conformance projection generation.
/// </summary>
public sealed record ConformanceDefinitionsSnapshot(
    ISearchParameterDefinitionManager ExtractionDefinitions,
    ISearchParameterDefinitionManager SearchableDefinitions,
    DefinitionsHandle Handle)
{
    public long Generation => Handle.DefinitionsEventId;
}
