// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic query to retrieve a resource by type and ID.
/// Works for all FHIR resource types (Patient, Observation, Condition, etc.).
/// Returns SearchEntryResult for zero-copy serialization (read path with raw bytes).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
public record GetResourceQuery(string ResourceType, string Id) : IRequest<SearchEntryResult?>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate read capability for this resource type.
    /// Checks if CapabilityStatement advertises 'read' interaction for the resource type.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'read').exists()";
}
