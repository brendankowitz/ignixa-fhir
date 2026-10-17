// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>How far evidence can say which resource a row was made from.</summary>
/// <param name="EvidenceRows">When row provenance is available: evidence cites the source resource of up to this many of the first rows shown.</param>
/// <param name="Unavailable">Why it is not: the engine orders the rows of a top-level <c>unionAll</c> across resources, so a row has no single source.</param>
public sealed record ViewProvenance(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EvidenceRows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unavailable);
