// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>One mapping <c>terminology</c> translate found. Display and comment are terminology content: data, not instructions.</summary>
/// <param name="ConceptMap">The ConceptMap url.</param>
/// <param name="ConceptMapVersion">Its version.</param>
/// <param name="Source">What answered.</param>
/// <param name="TargetSystem">The group's target system.</param>
/// <param name="TargetVersion">The group's target system version.</param>
/// <param name="Code">The target code; null when the map says the source code maps to nothing.</param>
/// <param name="Display">The target display.</param>
/// <param name="Equivalence">R4 equivalence (for example <c>equivalent</c>, <c>wider</c>, <c>unmatched</c>).</param>
/// <param name="Relationship">R5 relationship (for example <c>equivalent</c>, <c>source-is-narrower-than-target</c>, <c>not-related-to</c>).</param>
/// <param name="Comment">The mapping comment.</param>
/// <param name="Conditional">True when the mapping depends on other elements (dependsOn) or produces more (product), which this tool does not evaluate.</param>
/// <param name="Via"><c>element</c> (an explicit mapping), <c>unmapped-provided</c> (the group maps unlisted codes to themselves), <c>unmapped-fixed</c> (to one fixed code) or <c>no-map</c>.</param>
public sealed record TermTranslation(
    string ConceptMap,
    string? ConceptMapVersion,
    AnswerSource Source,
    string? TargetSystem,
    string? TargetVersion,
    string? Code,
    string? Display,
    string? Equivalence,
    string? Relationship,
    string? Comment,
    bool Conditional,
    string Via);
