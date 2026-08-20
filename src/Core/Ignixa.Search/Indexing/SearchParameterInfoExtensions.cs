// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Ignixa.Search.Extensions;
using Ignixa.Search.Models;

namespace Ignixa.Search.Indexing;

public static class SearchParameterInfoExtensions
{
    /// <summary>
    /// Given a list of <see cref="SearchParameterInfo"/> calculates a hash using the
    /// <see cref="SearchParameterInfo.Url"/>, <see cref="SearchParameterInfo.Type"/>,
    /// <see cref="SearchParameterInfo.Expression"/>, <see cref="SearchParameterInfo.TargetResourceTypes"/>, and
    /// <see cref="SearchParameterInfo.BaseResourceTypes"/>,
    /// values of each component. The same collection of search parameter infos (irrespective of their order in the input)
    /// will return the same hash.
    /// </summary>
    /// <remarks>
    /// All ordering here is <see cref="StringComparer.Ordinal"/> because this hash is intended to be
    /// persisted and compared across servers to decide whether a reindex is required, once that reindex
    /// path lands; for that comparison to be meaningful, ordering must be stable across hosts. Linguistic
    /// collation varies by host locale, which would otherwise make a server spuriously compute a different
    /// hash purely because of where it runs -- see <c>SearchParameterHashCultureInvarianceTests</c> for the
    /// measured divergence.
    /// </remarks>
    /// <param name="searchParamaterInfos">A list of <see cref="SearchParameterInfo" /></param>
    /// <returns>A hash based on the search parameter uri, type, expression, target resource types, and base resource types.</returns>
    internal static string CalculateSearchParameterHash(this IEnumerable<SearchParameterInfo> searchParamaterInfos)
    {
        EnsureArg.IsNotNull(searchParamaterInfos, nameof(searchParamaterInfos));
        EnsureArg.IsGt(searchParamaterInfos.Count(), 0, nameof(searchParamaterInfos));

        var sb = new StringBuilder();
        foreach (SearchParameterInfo searchParamInfo in searchParamaterInfos.OrderBy(x => x.Url.ToString(), StringComparer.Ordinal))
        {
            sb.Append(searchParamInfo.Url);
            sb.Append(searchParamInfo.Type);
            sb.Append(searchParamInfo.Expression);

            if (searchParamInfo.SortStatus != SortParameterStatus.Disabled) sb.Append("sortable");

            if (searchParamInfo.TargetResourceTypes != null &&
                searchParamInfo.TargetResourceTypes.Any())
                sb.Append(string.Join(null, searchParamInfo.TargetResourceTypes.OrderBy(s => s, StringComparer.Ordinal)));

            if (searchParamInfo.BaseResourceTypes != null &&
                searchParamInfo.BaseResourceTypes.Any())
                sb.Append(string.Join(null, searchParamInfo.BaseResourceTypes.OrderBy(s => s, StringComparer.Ordinal)));
        }

        string hash = sb.ToString().ComputeHash();
        return hash;
    }
}
