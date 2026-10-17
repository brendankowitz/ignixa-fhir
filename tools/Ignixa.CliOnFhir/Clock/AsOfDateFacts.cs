// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>What a view read as <c>%asOfDate</c>: the session's day, or the value a variable or the view's own constant put in its place.</summary>
/// <param name="Value">The date as written when it is a FHIR date; null when it is not.</param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
/// <param name="Digest">When the value is not a date, its digest; absent otherwise.</param>
public sealed record AsOfDateFacts(
    string? Value,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Digest = null);
