// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One code system the folder uses, in the compact answer. Its URI and codes are folder data; no data display is included.</summary>
/// <param name="System">The URI as written, or its digest when it is not an http(s) or urn URI without whitespace or control characters.</param>
/// <param name="Alias">The short name the patient map writes codes with (<c>sct</c>, <c>loinc</c>, ...); absent when it has none.</param>
/// <param name="Name">The well-known name; null when unknown.</param>
/// <param name="DistinctCodes">Distinct codes.</param>
/// <param name="Codings">Codings in all.</param>
/// <param name="Resources">Resources holding at least one.</param>
/// <param name="TopCodes">With <c>top</c>: the most frequent codes (bare) and their counts; absent otherwise.</param>
/// <param name="Loaded">With terminology loaded: <c>local</c>, <c>not-loaded</c> or <c>tx-server-available</c>; absent otherwise.</param>
public sealed record CodeSystemSummary(
    string System,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Alias,
    string? Name,
    int DistinctCodes,
    int Codings,
    int Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CodeCount>? TopCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Loaded);
