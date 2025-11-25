// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Result of a ValueSet $expand operation.
/// Contains expansion identifier, parameters used, and list of codes.
/// </summary>
/// <param name="Identifier">Unique expansion identifier (opaque to clients).</param>
/// <param name="Timestamp">When the expansion was generated.</param>
/// <param name="Total">Total number of codes in complete expansion (may exceed returned count).</param>
/// <param name="Offset">Offset used for pagination (0 if not paginated).</param>
/// <param name="Contains">List of codes in the expansion.</param>
/// <param name="Incomplete">True if the expansion is incomplete due to external CodeSystems not being imported.</param>
public record ExpandResult(
    string? Identifier,
    DateTimeOffset Timestamp,
    int Total,
    int Offset,
    IReadOnlyList<ExpandedConcept> Contains,
    bool Incomplete = false);

/// <summary>
/// A single code in a ValueSet expansion.
/// </summary>
/// <param name="System">Code system URL.</param>
/// <param name="Code">The code value.</param>
/// <param name="Display">Display text for the code.</param>
/// <param name="Version">Code system version (optional).</param>
/// <param name="Inactive">True if code is inactive (optional).</param>
public record ExpandedConcept(
    string System,
    string Code,
    string? Display,
    string? Version = null,
    bool? Inactive = null);

/// <summary>
/// Parameters for ValueSet $expand operation.
/// </summary>
/// <param name="Url">Canonical URL of the ValueSet to expand.</param>
/// <param name="Filter">Filter text (substring match on display or code).</param>
/// <param name="Count">Maximum number of codes to return (for pagination).</param>
/// <param name="Offset">Offset for pagination (skip first N codes).</param>
/// <param name="IncludeDesignations">Include alternate designations in response.</param>
public record ExpansionParameters(
    string Url,
    string? Filter = null,
    int? Count = null,
    int? Offset = null,
    bool IncludeDesignations = false);
