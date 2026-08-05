// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Models;

/// <summary>
/// Represents the parsed search query configuration.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOptions"/> class with default values.
    /// </summary>
    public SearchOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOptions"/> class as a shallow copy of
    /// <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The instance to copy every property from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This type is a mutable configuration object that callers routinely need to vary by one or two
    /// properties — a search that re-requests one extra row to detect a further page, or a <c>$includes</c>
    /// pass that widens the page and drops the caller's page boundary. Without a copy constructor the only
    /// options are to mutate the caller's instance, which leaks across every other holder of that reference,
    /// or to hand-copy the properties at the call site, which silently drops any property added later. Both
    /// in-repo call sites previously hand-copied, and both had fallen nine properties behind.
    /// </para>
    /// <para>
    /// The copy is shallow: collection properties are shared with <paramref name="other"/>. Every one is
    /// typed as a read-only interface and this class never mutates one in place — varying a collection means
    /// assigning a new one. A caller that retains a concrete reference to a collection it assigned can still
    /// mutate it and be seen by every copy; don't.
    /// </para>
    /// </remarks>
    public SearchOptions(SearchOptions other)
    {
        ArgumentNullException.ThrowIfNull(other);

        MaxItemCount = other.MaxItemCount;
        ContinuationToken = other.ContinuationToken;
        Expression = other.Expression;
        Sort = other.Sort;
        Include = other.Include;
        RevInclude = other.RevInclude;
        Elements = other.Elements;
        Total = other.Total;
        Summary = other.Summary;
        UnsupportedParams = other.UnsupportedParams;
        UnsupportedModifierParams = other.UnsupportedModifierParams;
        BundleIssues = other.BundleIssues;
        ResourceType = other.ResourceType;
        ResourceTypes = other.ResourceTypes;
        StartSurrogateId = other.StartSurrogateId;
        EndSurrogateId = other.EndSurrogateId;
        IncludesMaxItemCount = other.IncludesMaxItemCount;
        IncludesContinuationToken = other.IncludesContinuationToken;
        ResourceVersionTypes = other.ResourceVersionTypes;
        AccessConstraints = other.AccessConstraints;
        AllowedResourceTypes = other.AllowedResourceTypes;
    }

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
    /// Gets or sets the search parameters that carried a modifier this server does not support for them,
    /// for example <c>_id:above</c>. Always a subset of <see cref="UnsupportedParams"/>.
    /// </summary>
    /// <remarks>
    /// Tracked apart from <see cref="UnsupportedParams"/> because FHIR R4 gives the two cases different
    /// force: an unknown or unsupported <i>parameter</i> SHOULD be ignored, whereas a request suffixed by
    /// an unsupported <i>modifier</i> SHALL be rejected with a 400. Both are dropped from
    /// <see cref="Expression"/> either way, so a caller that chooses to honour <c>handling=lenient</c> can
    /// keep serving the query; the list exists so the HTTP boundary can apply the stricter default without
    /// this layer having to know about headers.
    /// </remarks>
    public IReadOnlyList<string> UnsupportedModifierParams { get; set; } = Array.Empty<string>();

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

    /// <summary>
    /// Gets or sets which resource versions the search may return. Defaults to <see cref="ResourceVersionTypes.Latest"/>,
    /// the only shape an ordinary search wants; _history, $export, and reindex widen it.
    /// </summary>
    /// <remarks>
    /// Like <see cref="AccessConstraints"/>, this property is forwarded into the SQL compiler:
    /// <c>SearchSqlCompiler.TryCreatePlanFromOptionsAsync</c> maps it onto the tri-state
    /// <c>Ignixa.Search.Sql.Ast.ResourceVisibility</c>, resolving each version column independently. A set
    /// that names <see cref="Latest"/> but not a column's non-current partner pins that column to its
    /// current value (<c>IsHistory = 0</c> / <c>IsDeleted = 0</c>); one that names the non-current partner
    /// (<see cref="History"/> or <see cref="SoftDeleted"/>) but not <see cref="Latest"/> pins it to the
    /// non-current value (<c>= 1</c>); one that names both, or neither, leaves the column unfiltered. So
    /// <c>History</c> alone returns superseded versions <em>exclusively</em> (<c>IsHistory = 1</c>), while
    /// <c>Latest | History</c> returns the union (no <c>IsHistory</c> filter) — the distinction the earlier
    /// relaxation-only mapping could not draw, which is why a history-only or soft-deleted-only search had
    /// to be refused upstream rather than compiled. <see cref="None"/> is not a valid search input and the
    /// compiler throws <see cref="NotSupportedException"/> rather than treating it as <see cref="Latest"/>.
    /// </remarks>
    public ResourceVersionTypes ResourceVersionTypes { get; set; } = ResourceVersionTypes.Latest;

    /// <summary>
    /// Restrictions on which resources the caller may see, at most one per resource type. Empty means
    /// unrestricted. Enforced structurally by the compiler, not by rewriting the search expression.
    /// </summary>
    public IReadOnlyList<AccessConstraint> AccessConstraints { get; set; } = Array.Empty<AccessConstraint>();

    /// <summary>
    /// The global allow-list of resource types the caller is permitted to see. Null or empty means
    /// unrestricted. Unlike <see cref="AccessConstraints"/>, which narrows the <em>listed</em> types and
    /// leaves every <em>unlisted</em> type untouched, this is an allow-list: only the types named here may
    /// appear in any result, and a type absent from a non-empty list is denied outright. That distinction
    /// is why both concepts exist — a per-type narrowing cannot express "the caller may see nothing else",
    /// so a resource type with no constraint reached through an <c>_include</c> would otherwise pass
    /// unguarded, a fail-open authorization bypass. This is the concept SMART-on-FHIR clinical scopes
    /// produce (the set of resource types a scope grants) and the reason a SMART request can be routed to
    /// this compiler at all.
    /// <para>
    /// It is a caller <em>permission</em>, distinct from <see cref="ResourceTypes"/>, which is caller
    /// <em>intent</em> (the <c>_type</c> the caller asked to search). Both may be set; the effective base
    /// set is their intersection. Enforced structurally by the compiler on every row-producing stage — the
    /// match set, every <c>_include</c>/<c>_revinclude</c>/<c>:iterate</c> stage — not by rewriting the
    /// search expression, so no reachability path can navigate around it.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> AllowedResourceTypes { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Which versions of a resource a search may return. A flags enum because _history returns latest and
/// history together, and $export may additionally need soft-deleted rows.
/// </summary>
/// <remarks>
/// A SQL data layer maps this onto the tri-state <c>Ignixa.Search.Sql.Ast.ResourceVisibility</c>, resolving
/// the <c>IsHistory</c> and <c>IsDeleted</c> columns independently: naming <see cref="Latest"/> without a
/// column's non-current partner pins it to <c>= 0</c>, naming the partner (<see cref="History"/> or
/// <see cref="SoftDeleted"/>) without <see cref="Latest"/> pins it to <c>= 1</c>, and naming both or neither
/// leaves it unfiltered. So <c>History</c> alone returns superseded versions exclusively, whereas
/// <c>Latest | History</c> returns the union of current and superseded; the two are genuinely different
/// searches, not merely different statements of intent.
/// </remarks>
[Flags]
public enum ResourceVersionTypes
{
    /// <summary>No version selected. Not a valid search input; present so the default is explicit.</summary>
    None = 0,

    /// <summary>The current version of each resource. The implicit baseline, not a relaxation.</summary>
    Latest = 1,

    /// <summary>Superseded versions.</summary>
    History = 2,

    /// <summary>Soft-deleted rows.</summary>
    SoftDeleted = 4,
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
    Ignixa.Models.CodeableConcept Details = null,
    string Diagnostics = null,
    IReadOnlyList<string> Location = null,
    IReadOnlyList<string> Expression = null);
