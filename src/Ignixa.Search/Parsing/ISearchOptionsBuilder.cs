// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Interface for building SearchOptions from parsed query parameters.
/// </summary>
public interface ISearchOptionsBuilder
{
    /// <summary>
    /// Builds SearchOptions from parsed query parameters.
    /// </summary>
    /// <param name="resourceType">The resource type being searched (e.g., "Patient").</param>
    /// <param name="parameters">The parsed query parameters.</param>
    /// <returns>A SearchOptions instance configured according to the parameters.</returns>
    SearchOptions Build(string resourceType, IReadOnlyList<QueryParameter> parameters);
}
