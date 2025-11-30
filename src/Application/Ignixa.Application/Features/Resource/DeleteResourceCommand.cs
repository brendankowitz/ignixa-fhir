// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Generic command to delete any FHIR resource.
/// Works for all resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
public record DeleteResourceCommand(string ResourceType, string Id) : IRequest<bool>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate delete capability for this resource type.
    /// Checks if CapabilityStatement advertises 'delete' interaction for the resource type.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'delete').exists()";
}
