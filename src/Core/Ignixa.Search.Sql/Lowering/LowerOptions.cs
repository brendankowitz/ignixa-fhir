// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The optional inputs to <see cref="Lower.Run"/>, grouped onto one record so each is supplied by name
/// through an init-only property rather than as a trailing positional argument. The eight optionals used
/// to sit at the tail of a seventeen-parameter method, where <c>ResourceTypes</c>
/// (<see cref="IReadOnlyList{String}"/>) sat immediately before the authorization input
/// <c>AccessConstraints</c> (<see cref="IReadOnlyList{AccessConstraint}"/>); a caller slipping into
/// positional style could land the two in each other's slot with no compiler complaint. On a record the
/// only way to set either is by name, so that class of mistake cannot compile.
/// </summary>
public sealed record LowerOptions
{
    /// <summary>Emit a row count instead of the rows themselves. The compiler's only "count" concept.</summary>
    public bool CountOnly { get; init; }

    /// <summary>Cap the number of returned rows (a SQL <c>TOP</c>); null means no cap.</summary>
    public int? Top { get; init; }

    /// <summary>The reference instant a <c>:ap</c> (approximate) comparator widens around; null when unused.</summary>
    public DateTimeOffset? ApproximationReferenceTime { get; init; }

    /// <summary>Which resource versions the rows may include (latest, history, soft-deleted); null for the default.</summary>
    public ResourceVisibility? Visibility { get; init; }

    /// <summary>An inclusive surrogate-id range to bound the scan by; null for no bound.</summary>
    public SurrogateIdRange? SurrogateRange { get; init; }

    /// <summary>A parameter holding the expected search-parameter hash for reindex gating; null when unused.</summary>
    public SqlParameterRef? SearchParameterHash { get; init; }

    /// <summary>The types a wildcard/multi-type base set spans; null or empty means every type.</summary>
    public IReadOnlyList<string>? ResourceTypes { get; init; }

    /// <summary>
    /// The per-type access constraints enforced structurally on every row-producing stage. Null or empty
    /// means unrestricted. This is an authorization input; keeping it name-only is the point of this record.
    /// </summary>
    public IReadOnlyList<AccessConstraint>? AccessConstraints { get; init; }

    /// <summary>
    /// The global allow-list of resource types the caller is permitted to see, enforced structurally on
    /// every row-producing stage (the match set and every include/:iterate stage). Null or empty means
    /// unrestricted. This is an authorization input, deliberately distinct from <see cref="ResourceTypes"/>:
    /// <see cref="ResourceTypes"/> is caller <em>intent</em> (the <c>_type</c> the caller asked to search),
    /// this is caller <em>permission</em> (the types a SMART clinical scope grants). Both can be set; the
    /// effective base set is their intersection. Unlike <see cref="AccessConstraints"/>, which narrows only
    /// the types it lists and leaves unlisted types untouched, an allow-list denies every type it does not
    /// name — the semantics a per-type narrowing cannot express, and the one whose absence would let an
    /// <c>_include</c> reach an unpermitted type unguarded (a fail-open bypass). Keeping it name-only, like
    /// <see cref="AccessConstraints"/>, is the point of this record.
    /// </summary>
    public IReadOnlyList<string>? AllowedResourceTypes { get; init; }

    /// <summary>
    /// When true, the emitted statement returns include-stage rows only, omitting the match page from the
    /// result while still using it to seed the stages. This is the $includes operation's second page: the
    /// caller already has the match rows and asks only for more included resources.
    /// </summary>
    public bool IncludesOnly { get; init; }

    /// <summary>
    /// Allows typed leaf predicates to lower without a single target resource type, for system-level
    /// search. Deliberately explicit rather than inferred from a null target type: a null type already
    /// means "wildcard compartment", and <see cref="ResourceTypes"/> is orthogonal — that shapes the
    /// base set, this gates cross-type lowering of the leaves themselves. Both together is legal and is
    /// exactly the <c>GET /?_type=A,B&amp;name=foo</c> case.
    /// </summary>
    public bool SystemLevelSearch { get; init; }

    /// <summary>An OFFSET/FETCH page; mutually exclusive with keyset <c>page</c> and <c>Top</c>.</summary>
    public OffsetSpec? OffsetPage { get; init; }

    /// <summary>
    /// Scopes a <see cref="CountOnly"/> count to the current sort phase's own join output rather than the
    /// whole match set. The compiler-side half of two-phase sort execution.
    /// </summary>
    public bool CountPhaseScoped { get; init; }

    /// <summary>
    /// The keyset-pagination continuation token (boundary) for the second and subsequent pages of an
    /// <see cref="IncludesOnly"/> page: the last include row the previous page returned. Only meaningful
    /// together with <see cref="IncludesOnly"/>; <see cref="Lower.Run"/> rejects it otherwise, because the
    /// resume predicate only pages a stream of include rows and on an ordinary search would silently drop them.
    /// </summary>
    public IncludeBoundary? IncludeBoundary { get; init; }
}
