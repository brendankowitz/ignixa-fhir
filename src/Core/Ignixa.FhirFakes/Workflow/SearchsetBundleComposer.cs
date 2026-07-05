// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Default <see cref="ISearchResponseComposer"/>: splits a graph's <see cref="SearchResponseOptions.MatchResourceType"/>
/// entries into pages, attaches non-matching resources as includes per <see cref="IncludeCompleteness"/>,
/// and emits <c>self</c>/<c>next</c>/<c>previous</c> links using a <c>_page</c> query-string convention.
/// </summary>
public sealed class SearchsetBundleComposer : ISearchResponseComposer
{
    public IReadOnlyList<BundleJsonNode> Compose(ResourceGraph graph, SearchResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        if (options.PageSize <= 0)
        {
            throw new ArgumentException($"PageSize must be greater than zero, but was {options.PageSize}.", nameof(options));
        }

        var matches = graph.AllResources.Where(r => r.ResourceType == options.MatchResourceType).ToList();
        var includes = graph.AllResources.Where(r => r.ResourceType != options.MatchResourceType).ToList();

        var pages = matches.Chunk(options.PageSize).ToList();
        if (pages.Count == 0)
        {
            pages = [[]];
        }

        var bundles = new List<BundleJsonNode>(pages.Count);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            bundles.Add(ComposePage(pages[pageIndex], includes, matches.Count, pageIndex, pages.Count, options));
        }

        return bundles;
    }

    private static BundleJsonNode ComposePage(
        ResourceJsonNode[] pageMatches,
        IReadOnlyList<ResourceJsonNode> includes,
        int totalMatches,
        int pageIndex,
        int pageCount,
        SearchResponseOptions options)
    {
        var entries = new JsonArray();
        foreach (var match in pageMatches)
        {
            entries.Add(CreateEntry(match, searchMode: "match", options.BaseUrl));
        }

        if (options.IncludeCompleteness == IncludeCompleteness.Complete)
        {
            foreach (var include in includes)
            {
                entries.Add(CreateEntry(include, searchMode: "include", options.BaseUrl));
            }
        }

        var links = new JsonArray { CreateLink("self", PageUrl(options.BaseUrl, options.SearchUrl, pageIndex)) };
        if (pageIndex > 0)
        {
            links.Add(CreateLink("previous", PageUrl(options.BaseUrl, options.SearchUrl, pageIndex - 1)));
        }
        if (pageIndex < pageCount - 1)
        {
            links.Add(CreateLink("next", PageUrl(options.BaseUrl, options.SearchUrl, pageIndex + 1)));
        }

        var bundleNode = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = GetBundleTypeLiteral(options.BundleType),
            ["total"] = totalMatches,
            ["link"] = links,
            ["entry"] = entries,
        };

        return new BundleJsonNode(bundleNode);
    }

    private static JsonObject CreateEntry(ResourceJsonNode resource, string searchMode, string baseUrl) => new()
    {
        ["fullUrl"] = $"{baseUrl}/{resource.ResourceType}/{resource.Id}",
        ["resource"] = resource.MutableNode.DeepClone(),
        ["search"] = new JsonObject { ["mode"] = searchMode },
    };

    private static JsonObject CreateLink(string relation, string url) => new()
    {
        ["relation"] = relation,
        ["url"] = url,
    };

    private static string PageUrl(string baseUrl, string searchUrl, int pageIndex)
    {
        var fullSearchUrl = $"{baseUrl}{searchUrl}";
        if (pageIndex == 0)
        {
            return fullSearchUrl;
        }
        var separator = fullSearchUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{fullSearchUrl}{separator}_page={pageIndex}";
    }

    private static string GetBundleTypeLiteral(ResponseBundleType type) => type switch
    {
        ResponseBundleType.Searchset => "searchset",
        ResponseBundleType.BatchResponse => "batch-response",
        ResponseBundleType.TransactionResponse => "transaction-response",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
