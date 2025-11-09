// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing FHIR search results for MCP tool responses.
/// Contains a collection of resource entries with pagination information.
/// </summary>
public class SearchResultsDto
{
    /// <summary>
    /// The FHIR resource type being searched (e.g., "Patient", "Observation").
    /// </summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>
    /// Collection of resource entries matching the search criteria.
    /// </summary>
    public IReadOnlyList<ResourceEntryDto> Entries { get; init; } = Array.Empty<ResourceEntryDto>();

    /// <summary>
    /// Total count of matching resources (if requested via _total parameter).
    /// Null if total count was not requested or cannot be determined.
    /// </summary>
    public int? Total { get; init; }

    /// <summary>
    /// Indicates if there are more results available beyond the current page.
    /// Used to determine if a "next" link should be generated.
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// Continuation token for fetching the next page of results.
    /// Null if this is the last page or pagination is not supported.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
