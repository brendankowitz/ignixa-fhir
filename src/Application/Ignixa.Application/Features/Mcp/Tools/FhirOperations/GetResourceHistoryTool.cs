// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using Medino;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Features.History;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for retrieving version history for a FHIR resource.
/// Returns max 10 versions by default to optimize LLM token usage.
/// </summary>
[McpServerToolType]
public class GetResourceHistoryTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

    public GetResourceHistoryTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _fhirRequestContextAccessor = fhirRequestContextAccessor;
    }

    [McpServerTool(Name = "get_fhir_resource_history")]
    [Description(@"Get version history for a FHIR resource. Returns max 10 versions by default.
Use count parameter to request more (max 50 for LLM optimization).
Results are sorted by newest first (descending).
Example: resourceType='Patient', id='123', count=5")]
    public async Task<HistoryResultDto> GetResourceHistoryAsync(
        [Description("Resource type: Patient, Observation, Condition, etc.")]
        string resourceType,

        [Description("Resource ID")]
        string id,

        [Description("Max versions to return (default: 10, max: 50)")]
        int? count = null,

        [Description("Number of versions to skip (for pagination)")]
        int? offset = null,

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

        // Enforce default count=10, max=50 (LLM optimization per design guidelines)
        var effectiveCount = Math.Min(count ?? 10, 50);

        // Build history query parameters
        var parameters = new HistoryQueryParameters
        {
            Count = effectiveCount,
            Offset = offset ?? 0,
            Sort = HistorySortOrder.Descending, // Newest first
            Total = TotalMode.None // Don't calculate totals by default (expensive)
        };

        // Execute history query via Medino handler
        // Note: GetResourceHistoryHandler requires BaseUrl and RequestPath for pagination links
        // For MCP, we can use placeholder values since we return data, not FHIR Bundle links
        var requestContext = _fhirRequestContextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available - middleware may not have run");

        var baseUrl = "https://localhost"; // Placeholder for MCP tools
        var requestPath = $"/{resourceType}/{id}/_history";

        var query = new GetResourceHistoryQuery(
            ResourceType: resourceType,
            ResourceId: id,
            TenantId: resolvedTenantId,
            Parameters: parameters,
            BaseUrl: baseUrl,
            RequestPath: requestPath);

        var result = await _mediator.SendAsync(query, cancellationToken);

        // Materialize streaming results (MCP tools need full response, not IAsyncEnumerable)
        var entries = new List<ResourceEntryDto>();
        await foreach (var entry in result.Entries.WithCancellation(cancellationToken))
        {
            var resourceJson = JsonDocument.Parse(entry.ResourceBytes);
            entries.Add(new ResourceEntryDto
            {
                Resource = resourceJson,
                SearchMode = null // History entries don't have search mode
            });

            // Respect count limit
            if (entries.Count >= effectiveCount)
            {
                break;
            }
        }

        return new HistoryResultDto
        {
            ResourceType = resourceType,
            ResourceId = id,
            Entries = entries,
            Total = result.TotalCount,
            HasMore = entries.Count >= effectiveCount
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
