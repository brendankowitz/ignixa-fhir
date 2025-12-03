// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that advances the simulation time.
/// Used to model time passing between clinical events.
/// </summary>
public sealed class DelayState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the exact delay duration. If set, RangeMin/RangeMax are ignored.
    /// </summary>
    public TimeSpan? Exact { get; init; }

    /// <summary>
    /// Gets or sets the minimum delay when using a range.
    /// </summary>
    public TimeSpan? RangeMin { get; init; }

    /// <summary>
    /// Gets or sets the maximum delay when using a range.
    /// </summary>
    public TimeSpan? RangeMax { get; init; }

    /// <summary>
    /// Advances the simulation time by the specified or random duration.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);

        TimeSpan delay;

        if (Exact.HasValue)
        {
            delay = Exact.Value;
        }
        else if (RangeMin.HasValue && RangeMax.HasValue)
        {
            // Generate random delay within range
            var minTicks = RangeMin.Value.Ticks;
            var maxTicks = RangeMax.Value.Ticks;
            var randomTicks = _faker.Random.Long(minTicks, maxTicks);
            delay = TimeSpan.FromTicks(randomTicks);
        }
        else
        {
            // Default: 1 day
            delay = TimeSpan.FromDays(1);
        }

        context.AdvanceTime(delay);
    }

    /// <summary>
    /// Creates a DelayState with an exact duration.
    /// </summary>
    public static DelayState ExactDuration(TimeSpan duration) => new() { Exact = duration };

    /// <summary>
    /// Creates a DelayState with a random duration within the specified range.
    /// </summary>
    public static DelayState RandomDuration(TimeSpan min, TimeSpan max) => new() { RangeMin = min, RangeMax = max };

    /// <summary>
    /// Creates a DelayState for the specified number of days.
    /// </summary>
    public static DelayState Days(int days) => ExactDuration(TimeSpan.FromDays(days));

    /// <summary>
    /// Creates a DelayState for the specified number of weeks.
    /// </summary>
    public static DelayState Weeks(int weeks) => ExactDuration(TimeSpan.FromDays(weeks * 7));

    /// <summary>
    /// Creates a DelayState for the specified number of months.
    /// Uses 30.4375 days per month (365.25/12) for accurate age progression.
    /// </summary>
    public static DelayState Months(int months) => ExactDuration(TimeSpan.FromDays(months * 30.4375));

    /// <summary>
    /// Creates a DelayState for the specified number of years (approximate).
    /// </summary>
    public static DelayState Years(int years) => ExactDuration(TimeSpan.FromDays(years * 365));
}
