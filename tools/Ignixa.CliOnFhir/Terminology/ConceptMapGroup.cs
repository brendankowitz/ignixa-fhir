// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One ConceptMap <c>group</c>: mappings from one source system to one target system.</summary>
public sealed class ConceptMapGroup
{
    private readonly Dictionary<string, List<ConceptMapElement>> _elements;

    public ConceptMapGroup(string? source, string? sourceVersion, string? target, string? targetVersion, IReadOnlyList<ConceptMapElement> elements, ConceptMapUnmapped? unmapped)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Source = source;
        SourceVersion = sourceVersion;
        Target = target;
        TargetVersion = targetVersion;
        Elements = elements;
        Unmapped = unmapped;
        _elements = elements.GroupBy(element => element.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    public string? Source { get; }

    public string? SourceVersion { get; }

    public string? Target { get; }

    public string? TargetVersion { get; }

    public IReadOnlyList<ConceptMapElement> Elements { get; }

    public ConceptMapUnmapped? Unmapped { get; }

    /// <summary>The elements for <paramref name="code"/>, in source order; empty when the group does not map it.</summary>
    public IReadOnlyList<ConceptMapElement> ElementsFor(string code) => _elements.TryGetValue(code, out List<ConceptMapElement>? found) ? found : [];
}
