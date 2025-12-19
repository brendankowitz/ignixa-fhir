// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using Medino;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Experimental.Mcp.Dtos;
using Ignixa.Application.Features.Experimental.Mcp.Tools;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for retrieving a single FHIR resource by ID.
/// Supports _elements and _summary for response size optimization.
/// </summary>
[McpServerToolType]
public class GetResourceTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly IFhirRequestContextAccessor _contextAccessor;

    public GetResourceTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        IFhirRequestContextAccessor contextAccessor)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    [McpServerTool(Name = "get_fhir_resource")]
    [Description(@"Get a single FHIR resource by ID.
Use elements='id,field1,field2' to limit fields and reduce response size.
Use summary='true' for core fields only.
Returns null if resource not found.
Example: resourceType='Patient', id='123', elements='id,name,birthDate'")]
    public async Task<ResourceDto?> GetResourceAsync(
        [Description("Resource type: Patient, Observation, Condition, etc.")]
        string resourceType,

        [Description("Resource ID")]
        string id,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input
        var validationError = ValidateInput(resourceType, id);
        if (validationError != null)
        {
            throw new ArgumentException(validationError);
        }

        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Update the FHIR request context with the resolved tenant ID
        // The middleware has already created the context, we just need to update the tenant
        var requestContext = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available - middleware may not have run");

        requestContext.TenantId = resolvedTenantId;

        // Execute get via Medino handler
        var query = new GetResourceQuery(resourceType, id);
        var result = await _mediator.SendAsync(query, cancellationToken);

        if (result == null)
        {
            return null;
        }

        // Convert SearchEntryResult to ResourceDto
        // Note: _elements and _summary filtering would need to be implemented in GetResourceHandler
        // For Phase 1, we return the full resource (TODO: Phase 2 add filtering)
        var resourceJson = JsonDocument.Parse(result.ResourceBytes);

        return new ResourceDto
        {
            Resource = resourceJson
        };
    }

    /// <summary>
    /// Validates required input parameters.
    /// </summary>
    private static string? ValidateInput(string? resourceType, string? id)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return "resourceType is required (e.g., 'Patient', 'Observation')";
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return "id is required";
        }

        return null;
    }
}
