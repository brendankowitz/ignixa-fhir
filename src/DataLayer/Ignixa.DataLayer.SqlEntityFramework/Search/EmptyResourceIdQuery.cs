// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// The empty result set a query generator returns when a search can match nothing — for example a system
/// URI with no <c>dbo.System</c> row.
/// </summary>
/// <remarks>
/// <para>
/// The invariant this exists to hold is that every <c>IQueryable&lt;long&gt;</c> a query generator returns is
/// rooted in the same <see cref="FhirDbContext"/>, so callers may compose it with any operator.
/// </para>
/// <para>
/// <c>Enumerable.Empty&lt;long&gt;().AsQueryable()</c> reads as equivalent and is not. It carries the in-memory
/// <c>EnumerableQuery</c> provider, which EF inlines into the surrounding tree as an inline query root.
/// <c>Contains</c> survives that — an empty <c>IN</c> list folds to <c>WHERE 0 = 1</c> — but the set
/// operators (<c>Except</c>, <c>Union</c>, <c>Intersect</c>, <c>Concat</c>) need a real relational source
/// and EF refuses to emit a zero-row <c>VALUES</c> clause: "Empty collections are not supported as inline
/// query roots." Composition succeeds and the throw lands when the query is compiled, which for a search is
/// the first <c>MoveNextAsync</c> — after the 200 and the bundle header are already on the wire, so the
/// client sees a truncated body rather than an error.
/// </para>
/// </remarks>
internal static class EmptyResourceIdQuery
{
    internal static IQueryable<long> EmptyResourceIds(this FhirDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Resources.Where(_ => false).Select(r => r.ResourceSurrogateId);
    }
}
