// <copyright file="HistoryPaginationLinkBuilder.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Builds pagination links for FHIR history bundles.
/// Constructs self, first, next, previous, last links with query parameters.
/// </summary>
public static class HistoryPaginationLinkBuilder
{
    /// <summary>
    /// Builds all pagination links for a history bundle.
    /// </summary>
    /// <param name="baseUrl">Base URL (e.g., "https://api.example.com").</param>
    /// <param name="requestPath">Request path (e.g., "/Patient/123/_history").</param>
    /// <param name="parameters">Current query parameters.</param>
    /// <param name="totalCount">Total number of results across all pages (null if unknown).</param>
    /// <returns>List of BundleLinkJsonNode objects.</returns>
    public static IReadOnlyList<BundleLinkJsonNode> BuildLinks(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters,
        int? totalCount = null)
    {
        var links = new List<BundleLinkJsonNode>();

        // Self link (current page)
        links.Add(BuildSelfLink(baseUrl, requestPath, parameters));

        // First link (offset = 0)
        links.Add(BuildFirstLink(baseUrl, requestPath, parameters));

        // Previous link (if not on first page)
        if (parameters.Offset > 0)
        {
            links.Add(BuildPreviousLink(baseUrl, requestPath, parameters));
        }

        // Next link
        if (totalCount.HasValue)
        {
            // If total count is known, only include next if more pages exist
            var hasMorePages = parameters.Offset + parameters.Count < totalCount.Value;
            if (hasMorePages)
            {
                links.Add(BuildNextLink(baseUrl, requestPath, parameters));
            }
        }
        else
        {
            // If total count unknown, include next link (user can try it)
            // Server will return empty results if no more pages
            links.Add(BuildNextLink(baseUrl, requestPath, parameters));
        }

        // Last link (only if total count known and more than one page)
        if (totalCount.HasValue && totalCount.Value > parameters.Count)
        {
            links.Add(BuildLastLink(baseUrl, requestPath, parameters, totalCount.Value));
        }

        return links;
    }

    private static BundleLinkJsonNode BuildSelfLink(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters)
    {
        var url = BuildUrl(baseUrl, requestPath, parameters);
        return new BundleLinkJsonNode() { Relation = "self", Url = url };
    }

    private static BundleLinkJsonNode BuildFirstLink(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters)
    {
        var firstPageParams = parameters with { Offset = 0 };
        var url = BuildUrl(baseUrl, requestPath, firstPageParams);
        return new BundleLinkJsonNode() { Relation = "first", Url = url };
    }

    private static BundleLinkJsonNode BuildPreviousLink(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters)
    {
        // Calculate previous page offset
        var previousOffset = Math.Max(0, parameters.Offset - parameters.Count);
        var previousPageParams = parameters with { Offset = previousOffset };
        var url = BuildUrl(baseUrl, requestPath, previousPageParams);
        return new BundleLinkJsonNode() { Relation = "previous", Url = url };
    }

    private static BundleLinkJsonNode BuildNextLink(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters)
    {
        var nextPageParams = parameters with { Offset = parameters.Offset + parameters.Count };
        var url = BuildUrl(baseUrl, requestPath, nextPageParams);
        return new BundleLinkJsonNode() { Relation = "next", Url = url };
    }

    private static BundleLinkJsonNode BuildLastLink(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters,
        int totalCount)
    {
        // Calculate last page offset
        var lastPageOffset = Math.Max(0, ((totalCount - 1) / parameters.Count) * parameters.Count);
        var lastPageParams = parameters with { Offset = lastPageOffset };
        var url = BuildUrl(baseUrl, requestPath, lastPageParams);
        return new BundleLinkJsonNode() { Relation = "last", Url = url };
    }

    private static string BuildUrl(
        string baseUrl,
        string requestPath,
        HistoryQueryParameters parameters)
    {
        var sb = new StringBuilder();
        sb.Append(baseUrl.TrimEnd('/'));
        sb.Append(requestPath);

        // Add query parameters
        var queryParams = new List<string>();

        // Always include _count
        queryParams.Add($"_count={parameters.Count}");

        // Include _offset if not zero
        if (parameters.Offset > 0)
        {
            queryParams.Add($"_offset={parameters.Offset}");
        }

        // Include _since if specified
        if (parameters.Since.HasValue)
        {
            queryParams.Add($"_since={Uri.EscapeDataString(parameters.Since.Value.ToString("o"))}");
        }

        // Include _until if specified
        if (parameters.Until.HasValue)
        {
            queryParams.Add($"_until={Uri.EscapeDataString(parameters.Until.Value.ToString("o"))}");
        }

        // Include _sort if not default (descending)
        if (parameters.Sort == HistorySortOrder.Ascending)
        {
            queryParams.Add("_sort=asc");
        }

        // Append query string
        if (queryParams.Count > 0)
        {
            sb.Append('?');
            sb.Append(string.Join('&', queryParams));
        }

        return sb.ToString();
    }
}
