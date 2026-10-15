// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology_translate</c>.</summary>
/// <param name="Outcome">
/// <c>mapped</c> (at least one target), <c>not-mapped</c> (maps from the system exist, but none maps the code, or they map
/// it to nothing), <c>no-map-for-system</c> (no loaded ConceptMap has a group from the system, to the target system when
/// given) or <c>concept-map-not-loaded</c> (the ConceptMap asked for is not loaded).
/// </param>
/// <param name="System">The source system.</param>
/// <param name="Code">The source code.</param>
/// <param name="TargetSystem">The target system asked for, when given.</param>
/// <param name="ConceptMap">The ConceptMap asked for, when given.</param>
/// <param name="ConceptMapsChecked">ConceptMaps with a group from the system that were read.</param>
/// <param name="Translations">The mappings, in map precedence, group and target order.</param>
public sealed record TermTranslateFacts(
    string Outcome,
    string System,
    string Code,
    string? TargetSystem,
    string? ConceptMap,
    int ConceptMapsChecked,
    IReadOnlyList<TermTranslation> Translations);
