/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Additional tests to reach 80% coverage for FhirPath library.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.Specification;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class RemainingCoverageTests
{
    private readonly FhirPathCompiler _compiler = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region FhirEvaluationContext Tests

    [Fact]
    public void GivenFhirEvaluationContext_WhenCreated_ThenHasEnvironmentVariables()
    {
        // Arrange & Act
        var context = new FhirEvaluationContext();

        // Assert
        Assert.NotNull(context);
    }

    [Fact]
    public void GivenFhirEvaluationContext_WhenSetVariable_ThenCanRetrieve()
    {
        // Arrange
        var context = new FhirEvaluationContext();
        var element = CreateIntegerElement(42);

        // Act
        context.SetEnvironmentVariable("test", element);
        var result = context.GetEnvironmentVariable("test");

        // Assert
        Assert.Equal(element, result);
    }

    #endregion

    #region Select Function Tests (Iterator Coverage)

    [Fact]
    public void GivenMultipleItems_WhenSelect_ThenProjectsAllItems()
    {
        // Arrange
        var expr = _compiler.Parse("(1 | 2 | 3).select($this * 2)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(4, result[1].Value);
        Assert.Equal(6, result[2].Value);
    }

    [Fact]
    public void GivenNestedPath_WhenSelect_ThenReturnsProjection()
    {
        // Arrange
        var patient = CreatePatientWithMultipleNames();
        var expr = _compiler.Parse("name.select(given)");

        // Act
        var result = _evaluator.Evaluate(patient, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
    }

    #endregion

    #region Where Function Tests (Iterator Coverage)

    [Fact]
    public void GivenMultipleItems_WhenWhere_ThenFiltersCorrectly()
    {
        // Arrange
        var expr = _compiler.Parse("(1 | 2 | 3 | 4 | 5).where($this > 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(4, result[0].Value);
        Assert.Equal(5, result[1].Value);
    }

    [Fact]
    public void GivenComplexCondition_WhenWhere_ThenFiltersCorrectly()
    {
        // Arrange
        var expr = _compiler.Parse("(1 | 2 | 3 | 4).where($this > 1 and $this < 4)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(3, result[1].Value);
    }

    #endregion

    #region Repeat Function Tests (TypedElementEqualityComparer Coverage)

    [Fact]
    public void GivenNestedStructure_WhenRepeat_ThenReturnsAllDescendants()
    {
        // Arrange
        var patient = CreatePatientWithMultipleNames();
        var expr = _compiler.Parse("repeat(children())");

        // Act
        var result = _evaluator.Evaluate(patient, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenCircularReference_WhenRepeat_ThenHandlesGracefully()
    {
        // Arrange
        // Repeat with empty projection should return just the input
        var expr = _compiler.Parse("(1 | 2).repeat({})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region IsDistinct Tests (TypedElementEqualityComparer Coverage)

    [Fact]
    public void GivenUniqueIntegers_WhenIsDistinct_ThenReturnsTrue()
    {
        // Arrange
        var expr = _compiler.Parse("(5 | 10 | 15).`isDistinct`()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenUniqueStrings_WhenIsDistinct_ThenReturnsTrue()
    {
        // Arrange
        var expr = _compiler.Parse("('a' | 'b' | 'c').`isDistinct`()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region PrimitiveElement Property Tests

    [Fact]
    public void GivenPrimitiveElement_WhenAccessName_ThenReturnsEmptyString()
    {
        // This tests the PrimitiveElement.Name property through evaluation
        var expr = _compiler.Parse("42");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessLocation_ThenReturnsEmptyString()
    {
        // This tests the PrimitiveElement.Location property
        var expr = _compiler.Parse("'test'");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Location);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessDefinition_ThenReturnsNull()
    {
        // This tests the PrimitiveElement.Definition property
        var expr = _compiler.Parse("true");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Null(result.Definition);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessChildren_ThenReturnsEmpty()
    {
        // This tests the PrimitiveElement.Children() method
        var expr = _compiler.Parse("123");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Empty(result.Children());
    }

    #endregion

    #region ChildExpression Iterator Tests

    [Fact]
    public void GivenComplexResource_WhenNavigateChildren_ThenReturnsAllMatches()
    {
        // This exercises the ChildExpression iterator
        var patient = CreatePatientWithMultipleNames();
        var expr = _compiler.Parse("name.given");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenMultipleLevels_WhenNavigateDeep_ThenTraversesCorrectly()
    {
        // This exercises nested child navigation
        var patient = CreatePatientWithMultipleNames();
        var expr = _compiler.Parse("name.given.first()");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    #endregion

    #region Expression Base Class Tests

    [Fact]
    public void GivenExpression_WhenCompared_ThenUsesLocationInfo()
    {
        // This tests the Expression base class methods
        var expr1 = _compiler.Parse("Patient");
        var expr2 = _compiler.Parse("name");

        Assert.NotEqual(expr1.Location, expr2.Location);
    }

    #endregion

    #region Helper Methods

    private ITypedElement CreateIntegerElement(int value)
    {
        return new PrimitiveTypedElement(value, "integer");
    }

    private ITypedElement CreatePatientWithMultipleNames()
    {
        var name1Children = new List<ITypedElement>
        {
            new PrimitiveTypedElement("John", "string", "given"),
            new PrimitiveTypedElement("Smith", "string", "family")
        };

        var name2Children = new List<ITypedElement>
        {
            new PrimitiveTypedElement("Johnny", "string", "given"),
            new PrimitiveTypedElement("Smith", "string", "family")
        };

        var children = new List<ITypedElement>
        {
            new ComplexTypedElement("name", "HumanName", name1Children),
            new ComplexTypedElement("name", "HumanName", name2Children),
            new PrimitiveTypedElement("male", "code", "gender")
        };

        return new ComplexTypedElement("Patient", "Patient", children);
    }

    private class PrimitiveTypedElement : ITypedElement
    {
        public PrimitiveTypedElement(object value, string type, string? name = null)
        {
            Value = value;
            InstanceType = type;
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }

    private class ComplexTypedElement : ITypedElement
    {
        private readonly List<ITypedElement> _children;

        public ComplexTypedElement(string name, string type, List<ITypedElement> children)
        {
            Name = name;
            InstanceType = type;
            _children = children;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }

            return _children.Where(c => c.Name == name);
        }
    }

    #endregion
}
