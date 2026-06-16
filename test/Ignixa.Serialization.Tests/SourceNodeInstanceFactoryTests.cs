/*
 * Tests for SourceNodeInstanceFactory - the native source-node-backed
 * IInstanceFactory used for FHIRPath instance-selector object creation.
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
        var result = _factory.Create("Coding", null, elements);

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
        var result = _factory.Create("Coding", null, elements);
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
        var result = _factory.Create("Period", null, []);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Period", result!.InstanceType);
        Assert.Empty(result.Children());
    }

    [Fact]
    public void GivenUnknownType_WhenCreate_ThenReturnsNull()
    {
        var result = _factory.Create("CompletelyMadeUpType", null, []);

        Assert.Null(result);
    }

    [Fact]
    public void GivenSystemNamespace_WhenCreate_ThenReturnsNull()
    {
        var result = _factory.Create("String", "System", [new InstanceElement("value", [Prim("x", "string")])]);

        Assert.Null(result);
    }

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
