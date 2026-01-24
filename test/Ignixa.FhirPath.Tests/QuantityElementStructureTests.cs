/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for QuantityElement IElement structure.
 * Ensures that Quantity elements expose proper FHIR structure via Children()
 * with named child elements for interoperability with serialization layers.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests;

public class QuantityElementStructureTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// Simple IElement for testing purposes (acts as evaluation root).
    /// </summary>
    private class TestElement : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "Base";
        public object Value => 0;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => default;
    }

    [Fact]
    public void QuantityElement_ShouldHaveNamedChildren()
    {
        // Arrange: Parse and evaluate a quantity expression
        var expression = _parser.Parse("100 'mg'");
        var testElement = new TestElement();
        var result = _evaluator.Evaluate(testElement, expression).ToList();

        // Assert: Should return a single quantity element
        Assert.Single(result);
        var quantityElement = result[0];
        Assert.Equal("Quantity", quantityElement.InstanceType);

        // Act: Get all children
        var children = quantityElement.Children().ToList();

        // Assert: Should have named children for value, unit/code, and system
        var valueChild = children.FirstOrDefault(c => c.Name == "value");
        Assert.NotNull(valueChild);
        Assert.Equal("decimal", valueChild.InstanceType);
        Assert.Equal(100m, valueChild.Value);

        var unitChild = children.FirstOrDefault(c => c.Name == "unit");
        Assert.NotNull(unitChild);
        Assert.Equal("string", unitChild.InstanceType);
        Assert.Equal("mg", unitChild.Value);

        var codeChild = children.FirstOrDefault(c => c.Name == "code");
        Assert.NotNull(codeChild);
        Assert.Equal("string", codeChild.InstanceType);
        Assert.Equal("mg", codeChild.Value);

        var systemChild = children.FirstOrDefault(c => c.Name == "system");
        Assert.NotNull(systemChild);
        Assert.Equal("uri", systemChild.InstanceType);
        Assert.Equal("http://unitsofmeasure.org", systemChild.Value);
    }

    [Fact]
    public void QuantityElement_ChildrenWithNameFilter_ShouldReturnMatchingChildren()
    {
        // Arrange
        var expression = _parser.Parse("5.5 'cm'");
        var testElement = new TestElement();
        var result = _evaluator.Evaluate(testElement, expression).ToList();
        var quantityElement = result[0];

        // Act & Assert: Filter by "value"
        var valueChildren = quantityElement.Children("value").ToList();
        Assert.Single(valueChildren);
        Assert.Equal("value", valueChildren[0].Name);
        Assert.Equal(5.5m, valueChildren[0].Value);

        // Act & Assert: Filter by "unit" - should return only "unit" child
        var unitChildren = quantityElement.Children("unit").ToList();
        Assert.Single(unitChildren);
        Assert.Equal("unit", unitChildren[0].Name);
        Assert.Equal("cm", unitChildren[0].Value);

        // Act & Assert: Filter by "code" - should return only "code" child
        var codeChildren = quantityElement.Children("code").ToList();
        Assert.Single(codeChildren);
        Assert.Equal("code", codeChildren[0].Name);
        Assert.Equal("cm", codeChildren[0].Value);

        // Act & Assert: Filter by "system"
        var systemChildren = quantityElement.Children("system").ToList();
        Assert.Single(systemChildren);
        Assert.Equal("system", systemChildren[0].Name);
        Assert.Equal("http://unitsofmeasure.org", systemChildren[0].Value);
    }

    [Fact]
    public void PrimitiveElement_ShouldPreserveName()
    {
        // This test ensures that PrimitiveElement constructor accepts and preserves names
        // Regression test for: PrimitiveElement(object value, string type, string name)

        var expression = _parser.Parse("'test'");
        var testElement = new TestElement();
        var result = _evaluator.Evaluate(testElement, expression).ToList();
        var element = result[0];

        // PrimitiveElement at root level has empty name
        Assert.Equal(string.Empty, element.Name);

        // But when used as a child (like in Quantity), it should have a name
        var quantityExpr = _parser.Parse("10 'kg'");
        var quantityResult = _evaluator.Evaluate(testElement, quantityExpr).ToList();
        var quantity = quantityResult[0];

        var valueChildren = quantity.Children("value");
        Assert.NotEmpty(valueChildren);
        Assert.Equal("value", valueChildren[0].Name); // Should preserve the name passed to constructor
    }
}
