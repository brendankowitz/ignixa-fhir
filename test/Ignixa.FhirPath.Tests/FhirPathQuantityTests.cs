/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath quantity literals.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests;

public class FhirPathQuantityTests
{
    private readonly FhirPathParser _parser = new();

    private QuantityExpression ParseQuantity(string input) =>
        Assert.IsType<QuantityExpression>(_parser.Parse(input));

    [Fact]
    public void GivenIntegerQuantity_WhenParsing_ThenReturnsQuantityExpression()
    {
        var quantity = ParseQuantity("5 'mg'");

        Assert.Equal(5m, quantity.Value);
        Assert.Equal("mg", quantity.Unit);
    }

    [Fact]
    public void GivenDecimalQuantity_WhenParsing_ThenReturnsQuantityExpression()
    {
        var quantity = ParseQuantity("37.5 'Cel'");

        Assert.Equal(37.5m, quantity.Value);
        Assert.Equal("Cel", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithBracketUnit_WhenParsing_ThenReturnsQuantityExpression()
    {
        var quantity = ParseQuantity("100 '[lb_av]'");

        Assert.Equal(100m, quantity.Value);
        Assert.Equal("[lb_av]", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithComplexUnit_WhenParsing_ThenReturnsQuantityExpression()
    {
        var quantity = ParseQuantity("120.5 'mm[Hg]'");

        Assert.Equal(120.5m, quantity.Value);
        Assert.Equal("mm[Hg]", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithWhitespace_WhenParsing_ThenReturnsQuantityExpression()
    {
        var quantity = ParseQuantity("42  'kg'");

        Assert.Equal(42m, quantity.Value);
        Assert.Equal("kg", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityToString_WhenCalling_ThenReturnsFormattedString()
    {
        var quantity = ParseQuantity("5.5 'mg'");
        var result = quantity.ToString();

        Assert.Equal("5.5 'mg'", result);
    }
}
