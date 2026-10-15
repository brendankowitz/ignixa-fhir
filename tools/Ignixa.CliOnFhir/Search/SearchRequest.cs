// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Search;

/// <summary>One search over the folder.</summary>
/// <param name="ResourceType">The resource type to search.</param>
/// <param name="Query">FHIR search query string such as <c>code=http://loinc.org|8867-4&amp;_count=5</c>; null or empty matches everything.</param>
/// <param name="Count">Page size; overrides <c>_count</c> in the query. Null means the query's <c>_count</c> or the default.</param>
/// <param name="Offset">Number of matches to skip, in catalog order.</param>
/// <param name="Summary">Null for resources, or <c>count</c> for the total only; adds <c>_summary</c> to the query.</param>
/// <param name="Elements">Comma-separated top-level elements to keep; adds to <c>_elements</c> in the query.</param>
/// <param name="Lenient">
/// When true, a parameter name the search pipeline does not recognise is ignored with a <c>parameter-ignored</c> warning
/// (which can widen the results) instead of failing the request.
/// </param>
/// <param name="Compartment">
/// A compartment such as <c>Patient/123</c> to search within, as <c>Patient/123/Observation?query</c> does; null searches
/// the whole folder.
/// </param>
public sealed record SearchRequest(
    string ResourceType,
    string? Query = null,
    int? Count = null,
    int Offset = 0,
    string? Summary = null,
    string? Elements = null,
    bool Lenient = false,
    string? Compartment = null);
