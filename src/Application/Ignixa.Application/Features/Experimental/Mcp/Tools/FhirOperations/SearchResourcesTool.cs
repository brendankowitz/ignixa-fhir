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
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for searching FHIR resources with LLM-optimized response sizes.
/// Defaults to 10 results with support for _elements and _summary parameters.
/// </summary>
[McpServerToolType]
public class SearchResourcesTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;
    private readonly ISearchOptionsBuilderFactory _builderFactory;
    private readonly IFhirVersionContext _versionContext;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IFhirRequestContextAccessor _contextAccessor;

    public SearchResourcesTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator,
        ISearchOptionsBuilderFactory builderFactory,
        IFhirVersionContext versionContext,
        IFhirRequestContextAccessor contextAccessor)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
        _tenantConfigurationStore = tenantStore ?? throw new ArgumentNullException(nameof(tenantStore));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
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

        // Build QueryParameter list from searchParams dictionary
        // Include search parameters plus control parameters (_count, _elements, _summary, _total)
        var queryParameters = new List<QueryParameter>();

        // Add search parameters from the input dictionary
        foreach (var kvp in searchParams)
        {
            queryParameters.Add(new QueryParameter(kvp.Key, kvp.Value));
        }

        // Add control parameters
        queryParameters.Add(new QueryParameter("_count", effectiveCount.ToString()));

        if (!string.IsNullOrEmpty(elements))
        {
            queryParameters.Add(new QueryParameter("_elements", elements));
        }

        if (!string.IsNullOrEmpty(summary))
        {
            queryParameters.Add(new QueryParameter("_summary", summary));
        }

        if (!string.IsNullOrEmpty(total))
        {
            queryParameters.Add(new QueryParameter("_total", total));
        }

        // Get FHIR version for the tenant to resolve correct schema provider
        var fhirVersion = await ResolveFhirVersionAsync(resolvedTenantId, cancellationToken);

        // Get the appropriate schema provider for this FHIR version
        var schemaProvider = _versionContext.GetBaseSchemaProvider(fhirVersion);

        // Use SearchOptionsBuilder to parse all parameters and build expressions
        var builder = _builderFactory.Create(fhirVersion);
        var searchOptions = builder.Build(resourceType, queryParameters, schemaProvider);

        // Update the FHIR request context with the resolved tenant ID
        // The middleware has already created the context, we just need to update the tenant
        var requestContext = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available - middleware may not have run");

        requestContext.TenantId = resolvedTenantId;

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

    /// <summary>
    /// Resolve the FHIR version from tenant configuration, with fallback to R4 default.
    /// </summary>
    private async Task<FhirVersion> ResolveFhirVersionAsync(int tenantId, CancellationToken cancellationToken)
    {
        // Default to R4
        var fhirVersion = FhirVersion.R4;

        try
        {
            var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
            if (tenantConfig != null && !string.IsNullOrEmpty(tenantConfig.FhirVersion))
            {
                fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
            }
        }
        catch
        {
            // If tenant resolution fails, use default R4
        }

        return fhirVersion;
    }
}
