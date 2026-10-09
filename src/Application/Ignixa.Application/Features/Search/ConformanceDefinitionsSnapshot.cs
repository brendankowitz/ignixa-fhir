using Ignixa.Application.Features.Conformance;
using Ignixa.Search.Definition;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Immutable tenant definition set prepared from one conformance projection generation.
/// </summary>
/// <param name="Source">The detached projection the set was built from.</param>
public sealed record ConformanceDefinitionsSnapshot(
    ISearchParameterDefinitionManager ExtractionDefinitions,
    ISearchParameterDefinitionManager SearchableDefinitions,
    DefinitionsHandle Handle,
    ConformanceStateSnapshot Source,
    long PublicationSequence = 0)
{
    public long Generation => Handle.DefinitionsEventId;
}
