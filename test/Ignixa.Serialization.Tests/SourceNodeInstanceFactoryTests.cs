/*
 * Tests for SourceNodeInstanceFactory - the native source-node-backed
 * instance-creation delegate used for FHIRPath instance-selector object creation.
 */

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class SourceNodeInstanceFactoryTests
{
    private readonly R4CoreSchemaProvider _schema = new();
    private readonly SourceNodeInstanceFactory _factory;

    public SourceNodeInstanceFactoryTests()
    {
        _factory = new SourceNodeInstanceFactory(_schema);
    }

    [Fact]
    public void GivenKnownTypeWithPrimitives_WhenCreate_ThenReturnsNavigableTypedElement()
    {
        // Arrange
        var elements = new[]
        {
            new InstanceElement("system", [Prim("http://example.org", "string")]),
            new InstanceElement("code", [Prim("c1", "string")]),
        };

        // Act
        var result = Create("Coding", null, elements);

        // Assert - first-class node: correct type, schema metadata, navigable
        Assert.NotNull(result);
        Assert.Equal("Coding", result!.InstanceType);
        Assert.NotNull(result.Type);
        Assert.Equal("Coding", result.Type!.Info.Name);
        Assert.Equal("http://example.org", result.Children("system").Single().Value);
        Assert.Equal("c1", result.Children("code").Single().Value);
    }

    [Fact]
    public void GivenCreatedInstance_WhenInspectingBackingJson_ThenRoundTrips()
    {
        // Arrange
        var elements = new[]
        {
            new InstanceElement("system", [Prim("http://example.org", "string")]),
            new InstanceElement("code", [Prim("c1", "string")]),
        };

        // Act
        var result = Create("Coding", null, elements);
        var json = result!.Meta<JsonNode>();

        // Assert - the created node is backed by real JSON (round-trippable)
        Assert.NotNull(json);
        Assert.Equal("http://example.org", json!["system"]!.GetValue<string>());
        Assert.Equal("c1", json["code"]!.GetValue<string>());
    }

    [Fact]
    public void GivenEmptyElements_WhenCreate_ThenReturnsEmptyTypedObject()
    {
        // Act
        var result = Create("Period", null, []);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Period", result!.InstanceType);
        Assert.Empty(result.Children());
    }

    [Fact]
    public void GivenPrimitiveTypeWithValueElement_WhenCreate_ThenReturnsPrimitiveNode()
    {
        // Arrange - per spec, primitive target types carry their value via "value"
        var elements = new[] { new InstanceElement("value", [Prim("final", "string")]) };

        // Act
        var result = Create("code", null, elements);

        // Assert - a primitive node, not a complex object with a "value" child
        Assert.NotNull(result);
        Assert.Equal("code", result!.InstanceType);
        Assert.True(result.HasPrimitiveValue);
        Assert.Equal("final", result.Value);
        Assert.Empty(result.Children());
    }

    [Fact]
    public void GivenUnknownType_WhenCreate_ThenReturnsNull()
    {
        var result = Create("CompletelyMadeUpType", null, []);

        Assert.Null(result);
    }

    [Fact]
    public void GivenSystemNamespace_WhenCreate_ThenReturnsNull()
    {
        var result = Create("String", "System", [new InstanceElement("value", [Prim("x", "string")])]);

        Assert.Null(result);
    }

    [Fact]
    public void GivenResourceType_WhenCreate_ThenBackingJsonCarriesResourceType()
    {
        // Arrange - without resourceType the backing JSON cannot be re-read by a FHIR parser
        var elements = new[] { new InstanceElement("id", [Prim("p1", "string")]) };

        // Act
        var result = Create("Patient", null, elements);
        var json = result!.Meta<JsonNode>();

        // Assert
        Assert.NotNull(json);
        Assert.Equal("Patient", json!["resourceType"]!.GetValue<string>());
        Assert.Equal("p1", json["id"]!.GetValue<string>());
    }

    [Fact]
    public void GivenNonResourceType_WhenCreate_ThenBackingJsonOmitsResourceType()
    {
        var result = Create("Coding", null, [new InstanceElement("code", [Prim("c1", "string")])]);

        var json = result!.Meta<JsonNode>() as JsonObject;
        Assert.NotNull(json);
        Assert.False(json!.ContainsKey("resourceType"));
    }

    [Fact]
    public void GivenChoiceElementBaseNameWithComplexValue_WhenCreate_ThenEmitsTypeSuffixedProperty()
    {
        // Arrange - "value" on Observation is value[x]; the assigned Quantity picks the suffix
        var quantity = Create("Quantity", null, [new InstanceElement("value", [Prim(70, "decimal")])]);
        var elements = new[] { new InstanceElement("value", [quantity!]) };

        // Act
        var result = Create("Observation", null, elements);
        var json = result!.Meta<JsonNode>() as JsonObject;

        // Assert - canonical FHIR names the property valueQuantity, not value
        Assert.NotNull(json);
        Assert.True(json!.ContainsKey("valueQuantity"));
        Assert.False(json.ContainsKey("value"));

        // ...and FHIRPath navigation by the base name still resolves it
        Assert.Single(result.Children("value"));
    }

    [Fact]
    public void GivenChoiceElementBaseNameWithPrimitiveValue_WhenCreate_ThenEmitsTypeSuffixedProperty()
    {
        var result = Create("Observation", null, [new InstanceElement("value", [Prim("high", "string")])]);

        var json = result!.Meta<JsonNode>() as JsonObject;
        Assert.NotNull(json);
        Assert.Equal("high", json!["valueString"]!.GetValue<string>());
    }

    [Fact]
    public void GivenAlreadySuffixedChoiceName_WhenCreate_ThenLeavesNameUnchanged()
    {
        // Arrange - an author who writes the suffix must not get it applied twice
        var result = Create("Observation", null, [new InstanceElement("valueString", [Prim("high", "string")])]);

        var json = result!.Meta<JsonNode>() as JsonObject;
        Assert.NotNull(json);
        Assert.True(json!.ContainsKey("valueString"));
        Assert.False(json.ContainsKey("valueStringString"));
    }

    [Fact]
    public void GivenChoiceTypeNotDeclaredForElement_WhenCreate_ThenLeavesBaseName()
    {
        // Arrange - Coding is not one of Observation.value[x]'s types; guessing a suffix would
        // invent a property name, so the base name is kept and validation can flag it later.
        var coding = Create("Coding", null, [new InstanceElement("code", [Prim("c1", "string")])]);

        var result = Create("Observation", null, [new InstanceElement("value", [coding!])]);

        var json = result!.Meta<JsonNode>() as JsonObject;
        Assert.NotNull(json);
        Assert.True(json!.ContainsKey("value"));
    }

    [Fact]
    public void GivenDuplicateAssignmentsToRepeatingElement_WhenCreate_ThenAggregatesBothValues()
    {
        // Arrange - two assignments to the same repeating element previously overwrote each other
        var elements = new[]
        {
            new InstanceElement("given", [Prim("John", "string")]),
            new InstanceElement("given", [Prim("Jacob", "string")]),
        };

        // Act
        var result = Create("HumanName", null, elements);

        // Assert
        Assert.NotNull(result);
        var given = result!.Children("given").Select(c => c.Value).ToArray();
        Assert.Equal(new object?[] { "John", "Jacob" }, given);
    }

    [Fact]
    public void GivenDuplicateAssignmentsToSingletonElement_WhenCreate_ThenThrows()
    {
        // Arrange - HumanName.family is 0..1, so two values cannot be represented
        var elements = new[]
        {
            new InstanceElement("family", [Prim("Smith", "string")]),
            new InstanceElement("family", [Prim("Jones", "string")]),
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => Create("HumanName", null, elements));
        Assert.Contains("family", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenUnknownElementName_WhenCreate_ThenEmittedVerbatim()
    {
        // Arrange - this factory constructs, it does not validate
        var result = Create("Coding", null, [new InstanceElement("madeUpField", [Prim("x", "string")])]);

        var json = result!.Meta<JsonNode>() as JsonObject;
        Assert.NotNull(json);
        Assert.Equal("x", json!["madeUpField"]!.GetValue<string>());
    }

    private IElement? Create(string typeName, string? namespacePrefix, IReadOnlyList<InstanceElement> elements) =>
        _factory.Create(new InstanceCreationRequest(typeName, namespacePrefix, elements));

    private static IElement Prim(object value, string type) => new PrimitiveValueElement(value, type);

    private sealed class PrimitiveValueElement : IElement
    {
        public PrimitiveValueElement(object value, string instanceType)
        {
            Value = value;
            InstanceType = instanceType;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
