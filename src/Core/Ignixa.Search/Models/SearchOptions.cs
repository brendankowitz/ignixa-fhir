// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Serialization.Models;

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
    /// Gets or sets the _elements parameter (comma-separated list of element names to include).
    /// </summary>
    public IReadOnlySet<string> Elements { get; set; } = new HashSet<string>();

    /// <summary>
    /// Gets or sets whether to include the total count of matching resources.
    /// </summary>
    public TotalType Total { get; set; } = TotalType.None;

    /// <summary>
    /// Gets or sets the summary mode.
    /// </summary>
    public SummaryType Summary { get; set; } = SummaryType.None;

    /// <summary>
    /// Gets or sets any unsupported search parameters encountered.
    /// </summary>
    public IReadOnlyList<string> UnsupportedParams { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets issues related to the search (e.g., unsupported parameters).
    /// These will be rendered as Bundle.issue entries in the search response.
    /// Each issue is an OperationOutcome.issue structure (severity, code, diagnostics, etc.).
    /// </summary>
    public IReadOnlyList<IssueComponent> BundleIssues { get; set; } = Array.Empty<IssueComponent>();

    /// <summary>
    /// Gets or sets the resource type being searched.
    /// </summary>
    public string ResourceType { get; set; }

    /// <summary>
    /// Gets or sets the resource types to filter by (from _type parameter).
    /// Used in system-level search to filter results to specific resource types.
    /// </summary>
    public IReadOnlyList<string> ResourceTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional: When set, filters results to resources within this surrogate ID range.
    /// Used for parallel export operations to partition work across multiple workers.
    /// When both are set, filters to resources where: StartSurrogateId <= SurrogateId <= EndSurrogateId
    /// </summary>
    public long? StartSurrogateId { get; set; }

    /// <summary>
    /// Optional: The end of the surrogate ID range (inclusive).
    /// Must be set together with StartSurrogateId to take effect.
    /// </summary>
    public long? EndSurrogateId { get; set; }

    /// <summary>
    /// Maximum number of included resources to return (_includesCount parameter).
    /// When set, limits the number of _include/_revinclude results per page.
    /// If null, includes are not limited separately from primary results.
    /// </summary>
    public int? IncludesMaxItemCount { get; set; }

    /// <summary>
    /// Continuation token for pagination of included resources (_includesContinuationToken).
    /// Used by the $includes operation to fetch additional included resources.
    /// </summary>
    public string IncludesContinuationToken { get; set; }
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
    /// No _summary parameter was specified (return full resources).
    /// This is distinct from False, which means _summary=false was explicitly specified.
    /// </summary>
    None,

    /// <summary>
    /// Return full resources (_summary=false explicitly specified).
    /// </summary>
    False,

    /// <summary>
    /// Return only the count of matching resources.
    /// </summary>
    Count,

    /// <summary>
    /// Return only the id, versionId, and lastUpdated.
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

/// <summary>
/// Represents an issue in a Bundle response, aligned with OperationOutcome.issue structure.
/// https://www.hl7.org/fhir/operationoutcome.html
/// </summary>
/// <param name="Severity">Indicates whether the issue is fatal, error, warning, or information. (Required)</param>
/// <param name="Code">Describes the type of the issue. (Required)</param>
/// <param name="Details">A CodeableConcept with structured details about the issue type. (Optional)</param>
/// <param name="Diagnostics">Additional diagnostic information about the issue. (Optional)</param>
/// <param name="Location">The location of the issue in the request (FHIRPath expression). (Optional)</param>
/// <param name="Expression">The FHIRPath expression corresponding to the error. (Optional)</param>
public record IssueComponent(
    string Severity,
    string Code,
    CodeableConceptJsonNode Details = null,
    string Diagnostics = null,
    IReadOnlyList<string> Location = null,
    IReadOnlyList<string> Expression = null);
