// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Models;

/// <summary>
/// Represents the parsed search query configuration.
/// </summary>
public class SearchOptions
{
    /// <summary>
    /// Gets or sets the maximum number of items to return per page.
    /// </summary>
    public int MaxItemCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the continuation token for paging.
    /// </summary>
    public string ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the search expression tree (combined search parameters).
    /// </summary>
    public Expression Expression { get; set; }

    /// <summary>
    /// Gets or sets the sort expressions.
    /// </summary>
    public IReadOnlyList<SortExpression> Sort { get; set; } = Array.Empty<SortExpression>();

    /// <summary>
    /// Gets or sets the _include expressions.
    /// </summary>
    public IReadOnlyList<IncludeExpression> Include { get; set; } = Array.Empty<IncludeExpression>();

    /// <summary>
    /// Gets or sets the _revinclude expressions.
    /// </summary>
    public IReadOnlyList<IncludeExpression> RevInclude { get; set; } = Array.Empty<IncludeExpression>();

    /// <summary>
    /// Gets or sets whether to include the total count of matching resources.
    /// </summary>
    public TotalType Total { get; set; } = TotalType.None;

    /// <summary>
    /// Gets or sets the summary mode.
    /// </summary>
    public SummaryType Summary { get; set; } = SummaryType.False;

    /// <summary>
    /// Gets or sets any unsupported search parameters encountered.
    /// </summary>
    public IReadOnlyList<string> UnsupportedParams { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the resource type being searched.
    /// </summary>
    public string ResourceType { get; set; }
}

/// <summary>
/// Specifies how the server should return the total count of matching resources.
/// </summary>
public enum TotalType
{
    /// <summary>
    /// Do not include total count.
    /// </summary>
    None,

    /// <summary>
    /// Include accurate total count.
    /// </summary>
    Accurate,

    /// <summary>
    /// Include estimated total count.
    /// </summary>
    Estimate,
}

/// <summary>
/// Specifies how the server should return the search results.
/// </summary>
public enum SummaryType
{
    /// <summary>
    /// Return full resources.
    /// </summary>
    False,

    /// <summary>
    /// Return only the count of matching resources.
    /// </summary>
    Count,

    /// <summary>
    /// Return only the id, versionId, and lastModified.
    /// </summary>
    True,

    /// <summary>
    /// Return only the text narrative.
    /// </summary>
    Text,

    /// <summary>
    /// Return only the data elements.
    /// </summary>
    Data,
}
