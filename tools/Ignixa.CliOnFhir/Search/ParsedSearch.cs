// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Search;

/// <summary>A search request after parsing: what to evaluate, what to trim, and what the parser had to ignore.</summary>
/// <param name="Options">Ignixa's parse of the query; <see cref="SearchOptions.Expression"/> is null when nothing filters.</param>
/// <param name="IdFilters">
/// The <c>_id</c> parameters, each a set of accepted ids; all must hold. <c>_id</c> is intrinsic to the resource record, so
/// the index has no entry for it and the in-memory interpreter cannot evaluate it.
/// </param>
/// <param name="Elements">The elements to keep, or null to return whole resources.</param>
/// <param name="RequestedCount">The query's <c>_count</c>, or null when it had none.</param>
/// <param name="CountOnly">True when only the total was asked for.</param>
/// <param name="Warnings">Parameters or values that did not apply.</param>
public sealed record ParsedSearch(
    SearchOptions Options,
    IReadOnlyList<IReadOnlySet<string>> IdFilters,
    ElementsProjection? Elements,
    int? RequestedCount,
    bool CountOnly,
    IReadOnlyList<IssueItem> Warnings);
