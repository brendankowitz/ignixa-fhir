using Ignixa.Abstractions;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// One composite search parameter component whose definition URL resolves to nothing, recorded with
/// the reason it is left that way.
/// </summary>
/// <param name="Version">The FHIR version whose shipped definitions carry the dangling reference.</param>
/// <param name="CompositeUrl">The composite parameter that will not be indexed.</param>
/// <param name="ComponentIndex">Which of its components cannot be resolved.</param>
/// <param name="DefinitionUrl">The canonical URL the component points at.</param>
/// <param name="Reason">Why this is not repaired.</param>
internal sealed record CompositeComponentDivergence(
    FhirVersion Version,
    string CompositeUrl,
    int ComponentIndex,
    string DefinitionUrl,
    string Reason)
{
    public (FhirVersion, string, int) Key => (Version, CompositeUrl, ComponentIndex);

    public override string ToString() =>
        $"{Version} {CompositeUrl} component {ComponentIndex} -> {DefinitionUrl}";
}
