// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology</c> search.</summary>
/// <param name="Text">The text searched for, as given.</param>
/// <param name="System">The system searched within, when given.</param>
/// <param name="ValueSet">The ValueSet searched within, when given.</param>
/// <param name="Outcome"><c>searched</c>, or why nothing could be searched: <c>system-not-loaded</c>, <c>version-not-loaded</c>, <c>value-set-not-loaded</c>.</param>
/// <param name="CodeSystemsSearched">Code systems whose concepts were searched (or that the ValueSet's codes came from).</param>
/// <param name="ConceptsSearched">Concepts compared.</param>
/// <param name="ValueSetComplete">For a ValueSet, whether its expansion over loaded content is complete; null otherwise.</param>
/// <param name="Total">Matches in all.</param>
/// <param name="Offset">Matches skipped.</param>
/// <param name="Returned">Matches in this page.</param>
/// <param name="Matches">This page, ranked: exact before prefix before contains, then shorter display, then source precedence, system and code.</param>
public sealed record TermSearchFacts(
    string Text,
    string? System,
    string? ValueSet,
    string Outcome,
    int CodeSystemsSearched,
    int ConceptsSearched,
    bool? ValueSetComplete,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<TermMatch> Matches);
