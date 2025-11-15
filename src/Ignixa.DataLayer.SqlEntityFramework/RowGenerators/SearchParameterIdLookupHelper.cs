// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// Helper for looking up SearchParamId from searchParameterIdMap with support for OverridesUrl fallback.
/// </summary>
/// <remarks>
/// When Implementation Guides (like US Core) override base FHIR search parameters, the winning parameter
/// has a different URL but should use the same SearchParamId as the base parameter for indexing.
/// This helper implements the fallback logic:
/// 1. Try to lookup using the search parameter's URL
/// 2. If not found and the parameter has an OverridesUrl, try to lookup using that URL
/// 3. Return false if both lookups fail
/// </remarks>
public static class SearchParameterIdLookupHelper
{
    /// <summary>
    /// Attempts to get the SearchParamId for a search parameter, checking OverridesUrl as a fallback.
    /// </summary>
    /// <param name="searchParameter">The search parameter to look up.</param>
    /// <param name="searchParameterIdMap">Map of search parameter URLs to SearchParamIds.</param>
    /// <param name="searchParamId">The found SearchParamId, or 0 if not found.</param>
    /// <returns>True if a SearchParamId was found (either via URL or OverridesUrl), false otherwise.</returns>
    public static bool TryGetSearchParamId(
        SearchParameterInfo searchParameter,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        out short searchParamId)
    {
        var searchParamUrl = searchParameter.Url?.ToString();
        if (searchParamUrl == null)
        {
            searchParamId = 0;
            return false;
        }

        // Try primary lookup using the parameter's URL
        if (searchParameterIdMap.TryGetValue(searchParamUrl, out searchParamId))
        {
            return true;
        }

        // Fallback: if this parameter overrides another parameter, try the overridden URL
        if (searchParameter.OverridesUrl != null)
        {
            var overridesUrl = searchParameter.OverridesUrl.ToString();
            if (searchParameterIdMap.TryGetValue(overridesUrl, out searchParamId))
            {
                return true;
            }
        }

        searchParamId = 0;
        return false;
    }
}
