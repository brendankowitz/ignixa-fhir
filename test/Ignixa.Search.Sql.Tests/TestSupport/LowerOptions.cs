// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Test-support only. The optional inputs Lower.Run took before it was collapsed onto CompilationContext,
/// preserved so the existing corpus of lowering tests migrates by renaming the call and nothing else. No
/// production code references this type; new tests should build a CompilationContext directly.
/// </summary>
internal sealed record LowerOptions
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
}
