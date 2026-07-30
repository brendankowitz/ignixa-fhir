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

        // Assert - backing JSON carries the assigned properties (Meta<JsonNode> is non-null)
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

    [Fact]
    public void GivenComplexValueWrapper_WhenCreate_ThenRebuildsFromChildrenRatherThanSerializingTheClrObject()
    {
        // Arrange - mirrors FunctionHelpers.QuantityElement: exposes a CLR object on Value
        // but reports HasPrimitiveValue == false and carries real FHIR children.
        var quantity = new ComplexValueElement(
            new { Value = 70m, Unit = "kg" },
            "Quantity",
            [Prim(70m, "decimal", "value"), Prim("kg", "string", "unit")]);
        var elements = new[] { new InstanceElement("value", [quantity]) };

        // Act
        var result = Create("Observation", null, elements);
        var json = result!.Meta<JsonNode>();

        // Assert - the choice element resolves to valueQuantity and carries FHIR-cased
        // children, not the PascalCase shape a CLR serializer would emit.
        Assert.NotNull(json);
        var value = json!["valueQuantity"];
        Assert.NotNull(value);
        Assert.Equal(70m, value!["value"]!.GetValue<decimal>());
        Assert.Equal("kg", value["unit"]!.GetValue<string>());
        Assert.Null(value["Value"]);
        Assert.Null(value["Unit"]);
    }

    [Fact]
    public void GivenPrimitiveCarryingShadowExtensions_WhenCreate_ThenEmitsScalarValueNotTheShadowObject()
    {
        // Arrange - a FHIR primitive with a _value shadow. Meta<JsonNode>() returns the
        // shadow, so converting must consult the primitive value first.
        var shadow = new JsonObject
        {
            ["extension"] = new JsonArray(new JsonObject { ["url"] = "http://example.org/note" }),
        };
        var shadowed = new ShadowedPrimitiveElement("final", "code", shadow);
        var elements = new[] { new InstanceElement("status", [shadowed]) };

        // Act
        var result = Create("Observation", null, elements);
        var json = result!.Meta<JsonNode>();

        // Assert
        Assert.NotNull(json);
        Assert.Equal("final", json!["status"]!.GetValue<string>());
    }

    [Fact]
    public void GivenDuplicateAssignmentsToAlreadySuffixedChoiceName_WhenCreate_ThenThrows()
    {
        // Arrange - Observation.value[x] does not repeat, so assigning the suffixed
        // name twice must be rejected exactly as the bare base name is.
        var elements = new[]
        {
            new InstanceElement("valueString", [Prim("a", "string")]),
            new InstanceElement("valueString", [Prim("b", "string")]),
        };

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => Create("Observation", null, elements));
        Assert.Contains("valueString", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAssignmentNamedResourceType_WhenCreate_ThenDiscriminatorStaysTheConstructedType()
    {
        // Arrange
        var elements = new[]
        {
            new InstanceElement("resourceType", [Prim("Observation", "string")]),
            new InstanceElement("id", [Prim("p1", "string")]),
        };

        // Act
        var result = Create("Patient", null, elements);
        var json = result!.Meta<JsonNode>();

        // Assert - a user assignment must not be able to forge the resource discriminator.
        Assert.Equal("Patient", result.InstanceType);
        Assert.NotNull(json);
        Assert.Equal("Patient", json!["resourceType"]!.GetValue<string>());
    }

    private IElement? Create(string typeName, string? namespacePrefix, IReadOnlyList<InstanceElement> elements) =>
        _factory.Create(new InstanceCreationRequest(typeName, namespacePrefix, elements));

    private static IElement Prim(object value, string type) => new PrimitiveValueElement(value, type);

    private static IElement Prim(object value, string type, string name) => new PrimitiveValueElement(value, type, name);

    private sealed class ComplexValueElement : IElement
    {
        private readonly IReadOnlyList<IElement> _children;

        public ComplexValueElement(object value, string instanceType, IReadOnlyList<IElement> children)
        {
            Value = value;
            InstanceType = instanceType;
            _children = children;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;
        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null ? _children : [.. _children.Where(c => c.Name == name)];

        public T? Meta<T>() where T : class => null;
    }

    private sealed class ShadowedPrimitiveElement : IElement
    {
        private readonly JsonObject _shadow;

        public ShadowedPrimitiveElement(object value, string instanceType, JsonObject shadow)
        {
            Value = value;
            InstanceType = instanceType;
            _shadow = shadow;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => _shadow as T;
    }

    private sealed class PrimitiveValueElement : IElement
    {
        public PrimitiveValueElement(object value, string instanceType, string name = "")
        {
            Value = value;
            InstanceType = instanceType;
            Name = name;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
