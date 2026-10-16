// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>How a search query would run over the folder, and, when it was run, what it found.</summary>
/// <param name="ResourceType">The searched type.</param>
/// <param name="Query">The query as received.</param>
/// <param name="Compartment">The compartment searched within; absent when none.</param>
/// <param name="Indexed">Resources of the searched type in the folder's index.</param>
/// <param name="Parameters">Each query parameter, then each sort key and include, in query order.</param>
/// <param name="Executed">Whether the search was run (<c>execute</c>).</param>
/// <param name="Results">What the search found, as <c>search_fhir_resources</c> returns it; absent unless executed.</param>
public sealed record PlanFacts(
    string ResourceType,
    string Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Compartment,
    int Indexed,
    IReadOnlyList<ParameterPlan> Parameters,
    bool Executed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SearchFacts? Results = null);
