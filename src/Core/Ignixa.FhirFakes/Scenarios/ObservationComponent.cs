// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Represents a component of a multi-component observation.
/// </summary>
public sealed class ObservationComponent
{
    /// <summary>
    /// Gets or sets the component code.
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the numeric value.
    /// </summary>
    public decimal? Value { get; init; }

    /// <summary>
    /// Gets or sets the unit.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets or sets the UCUM unit code.
    /// </summary>
    public string? UnitCode { get; init; }

    /// <summary>
    /// Gets or sets the minimum value for random generation.
    /// </summary>
    public decimal? ValueRangeMin { get; init; }

    /// <summary>
    /// Gets or sets the maximum value for random generation.
    /// </summary>
    public decimal? ValueRangeMax { get; init; }

    /// <summary>
    /// Gets or sets a function to calculate the value from context.
    /// </summary>
    public Func<ScenarioContext, decimal>? ValueFromContext { get; init; }
}
