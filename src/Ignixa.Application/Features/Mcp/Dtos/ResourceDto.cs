// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Application.Features.Mcp.Dtos;

/// <summary>
/// DTO representing a single FHIR resource for MCP tool responses.
/// Optimized for LLM consumption - removes redundant fields already in resource JSON.
/// Used for read operations that return a single resource.
/// </summary>
public class ResourceDto
{
    /// <summary>
    /// The complete FHIR resource as a JSON document.
    /// Contains all fields including id, resourceType, meta.versionId, meta.lastUpdated.
    /// Use _elements query parameter to limit which fields are included.
    /// </summary>
    public JsonDocument Resource { get; init; } = JsonDocument.Parse("{}");
}
