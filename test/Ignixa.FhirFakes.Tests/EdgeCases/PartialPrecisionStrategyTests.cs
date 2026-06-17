// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Bogus;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class PartialPrecisionStrategyTests
{
    private static readonly Regex FhirPartialDateRegex = new(@"^\d{4}(-\d{2})?$", RegexOptions.CultureInvariant);

    private static string PatientWithBirthDate(string birthDate) => $$"""
        {
          "resourceType": "Patient",
          "id": "pp-test",
          "birthDate": "{{birthDate}}",
          "name": [{ "family": "Smith" }]
        }
        """;

    [Fact]
    public void GivenFullDateValue_WhenPartialPrecisionApplied_ThenResultIsYearOnlyOrYearMonth()
    {
        var strategy = new PartialPrecisionTemporalStrategy();
        var target = EdgeCaseTargetFactory.AtPath(PatientWithBirthDate("1990-03-15"), "Patient.birthDate");

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
        var strategy = new PartialPrecisionTemporalStrategy();
        var target = EdgeCaseTargetFactory.AtPath(PatientWithBirthDate("1990"), "Patient.birthDate");

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
        var strategy = new PartialPrecisionTemporalStrategy();
        var target = EdgeCaseTargetFactory.AtPath(PatientWithBirthDate("1990-03"), "Patient.birthDate");

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
        var strategy = new PartialPrecisionTemporalStrategy();
        var target = EdgeCaseTargetFactory.AtPath(PatientWithBirthDate("1990-03-15"), "Patient.birthDate");

        var canApply = strategy.CanApply(target);

        canApply.ShouldBeTrue();
    }

    [Fact]
    public void GivenPartialPrecisionStrategy_WhenCanApplyOnFreeText_ThenFalse()
    {
        var strategy = new PartialPrecisionTemporalStrategy();
        var target = EdgeCaseTargetFactory.AtPath(PatientWithBirthDate("1990-03-15"), "Patient.name[0].family");

        var canApply = strategy.CanApply(target);

        canApply.ShouldBeFalse();
    }
}
