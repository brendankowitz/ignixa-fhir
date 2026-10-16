// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>Which resources a validation covered.</summary>
/// <param name="Kind"><c>folder</c>, <c>type</c>, <c>search</c>, <c>patient</c> or <c>references</c>.</param>
/// <param name="ResourceType">The type validated, when the scope names one (or the requested profile constrains one).</param>
/// <param name="Query">The search query that chose them, as received.</param>
/// <param name="Patient">The patient whose compartment was validated (<c>Patient/id</c>, the id as its digest when not a FHIR id).</param>
/// <param name="Total">Resources in scope, before <c>offset</c> and <c>limit</c>.</param>
public sealed record ValidationScopeFacts(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResourceType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Patient,
    int Total);
