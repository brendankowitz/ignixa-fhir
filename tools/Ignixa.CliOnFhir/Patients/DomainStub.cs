// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>One domain of the root digest: how much it holds, its first items and how to open it.</summary>
/// <param name="NodeKey">The <c>patient_context</c> expand node listing the whole domain.</param>
/// <param name="Count">Items in the domain under its rule (active problems and medications, encounters in the window, lab codes).</param>
/// <param name="Items">The first items, at most five, in the domain's order.</param>
/// <param name="Rule">The rule that decided membership and order, as applied; absent when no rule applies.</param>
/// <param name="Excluded">Resources of the domain's types in the compartment that the rule left out; absent when it leaves none out by design.</param>
/// <param name="Window">The time window, for a domain that depends on the call's <c>asOf</c>.</param>
public sealed record DomainStub(
    string NodeKey,
    int Count,
    IReadOnlyList<ContextItem> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rule = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Excluded = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ContextWindow? Window = null);
