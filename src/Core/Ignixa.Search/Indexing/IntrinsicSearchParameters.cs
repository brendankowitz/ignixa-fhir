// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;

namespace Ignixa.Search.Indexing;

/// <summary>
/// The search parameter codes whose values are intrinsic to the resource record itself rather than
/// extracted into a search index.
/// </summary>
/// <remarks>
/// <para>
/// Intrinsic parameters carry no SearchParamId, so any caller that resolves, dispatches, or classifies by
/// SearchParamId must skip them: an indexer emits no index entry for them, and a query compiler reads them
/// from the record it is already selecting. A <c>_sort</c> on one of them therefore needs no captured sort
/// value — the page boundary already identifies it — whereas a sort on any other parameter does.
/// </para>
/// <para>
/// Note that this is narrower than "resource metadata": <c>_tag</c>, <c>_profile</c>, <c>_security</c> and
/// <c>_source</c> are metadata too, but they are extracted into the search index like any other parameter
/// and so are not intrinsic.
/// </para>
/// <para>
/// This is a contract of Ignixa's indexing pipeline rather than a claim about storage in general: these
/// three are never assigned a SearchParamId and <c>ElementSearchIndexer</c> never emits an entry for them,
/// so a data layer must resolve them from its own record whether or not its store could have indexed them.
/// It lives here rather than in a data layer because that contract is set here, and because a caller often
/// has to make the call before it has a compiled plan to inspect — while it still holds only parameter
/// codes. A SQL data layer draws the same line again after lowering: <c>SortKeyKind.LastUpdated</c>,
/// <c>SortKeyKind.ResourceType</c> and <c>SortKeyKind.ResourceId</c> are exactly these three.
/// </para>
/// </remarks>
public static class IntrinsicSearchParameters
{
    private static readonly FrozenSet<string> CodeSet = new[]
    {
        SearchParameterNames.Id,
        SearchParameterNames.ResourceType,
        SearchParameterNames.LastUpdated,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The codes, compared ordinally. Exposed as a set so a caller that validates a <c>_sort</c> key or
    /// reconstructs a page boundary can enumerate them, not only test one at a time.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="IReadOnlySet{T}"/> rather than the concrete backing set: this assembly ships as a
    /// stable package, so the property's return type is a permanent commitment and must not pin the storage
    /// choice. Prefer <see cref="IsIntrinsicCode"/> for a single test — it also tolerates a null code, which
    /// <see cref="IReadOnlySet{T}.Contains"/> is not required to.
    /// </remarks>
    public static IReadOnlySet<string> Codes => CodeSet;

    /// <summary>
    /// True when <paramref name="parameterCode"/> names an intrinsic search parameter rather than one
    /// backed by a search index. A null code is not one, matching how an unclassified code falls through
    /// everywhere else.
    /// </summary>
    /// <param name="parameterCode">The search parameter code, for example <c>_lastUpdated</c>.</param>
    /// <returns>Whether the parameter is intrinsic to the resource record.</returns>
    public static bool IsIntrinsicCode(string parameterCode)
        => parameterCode is not null && CodeSet.Contains(parameterCode);
}
