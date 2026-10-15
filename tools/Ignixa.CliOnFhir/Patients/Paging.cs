// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The <c>count</c> and <c>offset</c> arguments of the patient tools, with the same limits as search.</summary>
internal static class Paging
{
    /// <summary>The page size to use: <paramref name="count"/>, the default when null, or the maximum with a <c>count-clamped</c> warning.</summary>
    /// <exception cref="ToolRequestException">A negative offset or a count below 1.</exception>
    public static int Resolve(int? count, int offset, List<IssueItem> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (offset < 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'offset' must not be negative, not {offset}.");
        }

        if (count is < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'count' must be at least 1, not {count}.");
        }

        if (count > ResourceSearcher.MaxCount)
        {
            issues.Add(new IssueItem("warning", "count-clamped", $"count {count} exceeds the maximum of {ResourceSearcher.MaxCount}, so {ResourceSearcher.MaxCount} were returned per page; use offset to read further.", Location: null));
            return ResourceSearcher.MaxCount;
        }

        return count ?? ResourceSearcher.DefaultCount;
    }

    /// <summary>An <c>offset-beyond-total</c> information issue when <paramref name="offset"/> skips everything; otherwise nothing.</summary>
    public static IEnumerable<IssueItem> BeyondTotal(int offset, int total, string what)
    {
        if (offset > 0 && offset >= total)
        {
            yield return new IssueItem("information", "offset-beyond-total", $"offset {offset} is at or beyond the {total} {what}, so none are returned.", Location: null);
        }
    }
}
