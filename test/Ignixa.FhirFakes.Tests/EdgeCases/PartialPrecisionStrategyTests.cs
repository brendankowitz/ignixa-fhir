// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class PartialPrecisionStrategyTests
{
    private static readonly Regex FhirPartialDateRegex = new(@"^\d{4}(-\d{2})?$", RegexOptions.CultureInvariant);

    [Fact]
    public void GivenFullDateValue_WhenPartialPrecisionApplied_ThenResultIsYearOnlyOrYearMonth()
    {
        const string input = "1990-03-15";
        var strategy = new PartialPrecisionTemporalStrategy();
        var parent = new JsonObject { { "birthDate", input } };
        var target = new PropertyTarget(parent, "birthDate", "birthDate", input);

        for (var seed = 0; seed < 10; seed++)
        {
            var rng = new Randomizer(seed);
            var result = strategy.Apply(target, rng);

            FhirPartialDateRegex.IsMatch(result.NewValue).ShouldBeTrue($"Seed {seed}: '{result.NewValue}' does not match FHIR partial date pattern");
            result.NewValue.StartsWith("1990", StringComparison.Ordinal).ShouldBeTrue($"Seed {seed}: '{result.NewValue}' does not start with year 1990");
        }
    }

    [Fact]
    public void GivenYearOnlyValue_WhenPartialPrecisionApplied_ThenResultIsStillYear()
    {
        const string input = "1990";
        var strategy = new PartialPrecisionTemporalStrategy();
        var parent = new JsonObject { { "birthDate", input } };
        var target = new PropertyTarget(parent, "birthDate", "birthDate", input);

        for (var seed = 0; seed < 10; seed++)
        {
            var rng = new Randomizer(seed);
            var result = strategy.Apply(target, rng);

            var isValidPartialDate = result.NewValue == "1990" || result.NewValue == "1990-01";
            isValidPartialDate.ShouldBeTrue($"Seed {seed}: '{result.NewValue}' is neither '1990' nor '1990-01'");
            FhirPartialDateRegex.IsMatch(result.NewValue).ShouldBeTrue($"Seed {seed}: '{result.NewValue}' does not match FHIR partial date pattern");
        }
    }

    [Fact]
    public void GivenYearMonthValue_WhenPartialPrecisionApplied_ThenResultIsYearOnlyOrSameYearMonth()
    {
        const string input = "1990-03";
        var strategy = new PartialPrecisionTemporalStrategy();
        var parent = new JsonObject { { "birthDate", input } };
        var target = new PropertyTarget(parent, "birthDate", "birthDate", input);

        for (var seed = 0; seed < 10; seed++)
        {
            var rng = new Randomizer(seed);
            var result = strategy.Apply(target, rng);

            result.NewValue.StartsWith("1990", StringComparison.Ordinal).ShouldBeTrue($"Seed {seed}: '{result.NewValue}' does not start with year 1990");
            FhirPartialDateRegex.IsMatch(result.NewValue).ShouldBeTrue($"Seed {seed}: '{result.NewValue}' does not match FHIR partial date pattern");
        }
    }

    [Fact]
    public void GivenPartialPrecisionStrategy_WhenCanApplyOnDate_ThenTrue()
    {
        const string input = "1990-03-15";
        var strategy = new PartialPrecisionTemporalStrategy();
        var parent = new JsonObject { { "birthDate", input } };
        var target = new PropertyTarget(parent, "birthDate", "birthDate", input);

        var canApply = strategy.CanApply(target);

        canApply.ShouldBeTrue();
    }

    [Fact]
    public void GivenPartialPrecisionStrategy_WhenCanApplyOnFreeText_ThenFalse()
    {
        const string input = "Smith";
        var strategy = new PartialPrecisionTemporalStrategy();
        var parent = new JsonObject { { "family", input } };
        var target = new PropertyTarget(parent, "family", "family", input);

        var canApply = strategy.CanApply(target);

        canApply.ShouldBeFalse();
    }
}
