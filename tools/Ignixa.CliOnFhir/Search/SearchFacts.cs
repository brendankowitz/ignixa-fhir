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
/// <param name="Compartment">The compartment searched within (<c>Patient/123</c>); absent when the search was not limited to one.</param>
/// <param name="Total">Every resource matching the query, regardless of paging.</param>
/// <param name="Offset">Matches skipped before the first returned resource.</param>
/// <param name="Returned">Resources in <paramref name="Resources"/>.</param>
/// <param name="Resources">
/// The returned resources, in <c>_sort</c> order or else catalog order; absent when only the count was requested.
/// </param>
/// <param name="Included">
/// The resources <c>_include</c>/<c>_revinclude</c> add to the returned page, in discovery order; absent when the query
/// has neither. Not counted in <paramref name="Total"/> or <paramref name="Returned"/>; any left out are in <c>omitted</c>.
/// </param>
/// <param name="ResolvedReferences">
/// For each returned or included resource (<c>Type/id</c>), the Bundle references it shows as written (<c>urn:uuid:...</c>, a
/// fullUrl) and the <c>Type/id</c> they were matched as; the resources themselves are returned unchanged. At most
/// 100 are listed (the rest are counted in <c>omitted</c>); absent when the returned resources show none.
/// </param>
public sealed record SearchFacts(
    string ResourceType,
    string Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Compartment,
    int Total,
    int Offset,
    int Returned,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Included = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences = null);
