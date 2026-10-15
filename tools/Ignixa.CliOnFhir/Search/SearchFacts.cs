// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>What a search found.</summary>
/// <param name="ResourceType">The searched resource type.</param>
/// <param name="Query">The query as received.</param>
/// <param name="Total">Every resource matching the query, regardless of paging.</param>
/// <param name="Offset">Matches skipped before the first returned resource.</param>
/// <param name="Returned">Resources in <paramref name="Resources"/>.</param>
/// <param name="Resources">The returned resources in catalog order; absent when only the count was requested.</param>
public sealed record SearchFacts(
    string ResourceType,
    string Query,
    int Total,
    int Offset,
    int Returned,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Resources);
