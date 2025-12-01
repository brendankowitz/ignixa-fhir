// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic query to search for resources of any type.
/// Works for all FHIR resource types (Patient, Observation, Condition, etc.).
/// Supports system-wide search when ResourceType is null (searches across all resource types).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation"), or null for system-wide search.</param>
/// <param name="SearchOptions">The search options parsed from query parameters.</param>
public record SearchResourcesQuery(string? ResourceType, SearchOptions SearchOptions) : IRequest<SearchResourcesResult>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate search-type capability for this resource type.
    /// For system-wide searches (ResourceType is null), always returns true (no capability check needed).
    /// For specific resource types, checks if CapabilityStatement advertises 'search-type' interaction.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        ResourceType == null
            ? "true" // System-wide search is always allowed
            : $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'search-type').exists()";
}
