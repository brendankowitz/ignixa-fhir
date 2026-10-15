// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A concept that may match, with its folded texts and where it came from.</summary>
internal sealed record SearchCandidate(
    string System,
    string? Version,
    string Code,
    string? Display,
    bool? Inactive,
    string? FoldedDisplay,
    IReadOnlyList<string> Designations,
    IReadOnlyList<string> FoldedDesignations,
    ResourceOrigin? Origin,
    int SourceRank,
    string Source)
{
    public static SearchCandidate From(TerminologyStore store, CodeSystemEntry entry, FoldedConcept concept) => new(
        entry.Url,
        entry.Version,
        concept.Concept.Code,
        concept.Concept.Display,
        concept.Concept.Inactive,
        concept.Display,
        [.. concept.Concept.Designations.Select(designation => designation.Value)],
        concept.Designations,
        entry.Origin,
        store.PrecedenceOf(entry.Origin.Source, entry.Url),
        entry.Origin.Source.Name);

    // A ValueSet's code: matched on its own display, plus the code system's designations when that system is loaded.
    public static SearchCandidate From(TerminologyStore store, ExpansionCode code)
    {
        CodeSystemEntry? entry = store.ResolveCodeSystem(code.System, code.Version).Entry;
        Concept? concept = entry?.Find(code.Code);
        IReadOnlyList<string> designations = concept is null ? [] : [.. concept.Designations.Select(designation => designation.Value)];
        return new SearchCandidate(
            code.System,
            code.Version,
            code.Code,
            code.Display,
            code.Inactive,
            code.Display is null ? null : TextFolding.Fold(code.Display),
            designations,
            [.. designations.Select(TextFolding.Fold)],
            entry?.Origin,
            entry is null ? int.MaxValue : store.PrecedenceOf(entry.Origin.Source, entry.Url),
            entry?.Origin.Source.Name ?? "value-set");
    }
}
