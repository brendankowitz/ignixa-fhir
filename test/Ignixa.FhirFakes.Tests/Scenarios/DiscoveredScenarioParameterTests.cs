// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.FhirFakes.Scenarios;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class DiscoveredScenarioParameterTests
{
    private enum Severity
    {
        Low = 0,
        High = 1,
    }

    [Fact]
    public void GivenIntValue_WhenParsing_ThenReturnsInt()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int) };

        var parsed = parameter.TryParseValue("42", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void GivenDecimalValueUnderDeCulture_WhenParsing_ThenStaysInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var parameter = new DiscoveredScenarioParameter { Name = "bmi", Type = typeof(decimal) };

            var parsed = parameter.TryParseValue("35.5", out var value);

            parsed.ShouldBeTrue();
            value.ShouldBe(35.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void GivenBoolValue_WhenParsing_ThenReturnsBool()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "flag", Type = typeof(bool) };

        var parsed = parameter.TryParseValue("true", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe(true);
    }

    [Fact]
    public void GivenEnumValue_WhenParsing_ThenConvertsCaseInsensitively()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "severity", Type = typeof(Severity) };

        var parsed = parameter.TryParseValue("high", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe(Severity.High);
    }

    [Fact]
    public void GivenUndefinedEnumOrdinal_WhenParsing_ThenReturnsFalse()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "severity", Type = typeof(Severity) };

        var parsed = parameter.TryParseValue("999", out var value);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GivenDefinedEnumOrdinalString_WhenParsing_ThenReturnsFalse()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "severity", Type = typeof(Severity) };

        var parsed = parameter.TryParseValue("1", out var value);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GivenStringValue_WhenParsing_ThenReturnsRawString()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "gender", Type = typeof(string) };

        var parsed = parameter.TryParseValue("female", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe("female");
    }

    [Fact]
    public void GivenUnparseableIntValue_WhenParsing_ThenReturnsFalse()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int) };

        var parsed = parameter.TryParseValue("notanumber", out var value);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GivenIntBelowMin_WhenParsing_ThenReturnsFalseWithRangeReason()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int), Min = 18, Max = 85 };

        var parsed = parameter.TryParseValue("5", out var value, out var failureReason);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
        // Must not be reported as a type-conversion failure (5 converts to int just fine) — the
        // reason should name the actual problem: the value, and the allowed range.
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain("5");
        failureReason.ShouldContain("18");
        failureReason.ShouldContain("85");
    }

    [Fact]
    public void GivenIntAboveMax_WhenParsing_ThenReturnsFalseWithRangeReason()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int), Max = 85 };

        var parsed = parameter.TryParseValue("200", out var value, out var failureReason);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain("200");
        failureReason.ShouldContain("85");
    }

    [Fact]
    public void GivenIntWithinRange_WhenParsing_ThenReturnsInt()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int), Min = 18, Max = 85 };

        var parsed = parameter.TryParseValue("40", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe(40);
    }

    [Fact]
    public void GivenDecimalOutsideRange_WhenParsing_ThenReturnsFalse()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "bmi", Type = typeof(decimal), Min = 10, Max = 50 };

        var parsed = parameter.TryParseValue("75.5", out var value);

        parsed.ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GivenNullableIntValue_WhenParsing_ThenReturnsInt()
    {
        var parameter = new DiscoveredScenarioParameter { Name = "age", Type = typeof(int?) };

        var parsed = parameter.TryParseValue("7", out var value);

        parsed.ShouldBeTrue();
        value.ShouldBe(7);
    }
}
