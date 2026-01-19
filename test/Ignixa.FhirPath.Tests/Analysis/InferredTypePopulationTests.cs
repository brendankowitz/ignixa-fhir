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
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("HumanName", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenNestedPropertyAccess_WhenAnalyzingWithTypes_ThenTopLevelHasInferredType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.family");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert - The top-level expression should have inferred type
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("string", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFunctionCall_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.first()");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("HumanName", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenCollectionPropertyAccess_WhenAnalyzingWithTypes_ThenInferredTypeIncludesArrayNotation()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        // name is a collection
        Assert.Contains(typeSet.Types, t => t.IsCollection);
    }

    [Fact]
    public void GivenFirstFunction_WhenAnalyzingWithTypes_ThenReturnsHumanNameType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.first()");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("HumanName", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAnalyzeWithoutPopulateFlag_WhenCalled_ThenNodeTypesIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert - NodeTypes should always be populated now
        Assert.NotNull(result.NodeTypes);
        Assert.NotEmpty(result.NodeTypes);
        Assert.Contains(expression, result.NodeTypes.Keys);
    }

    [Fact]
    public void GivenConstantExpression_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("'test'");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("string", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenBinaryExpression_WhenAnalyzingWithTypes_ThenInferredTypeIsPopulated()
    {
        // Arrange
        var expression = _parser.Parse("1 + 2");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("integer", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenWhereFunction_WhenAnalyzingWithTypes_ThenPreservesCollectionType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.where(use = 'official')");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("HumanName", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenSelectFunction_WhenAnalyzingWithTypes_ThenInfersProjectedType()
    {
        // Arrange
        var expression = _parser.Parse("Patient.name.select(family)");

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        var typeName = string.Join(", ", typeSet.Types.Select(t => t.TypeName));
        Assert.Contains("string", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenChoiceType_WhenAnalyzingWithTypes_ThenIncludesMultipleTypes()
    {
        // Arrange - Observation.value is a choice type
        var expression = _parser.Parse("Observation.value");

        // Act
        var result = _analyzer.Analyze(expression, "Observation");

        // Assert
        Assert.True(result.NodeTypes.TryGetValue(expression, out var typeSet));
        Assert.NotNull(typeSet);
        // Choice types should have multiple types
        Assert.True(typeSet.Types.Count > 1, $"Expected multiple types for choice type, got: {typeSet.Types.Count}");
    }
}
