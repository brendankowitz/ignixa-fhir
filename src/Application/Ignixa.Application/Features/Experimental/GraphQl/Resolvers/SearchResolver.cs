// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Resolvers;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

public sealed class SearchResolver(
    IMediator mediator,
    ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
    IFhirRequestContextAccessor contextAccessor,
    IOptions<ExperimentalOptions> options,
    ILogger<SearchResolver> logger)
{
    public async Task<IReadOnlyList<JsonElement>> SearchListAsync(
        string resourceType,
        IResolverContext graphQlContext,
        CancellationToken cancellationToken)
    {
        var searchOptions = BuildSearchOptions(resourceType, graphQlContext);

        logger.LogDebug("GraphQL list search {ResourceType}", resourceType);

        var query = new SearchResourcesQuery(resourceType, searchOptions);
        var result = await mediator.SendAsync(query, cancellationToken);

        var entries = new List<JsonElement>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            if (!entry.IsDeleted)
                entries.Add(FieldResolver.ParseResourceBytes(entry.ResourceBytes));

            if (entries.Count >= searchOptions.MaxItemCount)
                break;
        }

        return entries;
    }

    public async Task<SearchConnectionResult> SearchAsync(
        string resourceType,
        IResolverContext graphQlContext,
        CancellationToken cancellationToken)
    {
        var searchOptions = BuildSearchOptions(resourceType, graphQlContext);

        logger.LogDebug("GraphQL searching {ResourceType}", resourceType);

        var query = new SearchResourcesQuery(resourceType, searchOptions);
        var result = await mediator.SendAsync(query, cancellationToken);

        var edges = new List<SearchEdge>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            if (!entry.IsDeleted)
            {
                edges.Add(new SearchEdge
                {
                    Resource = FieldResolver.ParseResourceBytes(entry.ResourceBytes),
                    Mode = "match",
                });
            }

            if (edges.Count >= searchOptions.MaxItemCount)
                break;
        }

        return new SearchConnectionResult
        {
            Count = result.Total,
            Offset = 0,
            Pagesize = searchOptions.MaxItemCount,
            Edges = edges,
            Next = result.ContinuationToken,
        };
    }

    public async Task<IReadOnlyList<JsonElement>> SearchReverseListAsync(
        string targetResourceType,
        string referenceParamName,
        string sourceResourceType,
        string sourceResourceId,
        IResolverContext graphQlContext,
        CancellationToken cancellationToken)
    {
        var additionalParams = new[] { new QueryParameter(referenceParamName, $"{sourceResourceType}/{sourceResourceId}") };
        var searchOptions = BuildSearchOptions(targetResourceType, graphQlContext, additionalParams);

        logger.LogDebug(
            "GraphQL reverse search {TargetType} via {Param}={SourceType}/{SourceId}",
            targetResourceType, referenceParamName, sourceResourceType, sourceResourceId);

        var query = new SearchResourcesQuery(targetResourceType, searchOptions);
        var result = await mediator.SendAsync(query, cancellationToken);

        var entries = new List<JsonElement>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            if (!entry.IsDeleted)
                entries.Add(FieldResolver.ParseResourceBytes(entry.ResourceBytes));

            if (entries.Count >= searchOptions.MaxItemCount)
                break;
        }

        return entries;
    }

    public async Task<SearchConnectionResult> SearchReverseAsync(
        string targetResourceType,
        string referenceParamName,
        string sourceResourceType,
        string sourceResourceId,
        IResolverContext graphQlContext,
        CancellationToken cancellationToken)
    {
        var additionalParams = new[] { new QueryParameter(referenceParamName, $"{sourceResourceType}/{sourceResourceId}") };
        var searchOptions = BuildSearchOptions(targetResourceType, graphQlContext, additionalParams);

        logger.LogDebug(
            "GraphQL reverse connection search {TargetType} via {Param}={SourceType}/{SourceId}",
            targetResourceType, referenceParamName, sourceResourceType, sourceResourceId);

        var query = new SearchResourcesQuery(targetResourceType, searchOptions);
        var result = await mediator.SendAsync(query, cancellationToken);

        var edges = new List<SearchEdge>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            if (!entry.IsDeleted)
            {
                edges.Add(new SearchEdge
                {
                    Resource = FieldResolver.ParseResourceBytes(entry.ResourceBytes),
                    Mode = "match",
                });
            }

            if (edges.Count >= searchOptions.MaxItemCount)
                break;
        }

        return new SearchConnectionResult
        {
            Count = result.Total,
            Offset = 0,
            Pagesize = searchOptions.MaxItemCount,
            Edges = edges,
            Next = result.ContinuationToken,
        };
    }

    private SearchOptions BuildSearchOptions(
        string resourceType,
        IResolverContext context,
        IReadOnlyList<QueryParameter>? additionalParameters = null)
    {
        var requestContext = contextAccessor.RequestContext;
        var fhirVersion = requestContext?.FhirVersion ?? FhirVersion.R4;
        var tenantId = requestContext?.TenantId;

        var parameters = new List<QueryParameter>();

        var graphQlOptions = options.Value.Features.GraphQl;
        var countOptional = context.ArgumentOptional<int?>("_count");
        var count = countOptional.HasValue ? countOptional.Value ?? graphQlOptions.DefaultPageSize : graphQlOptions.DefaultPageSize;
        count = Math.Clamp(count, 0, graphQlOptions.MaxPageSize);
        parameters.Add(new QueryParameter("_count", count.ToString()));

        var cursorOptional = context.ArgumentOptional<string?>("_cursor");
        var cursor = cursorOptional.HasValue ? cursorOptional.Value : null;
        if (!string.IsNullOrEmpty(cursor))
            parameters.Add(new QueryParameter("ct", cursor));

        var sortOptional = context.ArgumentOptional<IReadOnlyList<string>?>("_sort");
        if (sortOptional.HasValue && sortOptional.Value is { Count: > 0 })
            parameters.Add(new QueryParameter("_sort", string.Join(",", sortOptional.Value)));

        var totalOptional = context.ArgumentOptional<string?>("_total");
        var total = totalOptional.HasValue ? totalOptional.Value : null;
        if (!string.IsNullOrEmpty(total))
            parameters.Add(new QueryParameter("_total", total));

        var filterOptional = context.ArgumentOptional<string?>("_filter");
        if (filterOptional.HasValue && !string.IsNullOrEmpty(filterOptional.Value))
            parameters.Add(new QueryParameter("_filter", filterOptional.Value));

        // Forward all other arguments as FHIR search parameters
        foreach (var argument in context.Selection.Field.Arguments)
        {
            var argName = argument.Name;
            if (argName is "_count" or "_cursor" or "_sort" or "_total" or "_filter" or "_reference")
                continue;

            // FHIR system-level params start with '_' (e.g., _tag, _id, _lastUpdated).
            // Only replace internal underscores with hyphens for user-defined params
            // (e.g., general_practitioner → general-practitioner).
            var fhirParamName = argName.StartsWith('_') ? argName : argName.Replace('_', '-');
            var valueOptional = context.ArgumentOptional<string?>(argName);
            if (valueOptional.HasValue && !string.IsNullOrEmpty(valueOptional.Value))
                parameters.Add(new QueryParameter(fhirParamName, valueOptional.Value));
        }

        if (additionalParameters is not null)
            parameters.AddRange(additionalParameters);

        var builder = searchOptionsBuilderFactory.Create(fhirVersion, tenantId);
        return builder.Build(resourceType, parameters);
    }
}

