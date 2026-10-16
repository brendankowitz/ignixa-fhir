// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Views;

/// <summary>The rows of a run returned in the response: from <see cref="Offset"/>, at most <see cref="Count"/>.</summary>
/// <param name="Count">Rows to return; 0 returns none (the totals and the columns only).</param>
/// <param name="Offset">Rows to skip.</param>
internal readonly record struct ViewPaging(int Count, int Offset)
{
    /// <summary>Rows returned when the call does not say.</summary>
    public const int DefaultCount = 100;

    /// <summary>The most rows one response returns; <c>out</c> writes all of them.</summary>
    public const int MaxCount = 1000;

    /// <summary>
    /// The paging a request asks for. A count above <see cref="MaxCount"/> is clamped with a <c>count-clamped</c> warning, as
    /// search does.
    /// </summary>
    /// <exception cref="ToolRequestException">A negative count or offset.</exception>
    public static ViewPaging Resolve(ViewRequest request, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(issues);

        if (request.Count is < 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'count' must be 0 or more, not {request.Count}.");
        }

        if (request.Offset < 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'offset' must be 0 or more, not {request.Offset}.");
        }

        int count = request.Count ?? DefaultCount;
        if (count > MaxCount)
        {
            issues.Add(new IssueItem("warning", "count-clamped", $"count {count} exceeds the maximum of {MaxCount}, so {MaxCount} rows were returned per page; use offset to read further, or out to write every row to a file.", Location: null));
            count = MaxCount;
        }

        return new ViewPaging(count, request.Offset);
    }
}
