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
}
