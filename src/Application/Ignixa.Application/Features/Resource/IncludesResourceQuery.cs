// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Query to fetch additional included resources from a previous search.
/// Used by the $includes operation for independent pagination of _include/_revinclude results.
/// </summary>
/// <param name="ResourceType">The FHIR resource type from the original search (e.g., "Patient").</param>
/// <param name="SearchOptions">The search options with IncludesContinuationToken set.</param>
public record IncludesResourceQuery(string ResourceType, SearchOptions SearchOptions) : IRequest<SearchResourcesResult>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate search-type capability for this resource type.
    /// The $includes operation requires the same search capability as the original search.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'search-type').exists()";
}
