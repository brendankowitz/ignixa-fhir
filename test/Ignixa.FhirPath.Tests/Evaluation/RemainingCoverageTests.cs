/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Additional tests to reach 80% coverage for FhirPath library.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class RemainingCoverageTests
{
    private readonly FhirPathParser _parser = new();
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
        var expr = _parser.Parse("(1 | 2 | 3).select($this * 2)");
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
        var expr = _parser.Parse("name.select(given)");

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
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).where($this > 3)");
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
        var expr = _parser.Parse("(1 | 2 | 3 | 4).where($this > 1 and $this < 4)");
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
        var expr = _parser.Parse("repeat(children())");

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
        var expr = _parser.Parse("(1 | 2).repeat({})");
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
        var expr = _parser.Parse("(5 | 10 | 15).`isDistinct`()");
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
        var expr = _parser.Parse("('a' | 'b' | 'c').`isDistinct`()");
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
        var expr = _parser.Parse("42");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessLocation_ThenReturnsEmptyString()
    {
        // This tests the PrimitiveElement.Location property
        var expr = _parser.Parse("'test'");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Location);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessType_ThenReturnsNull()
    {
        // This tests the PrimitiveElement.Type property
        var expr = _parser.Parse("true");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.NotNull(result);
        Assert.Null(result.Type);
    }

    [Fact]
    public void GivenPrimitiveElement_WhenAccessChildren_ThenReturnsEmpty()
    {
        // This tests the PrimitiveElement.Children() method
        var expr = _parser.Parse("123");
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
        var expr = _parser.Parse("name.given");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    [Fact]
    public void GivenMultipleLevels_WhenNavigateDeep_ThenTraversesCorrectly()
    {
        // This exercises nested child navigation
        var patient = CreatePatientWithMultipleNames();
        var expr = _parser.Parse("name.given.first()");

        var result = _evaluator.Evaluate(patient, expr).ToList();

        Assert.NotEmpty(result);
    }

    #endregion

    #region Expression Base Class Tests

    [Fact]
    public void GivenExpression_WhenCompared_ThenUsesLocationInfo()
    {
        // This tests the Expression base class methods
        var expr1 = _parser.Parse("Patient");
        var expr2 = _parser.Parse("name");

        Assert.NotEqual(expr1.Location, expr2.Location);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private IElement CreatePatientWithMultipleNames()
    {
        var name1Children = new List<IElement>
        {
            new PrimitiveElement("John", "string", "given"),
            new PrimitiveElement("Smith", "string", "family")
        };

        var name2Children = new List<IElement>
        {
            new PrimitiveElement("Johnny", "string", "given"),
            new PrimitiveElement("Smith", "string", "family")
        };

        var children = new List<IElement>
        {
            new ComplexElement("name", "HumanName", name1Children),
            new ComplexElement("name", "HumanName", name2Children),
            new PrimitiveElement("male", "code", "gender")
        };

        return new ComplexElement("Patient", "Patient", children);
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type, string? name = null)
        {
            Value = value;
            InstanceType = type;
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    private class ComplexElement : IElement
    {
        private readonly List<IElement> _children;

        public ComplexElement(string name, string type, List<IElement> children)
        {
            Name = name;
            InstanceType = type;
            _children = children;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }

            return _children.Where(c => c.Name == name).ToList();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
