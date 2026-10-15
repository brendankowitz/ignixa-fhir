// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>One node of a patient's context, expanded.</summary>
/// <param name="Patient">The patient, <c>Patient/id</c>.</param>
/// <param name="NodeKey">The node, as requested.</param>
/// <param name="AsOf">The instant time-dependent nodes were read at.</param>
/// <param name="Total">Items in the node.</param>
/// <param name="Offset">Items skipped before the first returned.</param>
/// <param name="Returned">Items in <paramref name="Items"/>.</param>
/// <param name="Items">The page of items, in the node's order.</param>
/// <param name="Rule">The rule that decided membership, order or links, as applied; absent when none applies.</param>
/// <param name="Window">The time window, for a node that depends on <paramref name="AsOf"/>.</param>
/// <param name="Resources">The page's resources as they are in the folder (trimmed by <c>elements</c>), in item order; absent for a node of lab codes.</param>
/// <param name="ResolvedReferences">For each returned resource, the Bundle references it shows and the <c>Type/id</c> they resolved to; absent when none.</param>
public sealed record NodeFacts(
    string Patient,
    string NodeKey,
    DateTimeOffset AsOf,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<ContextItem> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rule = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ContextWindow? Window = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Resources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences = null);
