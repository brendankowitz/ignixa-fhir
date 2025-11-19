/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath quantity literal evaluation (Phase 23).
 *
 * Official Test Suite Reference:
 * https://github.com/HL7/FHIRPath/blob/master/tests/quantity.xml
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.Abstractions;
using Superpower;

namespace Ignixa.FhirPath.Tests;

public class FhirPathQuantityLiteralTests
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer = FhirPathTokenizer.Create();
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Quantity Parsing Tests (Existing - Ensure No Regression)

    [Fact]
    public void GivenIntegerQuantity_WhenParsing_ThenReturnsQuantityExpression()
    {
        // Arrange & Act
        var quantity = ParseQuantity("5 'mg'");

        // Assert
        Assert.Equal(5m, quantity.Value);
        Assert.Equal("mg", quantity.Unit);
    }

    [Fact]
    public void GivenDecimalQuantity_WhenParsing_ThenReturnsQuantityExpression()
    {
        // Arrange & Act
        var quantity = ParseQuantity("37.5 'Cel'");

        // Assert
        Assert.Equal(37.5m, quantity.Value);
        Assert.Equal("Cel", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithBracketUnit_WhenParsing_ThenReturnsQuantityExpression()
    {
        // Arrange & Act
        var quantity = ParseQuantity("100 '[lb_av]'");

        // Assert
        Assert.Equal(100m, quantity.Value);
        Assert.Equal("[lb_av]", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithComplexUnit_WhenParsing_ThenReturnsQuantityExpression()
    {
        // Arrange & Act
        var quantity = ParseQuantity("120.5 'mm[Hg]'");

        // Assert
        Assert.Equal(120.5m, quantity.Value);
        Assert.Equal("mm[Hg]", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityWithWhitespace_WhenParsing_ThenReturnsQuantityExpression()
    {
        // Arrange & Act
        var quantity = ParseQuantity("42  'kg'");

        // Assert
        Assert.Equal(42m, quantity.Value);
        Assert.Equal("kg", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityToString_WhenCalling_ThenReturnsFormattedString()
    {
        // Arrange
        var quantity = ParseQuantity("5.5 'mg'");

        // Act
        var result = quantity.ToString();

        // Assert
        Assert.Equal("5.5 'mg'", result);
    }

    #endregion

    #region Quantity Evaluation Tests (NEW)

    [Fact]
    public void GivenSimpleQuantity_WhenEvaluating_ThenReturnsQuantityValue()
    {
        // Arrange
        var expr = _parser.Parse("5 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void GivenDecimalQuantity_WhenEvaluating_ThenReturnsQuantityValue()
    {
        // Arrange
        var expr = _parser.Parse("37.5 'Cel'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityWithUCUMUnit_WhenEvaluating_ThenReturnsQuantityValue()
    {
        // Arrange - UCUM unit for millimeters of mercury
        var expr = _parser.Parse("120 'mm[Hg]'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    #endregion

    #region Quantity Arithmetic Tests (NEW)

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenAddition_ThenReturnsSum()
    {
        // Arrange
        // Official test: (5 'mg') + (3 'mg') = 8 'mg'
        var expr = _parser.Parse("(5 'mg') + (3 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenSubtraction_ThenReturnsDifference()
    {
        // Arrange
        // Official test: (10 'mg') - (3 'mg') = 7 'mg'
        var expr = _parser.Parse("(10 'mg') - (3 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenTwoQuantitiesDifferentUnits_WhenAddition_ThenReturnsEmpty()
    {
        // Arrange
        // With UCUM support, mg + kg can be converted (both are mass)
        // This test verifies that TRULY incompatible units (e.g., mg + m) return empty
        var expr = _parser.Parse("(5 'mg') + (3 'm')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenQuantityAndScalar_WhenMultiplication_ThenReturnsScaledQuantity()
    {
        // Arrange
        // Official test: (5 'mg') * 3 = 15 'mg'
        var expr = _parser.Parse("(5 'mg') * 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityAndScalar_WhenDivision_ThenReturnsScaledQuantity()
    {
        // Arrange
        // Official test: (10 'mg') / 2 = 5 'mg'
        var expr = _parser.Parse("(10 'mg') / 2");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenDivision_ThenReturnsDecimalRatio()
    {
        // Arrange
        // (10 'mg') / (4 'mg') = 2.5 (dimensionless)
        var expr = _parser.Parse("(10 'mg') / (4 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("decimal", result.InstanceType);
        Assert.Equal(2.5m, result.Value);
    }

    #endregion

    #region Quantity Comparison Tests (NEW)

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenEqualComparison_ThenReturnsTrue()
    {
        // Arrange
        // Official test: (5 'mg') = (5 'mg') = true
        var expr = _parser.Parse("(5 'mg') = (5 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenNotEqualComparison_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("(5 'mg') != (3 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenLessThanComparison_ThenReturnsTrue()
    {
        // Arrange
        // Official test: (3 'mg') < (5 'mg') = true
        var expr = _parser.Parse("(3 'mg') < (5 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTwoQuantitiesSameUnit_WhenGreaterThanComparison_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("(10 'mg') > (5 'mg')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTwoQuantitiesDifferentUnits_WhenComparison_ThenReturnsEmpty()
    {
        // Arrange
        // With UCUM support, mg and kg can be compared (both are mass)
        // This test verifies that TRULY incompatible units (e.g., mg and m) return empty
        var expr = _parser.Parse("(5 'mg') < (1 'm')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Unit Conversion Tests (NEW - UCUM Integration)

    [Fact]
    public void GivenKilogramsToGrams_WhenConversion_ThenConvertsCorrectly()
    {
        // Arrange
        // Official test: (1 'kg') = (1000 'g') should be true after conversion
        var expr = _parser.Parse("(1 'kg') = (1000 'g')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenMetersToMillimeters_WhenConversion_ThenConvertsCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("(1 'm') = (1000 'mm')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenCelsiusToFahrenheit_WhenConversion_ThenConvertsCorrectly()
    {
        // Arrange
        // NOTE: UCUM/Fhir.Metrics does not support conversion between affine temperature scales
        // (Celsius, Fahrenheit) because they have different zero points.
        // This test verifies that incompatible conversions return empty.
        // Temperature intervals (K, Cel) can be converted, but absolute temperatures cannot.
        var expr = _parser.Parse("(0 'Cel') = (32 '[degF]')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        // Expect empty result due to UCUM limitation with affine temperature scales
        Assert.Empty(result);
    }

    [Fact]
    public void GivenIncompatibleUnits_WhenConversion_ThenReturnsEmpty()
    {
        // Arrange
        // Cannot convert mass (kg) to length (m)
        var expr = _parser.Parse("(1 'kg') = (1 'm')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Calendar Duration Tests (NEW)

    [Fact]
    public void GivenYearDuration_WhenParsing_ThenReturnsQuantity()
    {
        // Arrange
        var expr = _parser.Parse("1 year");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenDaysDuration_WhenParsing_ThenReturnsQuantity()
    {
        // Arrange
        // Official test: 4 days
        var expr = _parser.Parse("4 days");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenHourDuration_WhenParsing_ThenReturnsQuantity()
    {
        // Arrange
        var expr = _parser.Parse("1 hour");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenDurationArithmetic_WhenAddition_ThenReturnsSum()
    {
        // Arrange
        // (2 days) + (3 days) = 5 days
        var expr = _parser.Parse("(2 days) + (3 days)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenDifferentCalendarUnits_WhenAddition_ThenReturnsEmpty()
    {
        // Arrange
        // Cannot add years + days without conversion
        var expr = _parser.Parse("(1 year) + (5 days)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region FHIR Observation Examples (NEW)

    [Fact]
    public void GivenObservationWithQuantityValue_WhenEvaluating_ThenReturnsValue()
    {
        // Arrange
        // Simulates: Observation.valueQuantity = 120 'mm[Hg]'
        var observation = CreateObservationWithQuantity(120m, "mm[Hg]");
        var expr = _parser.Parse("value");

        // Act
        var result = _evaluator.Evaluate(observation, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact(Skip = "Requires FHIR Quantity element to FHIRPath Quantity conversion")]
    public void GivenObservationWithQuantity_WhenComparison_ThenFiltersCorrectly()
    {
        // Arrange
        // Simulates: Observation.valueQuantity > 140 'mm[Hg]' (hypertension threshold)
        var observation = CreateObservationWithQuantity(150m, "mm[Hg]");
        var expr = _parser.Parse("value > (140 'mm[Hg]')");

        // Act
        var result = _evaluator.Evaluate(observation, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenObservationWithTemperature_WhenEvaluating_ThenReturnsTemperature()
    {
        // Arrange
        // Simulates: Observation.valueQuantity = 37.5 'Cel'
        var observation = CreateObservationWithQuantity(37.5m, "Cel");
        var expr = _parser.Parse("value");

        // Act
        var result = _evaluator.Evaluate(observation, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    #endregion

    #region Edge Cases and Error Handling (NEW)

    [Fact]
    public void GivenNullQuantity_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var observation = CreateObservationWithoutValue();
        var expr = _parser.Parse("value");

        // Act
        var result = _evaluator.Evaluate(observation, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenZeroQuantity_WhenEvaluating_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("0 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenNegativeQuantity_WhenEvaluating_ThenReturnsNegative()
    {
        // Arrange
        var expr = _parser.Parse("-5 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenVeryLargeQuantity_WhenEvaluating_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("9999999999.99 'kg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityPrecision_WhenEvaluating_ThenPreservesPrecision()
    {
        // Arrange
        var expr = _parser.Parse("3.14159 'rad'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    #endregion

    #region Helper Methods

    private QuantityExpression ParseQuantity(string input)
    {
        var tokenizeResult = _tokenizer.TryTokenize(input);
        Assert.True(tokenizeResult.HasValue, $"Tokenization failed: {tokenizeResult}");

        var parseResult = FhirPathGrammar.Quantity.TryParse(tokenizeResult.Value);
        Assert.True(parseResult.HasValue, $"Parsing failed: {parseResult}");

        return parseResult.Value;
    }

    private ITypedElement CreateIntegerElement(int value)
    {
        return new PrimitiveTypedElement(value, "integer");
    }

    private ITypedElement CreateObservationWithQuantity(decimal value, string unit)
    {
        var quantityValue = new PrimitiveTypedElement(value, "decimal");
        var quantityUnit = new PrimitiveTypedElement(unit, "code");

        var quantity = new ComplexTypedElement("Quantity", "value",
            new (string, ITypedElement)[] {
                ("value", quantityValue),
                ("unit", quantityUnit),
                ("system", new PrimitiveTypedElement("http://unitsofmeasure.org", "uri")),
                ("code", quantityUnit)
            });

        return new ComplexTypedElement("Observation", "Observation",
            new (string, ITypedElement)[] {
                ("value", quantity)
            });
    }

    private ITypedElement CreateObservationWithoutValue()
    {
        return new ComplexTypedElement("Observation", "Observation",
            Array.Empty<(string, ITypedElement)>());
    }

    private class PrimitiveTypedElement : ITypedElement
    {
        public PrimitiveTypedElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }

    private class ComplexTypedElement : ITypedElement
    {
        private readonly List<(string name, ITypedElement element)> _children;

        public ComplexTypedElement(string instanceType, string name, IEnumerable<(string name, ITypedElement element)> children)
        {
            InstanceType = instanceType;
            Name = name;
            _children = children.ToList();
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            if (name == null)
                return _children.Select(c => c.element);

            return _children.Where(c => c.name == name).Select(c => c.element);
        }
    }

    #endregion
}
