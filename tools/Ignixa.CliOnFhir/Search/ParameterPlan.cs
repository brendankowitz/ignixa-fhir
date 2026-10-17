// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>How one query parameter (or sort key, or include) was understood.</summary>
/// <param name="Name">The name as written, with its modifier or chain (<c>subject:Patient.name</c>), or <c>_sort</c>, <c>_include</c>, <c>_revinclude</c>.</param>
/// <param name="Value">The value as written; for a sort key or include, the key or include it parsed to.</param>
/// <param name="Outcome">
/// <c>compiled</c> (it filters), <c>known-miss</c> (it compiled to a test that can never match, see <paramref name="Reason"/>),
/// <c>ignored</c> (lenient mode dropped it; see <c>issues</c>) or <c>failed</c>.
/// </param>
/// <param name="Reason">Why it can never match, or why it was ignored; absent otherwise.</param>
/// <param name="Modifiers">The modifiers applied (<c>not</c>, <c>exact</c>, <c>missing</c>, ...); empty for none.</param>
/// <param name="Prefixes">For number, date and quantity parameters, the comparison prefixes applied (<c>eq</c>, <c>ge</c>, ...); empty otherwise.</param>
/// <param name="Resolves">The definitions it resolved to, along a chain in order.</param>
public sealed record ParameterPlan(
    string Name,
    string Value,
    string Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<ParameterBinding> Resolves);
