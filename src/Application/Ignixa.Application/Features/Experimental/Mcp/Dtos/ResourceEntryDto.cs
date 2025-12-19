// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Application.Features.Experimental.Mcp.Dtos;

/// <summary>
/// DTO representing a single FHIR resource entry optimized for LLM consumption.
/// Removes redundant fields (id, resourceType, versionId, lastUpdated) that are already in the resource JSON.
/// Use _elements parameter to further reduce size by selecting specific fields.
/// </summary>
public class ResourceEntryDto
{
    /// <summary>
    /// The complete FHIR resource as a JSON document.
    /// Contains all fields including id, resourceType, meta.versionId, meta.lastUpdated.
    /// Use _elements query parameter to limit which fields are included.
    /// </summary>
    public JsonDocument Resource { get; init; } = JsonDocument.Parse("{}");

    /// <summary>
    /// Indicates how this entry relates to the search (match vs include).
    /// Values: "match" (matched search criteria), "include" (included via _include).
    /// Only relevant for search results, omit for single resource retrieval.
    /// </summary>
    public string? SearchMode { get; init; }
}
