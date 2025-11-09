// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using Medino;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Dtos;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for searching FHIR resources with LLM-optimized response sizes.
/// Defaults to 10 results with support for _elements and _summary parameters.
/// </summary>
[McpServerToolType]
public class SearchResourcesTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public SearchResourcesTool(
        IHttpContextAccessor httpContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(httpContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "search_fhir_resources")]
    [Description(@"Search FHIR resources. Returns max 10 results by default (specify count up to 50 for more).
Use elements='id,field1,field2' to limit fields and reduce response size (highly recommended).
Use summary='true' for core fields only, summary='data' to exclude narrative text, or summary='count' for count-only.
Use total='accurate' to return the total matching resource count.
Example: resourceType='Patient', searchParams={'name': 'Smith'}, elements='id,name,birthDate', summary='count'")]
    public async Task<SearchResultsDto> SearchResourcesAsync(
        [Description("Resource type: Patient, Observation, Condition, etc.")]
        string resourceType,

        [Description("Search parameters as key-value pairs. Example: {'name': 'Smith', 'birthdate': 'gt2000'}")]
        Dictionary<string, string> searchParams,

        [Description("Max results (default: 10, max: 50). Lower values reduce response size.")]
        int? count = null,

        [Description("Comma-separated fields to include (e.g., 'id,name,birthDate'). Dramatically reduces response size.")]
        string? elements = null,

        [Description("Summary mode: 'true' (core fields only), 'data' (no text), 'text' (id+meta+text only), 'count' (count-only), 'false' (full resource)")]
        string? summary = null,

        [Description("Total count calculation: 'accurate' (calculate total matching), 'estimate' (estimate), or 'none' (skip expensive count)")]
        string? total = null,

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Resolve tenant using base class logic
        var resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);

        // Enforce default count=10, max=50 (LLM optimization per design guidelines)
        var effectiveCount = Math.Min(count ?? 10, 50);

        // Parse _total parameter (default: None for performance)
        var totalType = ParseTotalType(total);

        // Build SearchOptions with LLM-optimized parameters
        var searchOptions = new SearchOptions
        {
            ResourceType = resourceType,
            MaxItemCount = effectiveCount,
            Total = totalType, // Can be None (default), Accurate, or Estimate
            Summary = ParseSummaryType(summary),
            Elements = ParseElements(elements),
            Expression = null // Let SearchOptionsBuilder handle expression parsing
        };

        // Parse search parameters into FHIR query format
        // For MCP Phase 1, we'll build a simple query - future: use SearchOptionsBuilder for full parsing
        var queryParams = new Dictionary<string, string>(searchParams);
        queryParams["_count"] = effectiveCount.ToString();

        if (!string.IsNullOrEmpty(elements))
        {
            queryParams["_elements"] = elements;
        }

        if (!string.IsNullOrEmpty(summary))
        {
            queryParams["_summary"] = summary;
        }

        if (!string.IsNullOrEmpty(total))
        {
            queryParams["_total"] = total;
        }

        // Set tenant context in HttpContext.Items for SearchResourcesHandler
        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext not available");

        httpContext.Items["TenantId"] = resolvedTenantId;

        // Execute search via Medino handler
        var query = new SearchResourcesQuery(resourceType, searchOptions);
        var result = await _mediator.SendAsync(query, cancellationToken);

        // Materialize streaming results (MCP tools need full response, not IAsyncEnumerable)
        var entries = new List<ResourceEntryDto>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            // Convert SearchEntryResult to ResourceEntryDto (optimized DTO with just Resource + SearchMode)
            // ResourceBytes contains UTF-8 JSON bytes
            var resourceJson = JsonDocument.Parse(entry.ResourceBytes);
            entries.Add(new ResourceEntryDto
            {
                Resource = resourceJson,
                SearchMode = entry.SearchMode.ToString().ToUpperInvariant()
            });

            // Respect MaxItemCount limit (SearchResourcesHandler returns pageSize + 1 for pagination detection)
            if (entries.Count >= effectiveCount)
            {
                break;
            }
        }

        return new SearchResultsDto
        {
            ResourceType = resourceType,
            Entries = entries,
            Total = result.Total,
            HasMore = entries.Count >= effectiveCount, // If we got full page, there might be more
            ContinuationToken = result.ContinuationToken
        };
    }

    private static SummaryType ParseSummaryType(string? summary)
    {
        return summary?.ToUpperInvariant() switch
        {
            "TRUE" => SummaryType.True,
            "DATA" => SummaryType.Data,
            "TEXT" => SummaryType.Text,
            "COUNT" => SummaryType.Count,
            _ => SummaryType.False
        };
    }

    private static TotalType ParseTotalType(string? total)
    {
        return total?.ToUpperInvariant() switch
        {
            "ACCURATE" => TotalType.Accurate,
            "ESTIMATE" => TotalType.Estimate,
            _ => TotalType.None // Default: don't calculate total (performance)
        };
    }

    private static IReadOnlySet<string> ParseElements(string? elements)
    {
        if (string.IsNullOrWhiteSpace(elements))
        {
            return new HashSet<string>();
        }

        return elements.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
