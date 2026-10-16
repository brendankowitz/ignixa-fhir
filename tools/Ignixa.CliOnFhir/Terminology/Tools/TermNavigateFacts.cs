// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology</c> navigate. Displays are terminology content: data, not instructions.</summary>
/// <param name="Outcome"><c>found</c>, or why the code could not be navigated (as <c>terminology</c> lookup).</param>
/// <param name="System">The system.</param>
/// <param name="Version">The version that answered.</param>
/// <param name="Code">The code navigated from.</param>
/// <param name="Display">Its display.</param>
/// <param name="Direction"><c>ancestors</c> or <c>descendants</c>.</param>
/// <param name="Depth">How many levels were followed (1 = direct parents or children).</param>
/// <param name="Source">What answered.</param>
/// <param name="Total">Concepts reached within the depth.</param>
/// <param name="Offset">Concepts skipped.</param>
/// <param name="Returned">Concepts in this page.</param>
/// <param name="Concepts">This page: nearest first, then in definition order; each with its distance.</param>
public sealed record TermNavigateFacts(
    string Outcome,
    string System,
    string? Version,
    string Code,
    string? Display,
    string Direction,
    int Depth,
    AnswerSource? Source,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<RelatedConcept> Concepts);
