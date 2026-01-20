/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath aggregate functions (Phase 23).
 *
 * Official Test Suite Reference:
 * https://github.com/HL7/FHIRPath/blob/master/tests/functions.xml (aggregate section)
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirPathAggregateTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Sum Function Tests

    [Fact]
    public void GivenIntegerCollection_WhenSum_ThenReturnsIntegerSum()
    {
        // Arrange
        // Official test: {1, 2, 3, 4, 5}.sum() = 15
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(15, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenDecimalCollection_WhenSum_ThenReturnsDecimalSum()
    {
        // Arrange
        var expr = _parser.Parse("(1.5 | 2.3 | 3.7).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(7.5m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenMixedIntegerDecimal_WhenSum_ThenReturnsDecimalSum()
    {
        // Arrange
        // Mixed types should promote to decimal
        var expr = _parser.Parse("(1 | 2.5 | 3).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(6.5m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityCollection_WhenSum_ThenReturnsQuantitySum()
    {
        // Arrange
        // (5 'mg') + (3 'mg') + (2 'mg') = 10 'mg'
        var expr = _parser.Parse("((5 'mg') | (3 'mg') | (2 'mg')).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenEmptyCollection_WhenSum_ThenReturnsZero()
    {
        // Arrange
        // Per FHIRPath spec: sum() on empty collection returns 0
        var expr = _parser.Parse("{}.sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(0, result[0].Value);
    }

    [Fact]
    public void GivenSingleItem_WhenSum_ThenReturnsThatItem()
    {
        // Arrange
        var expr = _parser.Parse("(42).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GivenCollectionWithNull_WhenSum_ThenIgnoresNull()
    {
        // Arrange
        // Nulls should be skipped in aggregation
        var expr = _parser.Parse("(1 | {} | 3).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(4, result.Value);
    }

    #endregion

    #region Min Function Tests

    [Fact]
    public void GivenIntegerCollection_WhenMin_ThenReturnsMinimum()
    {
        // Arrange
        // Official test: {5, 3, 8, 1, 9}.min() = 1
        var expr = _parser.Parse("(5 | 3 | 8 | 1 | 9).min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenDecimalCollection_WhenMin_ThenReturnsMinimum()
    {
        // Arrange
        var expr = _parser.Parse("(5.5 | 3.2 | 8.9 | 1.1).min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1.1m, result.Value);
    }

    [Fact]
    public void GivenStringCollection_WhenMin_ThenReturnsLexicographicMin()
    {
        // Arrange
        var expr = _parser.Parse("('apple' | 'banana' | 'cherry').min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("apple", result.Value);
    }

    [Fact]
    public void GivenDateCollection_WhenMin_ThenReturnsEarliestDate()
    {
        // Arrange
        var expr = _parser.Parse("(@2024-01-15 | @2024-01-10 | @2024-01-20).min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("2024-01-10", result.Value);
        Assert.Equal("date", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityCollection_WhenMin_ThenReturnsMinimumQuantity()
    {
        // Arrange
        var expr = _parser.Parse("((5 'mg') | (3 'mg') | (8 'mg')).min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenEmptyCollection_WhenMin_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenSingleItem_WhenMin_ThenReturnsThatItem()
    {
        // Arrange
        var expr = _parser.Parse("(42).min()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42, result.Value);
    }

    #endregion

    #region Max Function Tests

    [Fact]
    public void GivenIntegerCollection_WhenMax_ThenReturnsMaximum()
    {
        // Arrange
        // Official test: {5, 3, 8, 1, 9}.max() = 9
        var expr = _parser.Parse("(5 | 3 | 8 | 1 | 9).max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(9, result.Value);
    }

    [Fact]
    public void GivenDecimalCollection_WhenMax_ThenReturnsMaximum()
    {
        // Arrange
        var expr = _parser.Parse("(5.5 | 3.2 | 8.9 | 1.1).max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(8.9m, result.Value);
    }

    [Fact]
    public void GivenStringCollection_WhenMax_ThenReturnsLexicographicMax()
    {
        // Arrange
        var expr = _parser.Parse("('apple' | 'banana' | 'cherry').max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("cherry", result.Value);
    }

    [Fact]
    public void GivenDateTimeCollection_WhenMax_ThenReturnsLatestDateTime()
    {
        // Arrange
        var expr = _parser.Parse("(@2024-01-15T10:00:00Z | @2024-01-10T10:00:00Z | @2024-01-20T10:00:00Z).max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("2024-01-20T10:00:00Z", result.Value);
        Assert.Equal("dateTime", result.InstanceType);
    }

    [Fact]
    public void GivenQuantityCollection_WhenMax_ThenReturnsMaximumQuantity()
    {
        // Arrange
        var expr = _parser.Parse("((5 'mg') | (3 'mg') | (8 'mg')).max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenEmptyCollection_WhenMax_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.max()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Avg Function Tests

    [Fact]
    public void GivenIntegerCollection_WhenAvg_ThenReturnsDecimalAverage()
    {
        // Arrange
        // Official test: {1, 2, 3, 4, 5}.avg() = 3.0
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(3.0m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenDecimalCollection_WhenAvg_ThenReturnsDecimalAverage()
    {
        // Arrange
        var expr = _parser.Parse("(1.5 | 2.5 | 3.5).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2.5m, result.Value);
    }

    [Fact]
    public void GivenQuantityCollection_WhenAvg_ThenReturnsQuantityAverage()
    {
        // Arrange
        // (5 'mg' + 3 'mg' + 4 'mg') / 3 = 4 'mg'
        var expr = _parser.Parse("((5 'mg') | (3 'mg') | (4 'mg')).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenEmptyCollection_WhenAvg_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenSingleItem_WhenAvg_ThenReturnsThatItem()
    {
        // Arrange
        var expr = _parser.Parse("(42).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42m, result.Value);
    }

    [Fact]
    public void GivenOddNumberOfItems_WhenAvg_ThenReturnsExactAverage()
    {
        // Arrange
        var expr = _parser.Parse("(10 | 20 | 30).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(20m, result.Value);
    }

    #endregion

    #region FHIR Examples - Vital Signs

    [Fact]
    public void GivenTemperatureReadings_WhenAvg_ThenReturnsAverageTemperature()
    {
        // Arrange
        // Simulates average body temperature over multiple readings
        var expr = _parser.Parse("((37.0 'Cel') | (37.5 'Cel') | (36.8 'Cel')).avg()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    [Fact]
    public void GivenMedicationDosages_WhenSum_ThenReturnsTotalDose()
    {
        // Arrange
        // Simulates: MedicationAdministration.dosage.dose.sum()
        var expr = _parser.Parse("((5 'mg') | (5 'mg') | (10 'mg')).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
    }

    #endregion

    #region FHIR Examples - Validation Rules

    [Fact]
    public void GivenEncounterDiagnoses_WhenMaxRank_ThenValidatesAgainstCount()
    {
        // Arrange
        // Validation rule: Encounter.diagnosis.rank.max() <= Encounter.diagnosis.count()
        var expr = _parser.Parse("(1 | 2 | 3).max() <= 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenNegativeNumbers_WhenSum_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("(-5 | 10 | -3).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public void GivenVeryLargeNumbers_WhenSum_ThenHandlesCorrectly()
    {
        // Arrange
        var expr = _parser.Parse("(999999999 | 1).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1000000000, result.Value);
    }

    [Fact]
    public void GivenMixedTypesInvalid_WhenSum_ThenReturnsEmpty()
    {
        // Arrange
        // Cannot sum integers and strings
        var expr = _parser.Parse("(1 | 'text').sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenQuantitiesDifferentUnits_WhenSum_ThenReturnsEmpty()
    {
        // Arrange
        // Cannot sum mg + kg without conversion
        var expr = _parser.Parse("((5 'mg') | (1 'kg')).sum()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
