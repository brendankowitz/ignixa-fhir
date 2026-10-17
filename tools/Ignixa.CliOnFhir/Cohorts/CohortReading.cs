// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>An Observation's reading for an observation criterion.</summary>
/// <param name="Instant">When it was taken.</param>
/// <param name="Value">Its numeric value; null only when the criterion asks for a reading without a comparison.</param>
/// <param name="Unit">valueQuantity.code, else valueQuantity.unit.</param>
internal sealed record CohortReading(DateTimeOffset Instant, decimal? Value, string? Unit);
