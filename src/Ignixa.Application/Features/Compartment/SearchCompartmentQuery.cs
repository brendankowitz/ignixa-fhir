// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Compartment;

/// <summary>
/// Query to search for resources within a FHIR compartment.
/// Example: GET /Patient/123/Observation - Find all Observations in Patient/123 compartment.
///
/// Implements compartment search as defined in:
/// https://www.hl7.org/fhir/compartmentdefinition.html
/// </summary>
/// <param name="CompartmentType">The compartment type (e.g., "Patient", "Practitioner").</param>
/// <param name="CompartmentId">The compartment resource ID (e.g., "123").</param>
/// <param name="ResourceType">The FHIR resource type to search (e.g., "Observation").</param>
/// <param name="SearchOptions">The search options parsed from query parameters.</param>
public record SearchCompartmentQuery(
    string CompartmentType,
    string CompartmentId,
    string ResourceType,
    SearchOptions SearchOptions) : IRequest<SearchResourcesResult>, IRequireCapability
{
    /// <summary>
    /// Returns FHIRPath expression to validate compartment search capability.
    /// Checks if CapabilityStatement advertises 'search-type' interaction for the resource type.
    /// </summary>
    public string GetCapabilityRequirementExpression() =>
        $"rest.resource.where(type = '{ResourceType}').interaction.where(code = 'search-type').exists()";
}
