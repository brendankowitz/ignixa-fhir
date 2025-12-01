// <copyright file="HistoryQueryParametersParser.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Parses FHIR history query parameters from HTTP query string.
/// Supports: _count, _offset, _since, _until, _sort, _total, _summary.
/// </summary>
public static class HistoryQueryParametersParser
{
    /// <summary>
    /// Parses history query parameters from HttpRequest query string.
    /// </summary>
    /// <param name="queryString">HTTP query string collection.</param>
    /// <returns>Validated HistoryQueryParameters.</returns>
    public static HistoryQueryParameters Parse(IQueryCollection queryString)
    {
        ArgumentNullException.ThrowIfNull(queryString);

        var count = ParseCount(queryString);
        var summary = ParseSummary(queryString);
        var total = ParseTotal(queryString);

        // Auto-set Total=Accurate when _summary=count
        if (summary == SummaryType.Count && total == TotalMode.None)
        {
            total = TotalMode.Accurate;
        }

        // Auto-set Total=Accurate when _count=0 (common pattern for count-only queries)
        if (count == 0 && total == TotalMode.None)
        {
            total = TotalMode.Accurate;
        }

        var parameters = new HistoryQueryParameters
        {
            Count = count,
            Offset = ParseOffset(queryString),
            Since = ParseDateTimeOffset(queryString, "_since"),
            Until = ParseDateTimeOffset(queryString, "_until"),
            Sort = ParseSort(queryString),
            Total = total,
            Summary = summary,
        };

        return parameters.Validate();
    }

    private static int ParseCount(IQueryCollection queryString)
    {
        if (queryString.TryGetValue("_count", out var countValue) &&
            int.TryParse(countValue.ToString(), out var count))
        {
            return count;
        }

        return HistoryQueryParameters.DefaultCount;
    }

    private static int ParseOffset(IQueryCollection queryString)
    {
        if (queryString.TryGetValue("_offset", out var offsetValue) &&
            int.TryParse(offsetValue.ToString(), out var offset))
        {
            return offset;
        }

        return 0;
    }

    private static DateTimeOffset? ParseDateTimeOffset(IQueryCollection queryString, string parameterName)
    {
        if (queryString.TryGetValue(parameterName, out var value) &&
            !StringValues.IsNullOrEmpty(value))
        {
            var valueString = value.ToString();

            // Try parsing ISO 8601 format (FHIR standard)
            if (DateTimeOffset.TryParse(valueString, out var result))
            {
                return result;
            }
        }

        return null;
    }

    private static HistorySortOrder ParseSort(IQueryCollection queryString)
    {
        if (queryString.TryGetValue("_sort", out var sortValue))
        {
            var sortString = sortValue.ToString().ToUpperInvariant();

            return sortString switch
            {
                "ASC" => HistorySortOrder.Ascending,
                "ASCENDING" => HistorySortOrder.Ascending,
                "DESC" => HistorySortOrder.Descending,
                "DESCENDING" => HistorySortOrder.Descending,
                _ => HistorySortOrder.Descending, // Default per FHIR spec
            };
        }

        // Default: descending (newest first) per FHIR specification
        return HistorySortOrder.Descending;
    }

    private static TotalMode ParseTotal(IQueryCollection queryString)
    {
        if (queryString.TryGetValue("_total", out var totalValue))
        {
            var totalString = totalValue.ToString().ToUpperInvariant();

            return totalString switch
            {
                "NONE" => TotalMode.None,
                "ESTIMATE" => TotalMode.Estimate,
                "ACCURATE" => TotalMode.Accurate,
                _ => TotalMode.None, // Default if invalid value
            };
        }

        // Default: None (most performant, no total calculation)
        return TotalMode.None;
    }

    private static SummaryType ParseSummary(IQueryCollection queryString)
    {
        if (queryString.TryGetValue("_summary", out var summaryValue))
        {
            var summaryString = summaryValue.ToString().ToUpperInvariant();

            return summaryString switch
            {
                "COUNT" => SummaryType.Count,
                "TRUE" => SummaryType.True,
                "FALSE" => SummaryType.False,
                "DATA" => SummaryType.Data,
                "TEXT" => SummaryType.Text,
                _ => SummaryType.False, // Default if invalid value
            };
        }

        // Default: False (return full resources)
        return SummaryType.False;
    }
}
