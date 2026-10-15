// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>What <c>context_find</c> matched in a patient's context.</summary>
/// <param name="Patient">The patient, <c>Patient/id</c>.</param>
/// <param name="Query">The query, as received.</param>
/// <param name="Rule">How the query was matched (code, text or both) and against which elements.</param>
/// <param name="Total">Matching resources.</param>
/// <param name="Offset">Matches skipped before the first returned.</param>
/// <param name="Returned">Items in <paramref name="Items"/>.</param>
/// <param name="NodeKeys">The distinct node keys of the returned items, in order: where to go next.</param>
/// <param name="Items">The page of matches, code matches before text matches.</param>
/// <param name="Resources">The page's resources as they are in the folder (trimmed by <c>elements</c>), in item order.</param>
/// <param name="ResolvedReferences">For each returned resource, the Bundle references it shows and the <c>Type/id</c> they resolved to; absent when none.</param>
public sealed record FindFacts(
    string Patient,
    string Query,
    string Rule,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<ContextItem> Items,
    IReadOnlyList<JsonNode> Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences = null);
