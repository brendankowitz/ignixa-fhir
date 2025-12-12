// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Extension methods for working with query parameters in FHIR endpoints.
/// </summary>
public static class QueryParameterExtensions
{
    /// <summary>
    /// Extracts the _pretty parameter from the query collection.
    /// Returns true if _pretty is present (with or without a value), false otherwise.
    /// According to FHIR spec, _pretty is a boolean parameter that controls JSON formatting.
    /// Per FHIR spec: presence of the parameter implies true, even without a value.
    /// </summary>
    /// <param name="query">The query collection from HttpRequest.Query</param>
    /// <returns>True if _pretty is present and not explicitly set to false/0, false otherwise</returns>
    public static bool GetPrettyParameter(this IQueryCollection query)
    {
        if (!query.TryGetValue("_pretty", out var prettyValues))
        {
            return false;
        }

        var prettyValue = prettyValues.FirstOrDefault();

        // Handle null or empty string (just ?_pretty with no value) - FHIR spec says presence implies true
        if (string.IsNullOrEmpty(prettyValue))
        {
            return true;
        }

        // Handle whitespace-only value as false
        if (string.IsNullOrWhiteSpace(prettyValue))
        {
            return false;
        }

        // FHIR spec allows: _pretty=true or just _pretty (presence implies true)
        // Also handle case-insensitive comparison
        return prettyValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               prettyValue.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
