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
                    Resource = JsonSerializer.Deserialize<JsonElement>(entry.ResourceBytes.Span),
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

    private SearchOptions BuildSearchOptions(string resourceType, IResolverContext context)
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

        var sortOptional = context.ArgumentOptional<string?>("_sort");
        var sort = sortOptional.HasValue ? sortOptional.Value : null;
        if (!string.IsNullOrEmpty(sort))
            parameters.Add(new QueryParameter("_sort", sort));

        var totalOptional = context.ArgumentOptional<string?>("_total");
        var total = totalOptional.HasValue ? totalOptional.Value : null;
        if (!string.IsNullOrEmpty(total))
            parameters.Add(new QueryParameter("_total", total));

        var builder = searchOptionsBuilderFactory.Create(fhirVersion, tenantId);
        return builder.Build(resourceType, parameters);
    }
}
