// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>Which configuration a run applied, described by its shape only: no value of it (a key, a path, a replacement) is ever repeated.</summary>
/// <param name="Source"><c>file</c> or <c>inline</c>; there is no default profile.</param>
/// <param name="File">The configuration file's absolute path, for <c>file</c>.</param>
/// <param name="Rules">The rules by method, in the order each method first appears.</param>
public sealed record DeIdConfigFacts(
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File,
    IReadOnlyList<DeIdRuleFacts> Rules);
