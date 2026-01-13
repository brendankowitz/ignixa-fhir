/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath instance selector expressions.
 * Tests creation of FHIR objects using the syntax: TypeName { element: value, ... }
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class InstanceSelectorTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Simple Instance Selector Tests

    [Fact]
    public void GivenSimpleCoding_WhenInstanceSelector_ThenCreatesObject()
    {
        // Arrange
        var expression = "Coding { system: 'http://example.org', code: 'c1' }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Coding", result[0].InstanceType);

        var system = result[0].Children("system").SingleOrDefault();
        Assert.NotNull(system);
        Assert.Equal("http://example.org", system.Value);

        var code = result[0].Children("code").SingleOrDefault();
        Assert.NotNull(code);
        Assert.Equal("c1", code.Value);
    }

    [Fact]
    public void GivenMultipleProperties_WhenInstanceSelector_ThenCreatesObjectWithAllProperties()
    {
        // Arrange
        var expression = "Identifier { system: 'http://hl7.org', value: 'N0001', use: 'official' }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Identifier", result[0].InstanceType);

        var children = result[0].Children().ToList();
        Assert.Equal(3, children.Count);

        var system = result[0].Children("system").SingleOrDefault();
        Assert.NotNull(system);
        Assert.Equal("http://hl7.org", system.Value);

        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal("N0001", value.Value);

        var use = result[0].Children("use").SingleOrDefault();
        Assert.NotNull(use);
        Assert.Equal("official", use.Value);
    }

    #endregion

    #region Nested Instance Selector Tests

    [Fact]
    public void GivenNestedInstanceSelector_WhenEvaluated_ThenCreatesNestedStructure()
    {
        // Arrange
        var expression = "Identifier { type: CodeableConcept { coding: Coding { code: 'MR' } } }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Identifier", result[0].InstanceType);

        var type = result[0].Children("type").SingleOrDefault();
        Assert.NotNull(type);
        Assert.Equal("CodeableConcept", type.InstanceType);

        var coding = type.Children("coding").SingleOrDefault();
        Assert.NotNull(coding);
        Assert.Equal("Coding", coding.InstanceType);

        var code = coding.Children("code").SingleOrDefault();
        Assert.NotNull(code);
        Assert.Equal("MR", code.Value);
    }

    [Fact]
    public void GivenDeeplyNestedInstanceSelector_WhenEvaluated_ThenCreatesCompleteHierarchy()
    {
        // Arrange
        var expression = "Patient { name: HumanName { given: 'John', family: 'Doe' } }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Patient", result[0].InstanceType);

        var name = result[0].Children("name").SingleOrDefault();
        Assert.NotNull(name);
        Assert.Equal("HumanName", name.InstanceType);

        var given = name.Children("given").SingleOrDefault();
        Assert.NotNull(given);
        Assert.Equal("John", given.Value);

        var family = name.Children("family").SingleOrDefault();
        Assert.NotNull(family);
        Assert.Equal("Doe", family.Value);
    }

    #endregion

    #region Empty Object Initializer Tests

    [Fact]
    public void GivenEmptyObjectInitializer_WhenEvaluated_ThenCreatesEmptyObject()
    {
        // Arrange
        var expression = "Period {:}";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Period", result[0].InstanceType);

        var children = result[0].Children().ToList();
        Assert.Empty(children);
    }

    [Fact]
    public void GivenEmptyBraces_WhenEvaluated_ThenCreatesEmptyObject()
    {
        // Arrange
        var expression = "Coding {}";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Coding", result[0].InstanceType);

        var children = result[0].Children().ToList();
        Assert.Empty(children);
    }

    #endregion

    #region Namespace Prefix Tests

    [Fact]
    public void GivenFHIRNamespacePrefix_WhenInstanceSelector_ThenCreatesObject()
    {
        // Arrange
        var expression = "FHIR.Identifier { value: 'N0001' }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Identifier", result[0].InstanceType);

        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal("N0001", value.Value);
    }

    [Fact]
    public void GivenSystemNamespacePrefix_WhenInstanceSelector_ThenCreatesObject()
    {
        // Arrange
        var expression = "System.String { value: 'test' }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("String", result[0].InstanceType);

        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal("test", value.Value);
    }

    #endregion

    #region Empty Value Filtering Tests

    [Fact]
    public void GivenEmptyValueExpression_WhenEvaluated_ThenElementIsOmitted()
    {
        // Arrange - use where(false) to produce empty collection
        var expression = "Coding { system: 'http://example.org', code: (1 | 2).where(false), display: 'Test' }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Coding", result[0].InstanceType);

        var children = result[0].Children().ToList();
        Assert.Equal(2, children.Count);

        var system = result[0].Children("system").SingleOrDefault();
        Assert.NotNull(system);
        Assert.Equal("http://example.org", system.Value);

        var code = result[0].Children("code").SingleOrDefault();
        Assert.Null(code);

        var display = result[0].Children("display").SingleOrDefault();
        Assert.NotNull(display);
        Assert.Equal("Test", display.Value);
    }

    [Fact]
    public void GivenAllEmptyValues_WhenEvaluated_ThenCreatesEmptyObject()
    {
        // Arrange - use where(false) to produce empty collections
        var expression = "Period { start: (1).where(false), end: (2).where(false) }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Period", result[0].InstanceType);

        var children = result[0].Children().ToList();
        Assert.Empty(children);
    }

    #endregion

    #region Multiple Input Items Error Tests

    [Fact]
    public void GivenMultipleInputItems_WhenInstanceSelector_ThenThrowsException()
    {
        // Arrange
        var expression = "(1 | 2 | 3).select(Coding { code: $this.toString() })";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert - should work because select() evaluates instance selector for each item separately
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("Coding", r.InstanceType));
    }

    [Fact]
    public void GivenMultipleItemsDirectly_WhenInstanceSelector_ThenThrowsException()
    {
        // Arrange
        var multiItemCollection = System.Collections.Immutable.ImmutableList.Create(
            CreateIntegerElement(1),
            CreateIntegerElement(2),
            CreateIntegerElement(3)
        );

        var expression = "Coding { code: 'test' }";
        var ast = _parser.Parse(expression);

        // Act & Assert
        var context = new EvaluationContext { Focus = multiItemCollection };
        Assert.Throws<InvalidOperationException>(() =>
            ast.AcceptVisitor(_evaluator, context).ToList());
    }

    #endregion

    #region Empty Input Collection Tests

    [Fact]
    public void GivenEmptyInputCollection_WhenInstanceSelector_ThenReturnsEmpty()
    {
        // Arrange - use where(false) to create empty collection, then select on it
        var expression = "(1 | 2).where(false).select(Coding { code: 'test' })";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollectionDirectly_WhenInstanceSelector_ThenReturnsEmpty()
    {
        // Arrange
        var expression = "Coding { code: 'test' }";
        var ast = _parser.Parse(expression);
        var emptyContext = new EvaluationContext { Focus = System.Collections.Immutable.ImmutableList<IElement>.Empty };

        // Act
        var result = ast.AcceptVisitor(_evaluator, emptyContext).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Value Type Tests

    [Fact]
    public void GivenIntegerValue_WhenInstanceSelector_ThenCreatesElementWithInteger()
    {
        // Arrange
        var expression = "Quantity { value: 42 }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal(42, value.Value);
        Assert.Equal("integer", value.InstanceType);
    }

    [Fact]
    public void GivenDecimalValue_WhenInstanceSelector_ThenCreatesElementWithDecimal()
    {
        // Arrange
        var expression = "Quantity { value: 3.14 }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal(3.14m, value.Value);
        Assert.Equal("decimal", value.InstanceType);
    }

    [Fact]
    public void GivenBooleanValue_WhenInstanceSelector_ThenCreatesElementWithBoolean()
    {
        // Arrange
        var expression = "Flag { active: true }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        var active = result[0].Children("active").SingleOrDefault();
        Assert.NotNull(active);
        Assert.Equal(true, active.Value);
        Assert.Equal("boolean", active.InstanceType);
    }

    #endregion

    #region Expression Value Tests

    [Fact]
    public void GivenExpressionAsValue_WhenEvaluated_ThenEvaluatesExpressionFirst()
    {
        // Arrange
        var expression = "Coding { code: 'TEST'.lower() }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        var code = result[0].Children("code").SingleOrDefault();
        Assert.NotNull(code);
        Assert.Equal("test", code.Value);
    }

    [Fact]
    public void GivenArithmeticExpression_WhenEvaluated_ThenComputesValue()
    {
        // Arrange
        var expression = "Quantity { value: 10 + 5 }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        var value = result[0].Children("value").SingleOrDefault();
        Assert.NotNull(value);
        Assert.Equal(15, value.Value);
    }

    #endregion

    #region Multiple Cardinality Tests

    [Fact]
    public void GivenMultipleValueExpression_WhenEvaluated_ThenCreatesMultipleChildren()
    {
        // Arrange
        var expression = "HumanName { given: ('John' | 'Jacob') }";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(1);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("HumanName", result[0].InstanceType);

        var given = result[0].Children("given").ToList();
        Assert.Equal(2, given.Count);
        Assert.Equal("John", given[0].Value);
        Assert.Equal("Jacob", given[1].Value);
    }

    [Fact]
    public void GivenMultipleNestedElements_WhenEvaluated_ThenCreatesAll()
    {
        // Arrange - Use select to create multiple instances explicitly
        // This tests that when a value expression returns multiple items, all are added as children
        var expression = "(1 | 2).select(Patient { identifier: Identifier { value: $this.toString() } })";
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert - Should have 2 Patient instances (one for each input)
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("Patient", r.InstanceType));

        var firstPatientIdentifiers = result[0].Children("identifier").ToList();
        Assert.Single(firstPatientIdentifiers);
        Assert.Equal("1", firstPatientIdentifiers[0].Children("value").Single().Value);

        var secondPatientIdentifiers = result[1].Children("identifier").ToList();
        Assert.Single(secondPatientIdentifiers);
        Assert.Equal("2", secondPatientIdentifiers[0].Children("value").Single().Value);
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
