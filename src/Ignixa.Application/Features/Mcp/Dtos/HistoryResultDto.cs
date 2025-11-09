// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing FHIR history results for MCP tool responses.
/// Contains a collection of resource versions with pagination information.
/// </summary>
public class HistoryResultDto
{
    /// <summary>
    /// The FHIR resource type being queried (e.g., "Patient", "Observation").
    /// Null for system-level history (_history endpoint).
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// The logical ID of the resource being queried (instance-level history).
    /// Null for type-level or system-level history.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Collection of resource versions in the history.
    /// Ordered by lastModified descending (most recent first).
    /// </summary>
    public IReadOnlyList<ResourceEntryDto> Entries { get; init; } = Array.Empty<ResourceEntryDto>();

    /// <summary>
    /// Total count of versions in the history (if requested via _total parameter).
    /// Null if total count was not requested or cannot be determined.
    /// </summary>
    public int? Total { get; init; }

    /// <summary>
    /// Indicates if there are more versions available beyond the current page.
    /// Used to determine if a "next" link should be generated.
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// Continuation token for fetching the next page of history.
    /// Null if this is the last page or pagination is not supported.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
