// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Interface for parsing HTTP query string parameters.
/// </summary>
[System.CLSCompliant(false)]
public interface IQueryParameterParser
{
    /// <summary>
    /// Parses the query string from an HTTP request.
    /// </summary>
    /// <param name="queryString">The query collection from the HTTP request.</param>
    /// <returns>A list of parsed query parameters.</returns>
    IReadOnlyList<QueryParameter> Parse(IQueryCollection queryString);

    /// <summary>
    /// Parses a raw query string (without the leading '?').
    /// </summary>
    /// <param name="queryString">The raw query string (e.g., "name=John&amp;_count=10").</param>
    /// <returns>A list of parsed query parameters.</returns>
    IReadOnlyList<QueryParameter> Parse(string queryString);
}
