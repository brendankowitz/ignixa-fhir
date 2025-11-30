// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Result of a ConceptMap $translate operation.
/// Contains list of matching translations with equivalence information.
/// </summary>
/// <param name="Result">True if at least one match was found.</param>
/// <param name="Message">Human-readable message (e.g., "No matches found" or "Found 3 matches").</param>
/// <param name="Matches">List of translation matches.</param>
public record TranslateResult(
    bool Result,
    string? Message,
    IReadOnlyList<TranslateMatch> Matches);

/// <summary>
/// A single translation match from source code to target code.
/// </summary>
/// <param name="Equivalence">Equivalence relationship (equivalent, equal, wider, narrower, relatedto, inexact, unmatched, disjoint).</param>
/// <param name="Concept">The translated concept (system, code, display).</param>
/// <param name="Source">Canonical URL of the ConceptMap used for this match.</param>
/// <param name="Comment">Additional comments about the mapping (optional).</param>
public record TranslateMatch(
    string Equivalence,
    TranslateConcept Concept,
    string Source,
    string? Comment);

/// <summary>
/// A translated concept with system, code, and display.
/// </summary>
/// <param name="System">Target code system URL.</param>
/// <param name="Code">Target code.</param>
/// <param name="Display">Target display text (optional).</param>
public record TranslateConcept(
    string System,
    string Code,
    string? Display);
