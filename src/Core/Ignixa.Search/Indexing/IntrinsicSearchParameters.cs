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
/// from the record it is already selecting. The distinction is also what makes a <c>_sort</c> key "custom" —
/// a sort on an intrinsic parameter orders by something the keyset boundary already identifies, whereas a
/// sort on any other parameter needs a join and a captured sort value.
/// </para>
/// <para>
/// Note that this is narrower than "resource metadata": <c>_tag</c>, <c>_profile</c>, <c>_security</c> and
/// <c>_source</c> are metadata too, but they are indexed as tokens and so are not intrinsic.
/// </para>
/// <para>
/// This lives here, storage-agnostic, rather than in a data layer because the classification is a property
/// of the search parameters themselves. A SQL data layer draws the same line again after lowering — its
/// <c>SortKeyKind.LastUpdated</c>, <c>SortKeyKind.ResourceType</c> and <c>SortKeyKind.ResourceId</c> are
/// exactly these three — but a host must often make the call <em>before</em> compiling, while it still
/// holds only parameter codes: deciding whether a continuation token can be reconstructed into a typed or
/// typeless page boundary, for instance, has to happen before there is a plan to inspect. Without a shared
/// definition, each layer duplicates the literal set and they drift.
/// </para>
/// </remarks>
public static class IntrinsicSearchParameters
{
    /// <summary>
    /// The codes, compared ordinally. Exposed as a set because a host that validates a <c>_sort</c> key or
    /// reconstructs a page boundary needs to enumerate them, not only test one at a time.
    /// </summary>
    public static FrozenSet<string> Codes { get; } = new[]
    {
        SearchParameterNames.Id,
        SearchParameterNames.ResourceType,
        SearchParameterNames.LastUpdated,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="parameterCode"/> names an intrinsic search parameter rather than one
    /// backed by a search index. A null code is not one, matching how an unclassified code falls through
    /// everywhere else.
    /// </summary>
    /// <param name="parameterCode">The search parameter code, for example <c>_lastUpdated</c>.</param>
    /// <returns>Whether the parameter is intrinsic to the resource record.</returns>
    public static bool IsIntrinsicCode(string parameterCode)
        => parameterCode is not null && Codes.Contains(parameterCode);
}
