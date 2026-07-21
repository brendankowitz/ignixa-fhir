// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Models;
using Ignixa.Abstractions;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Interface for building SearchOptions from parsed query parameters.
/// </summary>
public interface ISearchOptionsBuilder
{
    /// <summary>
    /// Builds SearchOptions from parsed query parameters.
    /// </summary>
    /// <param name="resourceType">The resource type being searched (e.g., "Patient"), or null for system-wide search.</param>
    /// <param name="parameters">The parsed query parameters.</param>
    /// <param name="schemaProvider">Optional schema provider for validating _elements parameter.</param>
    /// <param name="outcomes">
    /// Optional collector for per-parameter provenance trace entries. When non-null, each
    /// <see cref="ParameterCategory.Search"/> parameter is parsed with its syntax projection and
    /// appended as a <see cref="ParameterTrace"/> — a <see cref="ParameterOutcome.Compiled"/> entry on
    /// success, or a <see cref="ParameterOutcome.Ignored"/> entry when FHIR lenient handling drops it.
    /// Leave null on production hot paths to avoid the syntax projection cost.
    /// </param>
    /// <returns>A SearchOptions instance configured according to the parameters.</returns>
    SearchOptions Build(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISchema? schemaProvider = null,
        IList<ParameterTrace>? outcomes = null);
}
