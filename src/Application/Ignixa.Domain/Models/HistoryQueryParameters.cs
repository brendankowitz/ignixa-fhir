// <copyright file="HistoryQueryParameters.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Search.Models;

namespace Ignixa.Domain.Models;

/// <summary>
/// Query parameters for FHIR _history operations.
/// </summary>
public sealed record HistoryQueryParameters
{
    /// <summary>
    /// Maximum number of results per page.
    /// Default: 20, Maximum: 1000 per FHIR recommendations.
    /// </summary>
    public int Count { get; init; } = 20;

    /// <summary>
    /// Number of results to skip (for offset-based pagination).
    /// Default: 0.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Filter: Include only versions created at or after this instant.
    /// FHIR parameter: _since.
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Filter: Include only versions created at or before this instant.
    /// Custom parameter (extension): _until.
    /// </summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// Sort order for history results.
    /// Default: Descending (newest first) per FHIR specification.
    /// </summary>
    public HistorySortOrder Sort { get; init; } = HistorySortOrder.Descending;

    /// <summary>
    /// Controls whether to calculate and include total count in Bundle response.
    /// Default: None (most performant, no total calculation).
    /// Use Accurate sparingly as it requires a separate count query.
    /// FHIR parameter: _total.
    /// </summary>
    public TotalMode Total { get; init; } = TotalMode.None;

    /// <summary>
    /// Controls the level of detail returned in the history response.
    /// Default: False (full resources).
    /// When set to Count, only Bundle.total is returned (no entries).
    /// FHIR parameter: _summary.
    /// </summary>
    public SummaryType Summary { get; init; } = SummaryType.False;

    /// <summary>
    /// Maximum allowed page size (enforced by server).
    /// </summary>
    public const int MaxCount = 1000;

    /// <summary>
    /// Default page size.
    /// </summary>
    public const int DefaultCount = 20;

    /// <summary>
    /// Ensures Count is within valid range [1, MaxCount].
    /// </summary>
    public HistoryQueryParameters WithValidatedCount()
    {
        if (Count < 1 || Count > MaxCount)
        {
            return this with { Count = Count < 1 ? DefaultCount : MaxCount };
        }

        return this;
    }

    /// <summary>
    /// Ensures Offset is non-negative.
    /// </summary>
    public HistoryQueryParameters WithValidatedOffset()
    {
        if (Offset < 0)
        {
            return this with { Offset = 0 };
        }

        return this;
    }

    /// <summary>
    /// Validates all parameters and returns a sanitized copy.
    /// </summary>
    public HistoryQueryParameters Validate()
    {
        return WithValidatedCount().WithValidatedOffset();
    }
}
