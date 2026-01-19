// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Tests for InferredType property population on expressions (GitHub issue #196).
/// </summary>
public class InferredTypePopulationTests
{
    private readonly IFhirSchemaProvider _schema;
    private readonly FhirPathAnalyzer _analyzer;
    private readonly FhirPathParser _parser;

    public InferredTypePopulationTests()
    {
        _schema = FhirVersion.R4.GetSchemaProvider();
        _analyzer = new FhirPathAnalyzer(_schema);
        _parser = new FhirPathParser();
    }

    [Fact]
    public void GivenSimplePropertyAccess_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("HumanName", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenNestedPropertyAccess_WhenAnalyzingWithTypes_ThenTopLevelHasInferredType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.family");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert - The top-level expression should have inferred type
        Assert.NotNull(expression.InferredType);
        Assert.Contains("string", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFunctionCall_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.first()");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("HumanName", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenCollectionPropertyAccess_WhenAnalyzingWithTypes_ThenInferredTypeIncludesArrayNotation()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        // name is a collection, so it should be HumanName[]
        Assert.Contains("[]", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFirstFunction_WhenAnalyzingWithTypes_ThenReturnsHumanNameType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.first()");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("HumanName", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAnalyzeWithoutPopulateFlag_WhenCalled_ThenInferredTypeIsNotSet()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name");

        // Act - analyze WITHOUT populateInferredTypes
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: false);

        // Assert - InferredType should remain null
        Assert.Null(expression.InferredType);
    }

    [Fact]
    public void GivenConstantExpression_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("'test'");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("string", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenBinaryExpression_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("1 + 2");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("integer", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenWhereFunction_WhenAnalyzingWithTypes_ThenPreservesCollectionType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.where(use = 'official')");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("HumanName", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenSelectFunction_WhenAnalyzingWithTypes_ThenInfersProjectedType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.select(family)");

        // Act
        _analyzer.Analyze(expression, "Patient", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        Assert.Contains("string", expression.InferredType, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenChoiceType_WhenAnalyzingWithTypes_ThenIncludesMultipleTypes()
    {
        // Arrange - Observation.value is a choice type
        var expression = _parser.Parse("Observation.value");

        // Act
        _analyzer.Analyze(expression, "Observation", populateInferredTypes: true);

        // Assert
        Assert.NotNull(expression.InferredType);
        // Choice types should have multiple types
        // The exact format depends on implementation but should have multiple type names
        Assert.True(
            expression.InferredType.Contains(',', StringComparison.Ordinal) ||
            expression.InferredType.Contains('|', StringComparison.Ordinal) ||
            expression.InferredType.Length > 20, // Multiple types would make a longer string
            $"Expected multiple types for choice type, got: {expression.InferredType}");
    }
}
