// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Parses HTTP query string parameters into structured QueryParameter objects.
/// </summary>
[System.CLSCompliant(false)]
public class QueryParameterParser : IQueryParameterParser
{
    /// <summary>
    /// Parses the query string from an HTTP request.
    /// </summary>
    /// <param name="queryString">The query collection from the HTTP request.</param>
    /// <returns>A list of parsed query parameters.</returns>
    public IReadOnlyList<QueryParameter> Parse(IQueryCollection queryString)
    {
        if (queryString == null || queryString.Count == 0)
        {
            return Array.Empty<QueryParameter>();
        }

        var parameters = new List<QueryParameter>(queryString.Count);

        foreach (var kvp in queryString)
        {
            string parameterName = kvp.Key;

            // Handle multiple values for the same parameter name
            // FHIR allows parameters to be repeated (e.g., name=John&name=Jane)
            foreach (string value in kvp.Value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    parameters.Add(new QueryParameter(parameterName, value));
                }
            }
        }

        return parameters;
    }

    /// <summary>
    /// Parses a raw query string (without the leading '?').
    /// </summary>
    /// <param name="queryString">The raw query string (e.g., "name=John&amp;_count=10").</param>
    /// <returns>A list of parsed query parameters.</returns>
    public IReadOnlyList<QueryParameter> Parse(string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return Array.Empty<QueryParameter>();
        }

        // Remove leading '?' if present
        if (queryString.StartsWith('?'))
        {
            queryString = queryString.Substring(1);
        }

        var parameters = new List<QueryParameter>();

        // Split by '&' to get individual parameters
        ReadOnlySpan<char> remaining = queryString.AsSpan();

        while (remaining.Length > 0)
        {
            int ampersandIndex = remaining.IndexOf('&');
            ReadOnlySpan<char> parameter;

            if (ampersandIndex >= 0)
            {
                parameter = remaining.Slice(0, ampersandIndex);
                remaining = remaining.Slice(ampersandIndex + 1);
            }
            else
            {
                parameter = remaining;
                remaining = ReadOnlySpan<char>.Empty;
            }

            // Split parameter into name and value
            int equalsIndex = parameter.IndexOf('=');

            if (equalsIndex >= 0)
            {
                string name = parameter.Slice(0, equalsIndex).ToString();
                string value = parameter.Slice(equalsIndex + 1).ToString();

                // URL decode the value
                value = Uri.UnescapeDataString(value);

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                {
                    parameters.Add(new QueryParameter(name, value));
                }
            }
        }

        return parameters;
    }
}
