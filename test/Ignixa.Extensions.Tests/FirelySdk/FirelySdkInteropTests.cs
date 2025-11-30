// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Xunit;

namespace Ignixa.Extensions.Tests.FirelySdk;

/// <summary>
/// Comprehensive tests for bidirectional Firely SDK interoperability adapters.
/// Tests IgnixaElementAdapter, TypedElementAdapter, and extension methods.
/// </summary>
public class FirelySdkInteropTests
{
    #region IgnixaElementAdapter Tests

    [Fact]
    public void GivenNullFirelyElement_WhenCreatingCoreElementAdapter_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new IgnixaElementAdapter(null!));
        Assert.Equal("firelyElement", exception.ParamName);
    }

    [Fact]
    public void GivenFirelyElement_WhenAccessingName_ThenReturnsCorrectValue()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Name = "family" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Name;

        // Assert
        Assert.Equal("family", result);
    }

    [Fact]
    public void GivenFirelyElement_WhenAccessingValue_ThenReturnsCorrectValue()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Value = "Smith" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Value;

        // Assert
        Assert.Equal("Smith", result);
    }

    [Fact]
    public void GivenFirelyElement_WhenAccessingInstanceType_ThenReturnsCorrectValue()
    {
        // Arrange
        var firelyElement = new MockTypedElement { InstanceType = "string" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.InstanceType;

        // Assert
        Assert.Equal("string", result);
    }

    [Fact]
    public void GivenFirelyElementWithNullInstanceType_WhenAccessingInstanceType_ThenReturnsEmptyString()
    {
        // Arrange
        var firelyElement = new MockTypedElement { InstanceType = null };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.InstanceType;

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GivenFirelyElement_WhenAccessingLocation_ThenReturnsCorrectValue()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Location = "Patient.name[0].family" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Location;

        // Assert
        Assert.Equal("Patient.name[0].family", result);
    }

    [Fact]
    public void GivenFirelyElementWithDefinition_WhenAccessingType_ThenReturnsAdapter()
    {
        // Arrange
        var definition = new MockElementDefinitionSummary { ElementName = "string" };
        var firelyElement = new MockTypedElement { Definition = definition };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Type;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("string", result.Info.Name);
    }

    [Fact]
    public void GivenFirelyElementWithoutDefinition_WhenAccessingType_ThenReturnsNull()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Definition = null };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Type;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenFirelyElementWithChildren_WhenCallingChildrenWithoutName_ThenReturnsAllChildren()
    {
        // Arrange
        var child1 = new MockTypedElement { Name = "given" };
        var child2 = new MockTypedElement { Name = "family" };
        var firelyElement = new MockTypedElement
        {
            ChildElements = new List<ITypedElement> { child1, child2 }
        };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Children();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("given", result[0].Name);
        Assert.Equal("family", result[1].Name);
    }

    [Fact]
    public void GivenFirelyElementWithChildren_WhenCallingChildrenWithName_ThenFiltersCorrectly()
    {
        // Arrange
        var child1 = new MockTypedElement { Name = "given" };
        var child2 = new MockTypedElement { Name = "family" };
        var child3 = new MockTypedElement { Name = "given" };
        var firelyElement = new MockTypedElement
        {
            ChildElements = new List<ITypedElement> { child1, child2, child3 }
        };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Children("given");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, child => Assert.Equal("given", child.Name));
    }

    [Fact]
    public void GivenFirelyElement_WhenCallingChildrenMultipleTimes_ThenCachesChildren()
    {
        // Arrange
        var child1 = new MockTypedElement { Name = "given" };
        var firelyElement = new MockTypedElement
        {
            ChildElements = new List<ITypedElement> { child1 }
        };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result1 = adapter.Children();
        var result2 = adapter.Children();

        // Assert - Same instance means cached
        Assert.Same(result1, result2);
    }

    [Fact]
    public void GivenFirelyElement_WhenCallingMetaForITypedElement_ThenReturnsOriginalFirelyElement()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Name = "test" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Meta<ITypedElement>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(firelyElement, result);
    }

    [Fact]
    public void GivenFirelyElement_WhenCallingMetaForOtherType_ThenReturnsNull()
    {
        // Arrange
        var firelyElement = new MockTypedElement { Name = "test" };
        var adapter = new IgnixaElementAdapter(firelyElement);

        // Act
        var result = adapter.Meta<string>();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region TypedElementAdapter Tests

    [Fact]
    public void GivenNullIgnixaElement_WhenCreatingTypedElementAdapter_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new TypedElementAdapter(null!));
        Assert.Equal("coreElement", exception.ParamName);
    }

    [Fact]
    public void GivenIgnixaElement_WhenAccessingName_ThenReturnsCorrectValue()
    {
        // Arrange
        var element = new MockElement { Name = "family" };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Name;

        // Assert
        Assert.Equal("family", result);
    }

    [Fact]
    public void GivenIgnixaElement_WhenAccessingValue_ThenReturnsCorrectValue()
    {
        // Arrange
        var element = new MockElement { Value = "Smith" };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Value;

        // Assert
        Assert.Equal("Smith", result);
    }

    [Fact]
    public void GivenIgnixaElement_WhenAccessingInstanceType_ThenReturnsCorrectValue()
    {
        // Arrange
        var element = new MockElement { InstanceType = "string" };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.InstanceType;

        // Assert
        Assert.Equal("string", result);
    }

    [Fact]
    public void GivenIgnixaElement_WhenAccessingLocation_ThenReturnsCorrectValue()
    {
        // Arrange
        var element = new MockElement { Location = "Patient.name[0].family" };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Location;

        // Assert
        Assert.Equal("Patient.name[0].family", result);
    }

    [Fact]
    public void GivenIgnixaElementWithType_WhenAccessingDefinition_ThenReturnsAdapter()
    {
        // Arrange
        var type = new MockType
        {
            Info = new TypeInfo("string", FhirPrimitive.String)
        };
        var element = new MockElement { Type = type };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Definition;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("string", result.ElementName);
    }

    [Fact]
    public void GivenIgnixaElementWithoutType_WhenAccessingDefinition_ThenReturnsNull()
    {
        // Arrange
        var element = new MockElement { Type = null };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Definition;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenIgnixaElementWithChildren_WhenCallingChildrenWithoutName_ThenConvertsAllChildren()
    {
        // Arrange
        var child1 = new MockElement { Name = "given" };
        var child2 = new MockElement { Name = "family" };
        var element = new MockElement
        {
            ChildElements = new List<IElement> { child1, child2 }
        };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Children().ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("given", result[0].Name);
        Assert.Equal("family", result[1].Name);
    }

    [Fact]
    public void GivenIgnixaElementWithChildren_WhenCallingChildrenWithName_ThenFiltersCorrectly()
    {
        // Arrange
        var child1 = new MockElement { Name = "given" };
        var child2 = new MockElement { Name = "family" };
        var element = new MockElement
        {
            ChildElements = new List<IElement> { child1, child2 }
        };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Children("family").ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("family", result[0].Name);
    }

    [Fact]
    public void GivenIgnixaElement_WhenCallingAnnotationForIElement_ThenReturnsOriginalIgnixaElement()
    {
        // Arrange
        var element = new MockElement { Name = "test" };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Annotation<IElement>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(element, result);
    }

    [Fact]
    public void GivenIgnixaElement_WhenCallingAnnotationForOtherType_ThenDelegatesToMeta()
    {
        // Arrange
        var metadata = "custom-metadata";
        var element = new MockElement { Name = "test", Metadata = metadata };
        var adapter = new TypedElementAdapter(element);

        // Act
        var result = adapter.Annotation<string>();

        // Assert
        Assert.Equal(metadata, result);
    }

    #endregion

    #region IgnixaExtensions Tests

    [Fact]
    public void GivenNullFirelyElement_WhenCallingToCoreElement_ThenThrowsArgumentNullException()
    {
        // Arrange
        ITypedElement? element = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => element!.ToIgnixaElement());
        Assert.Equal("element", exception.ParamName);
    }

    [Fact]
    public void GivenRegularFirelyElement_WhenCallingToCoreElement_ThenReturnsCoreElementAdapter()
    {
        // Arrange
        var element = new MockTypedElement { Name = "test" };

        // Act
        var result = element.ToIgnixaElement();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<IgnixaElementAdapter>(result);
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void GivenTypedElementAdapterWrappingIgnixaElement_WhenCallingToCoreElement_ThenUnwrapsToOriginal()
    {
        // Arrange
        var originalElement = new MockElement { Name = "original" };
        var adapter = new TypedElementAdapter(originalElement);

        // Act
        var result = adapter.ToIgnixaElement();

        // Assert
        Assert.Same(originalElement, result);
    }

    [Fact]
    public void GivenNullCollection_WhenCallingToCoreElements_ThenThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<ITypedElement>? elements = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => elements!.ToIgnixaElements());
        Assert.Equal("elements", exception.ParamName);
    }

    [Fact]
    public void GivenFirelyElementCollection_WhenCallingToCoreElements_ThenConvertsAllElements()
    {
        // Arrange
        var elements = new List<ITypedElement>
        {
            new MockTypedElement { Name = "first" },
            new MockTypedElement { Name = "second" },
            new MockTypedElement { Name = "third" }
        };

        // Act
        var result = elements.ToIgnixaElements().ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("first", result[0].Name);
        Assert.Equal("second", result[1].Name);
        Assert.Equal("third", result[2].Name);
    }

    #endregion

    #region FirelySdkExtensions Tests

    [Fact]
    public void GivenNullIgnixaElement_WhenCallingToTypedElement_ThenThrowsArgumentNullException()
    {
        // Arrange
        IElement? element = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => element!.ToTypedElement());
        Assert.Equal("element", exception.ParamName);
    }

    [Fact]
    public void GivenRegularIgnixaElement_WhenCallingToTypedElement_ThenReturnsTypedElementAdapter()
    {
        // Arrange
        var element = new MockElement { Name = "test" };

        // Act
        var result = element.ToTypedElement();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TypedElementAdapter>(result);
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void GivenCoreElementAdapterWrappingFirelyElement_WhenCallingToTypedElement_ThenUnwrapsToOriginal()
    {
        // Arrange
        var originalElement = new MockTypedElement { Name = "original" };
        var adapter = new IgnixaElementAdapter(originalElement);

        // Act
        var result = adapter.ToTypedElement();

        // Assert
        Assert.Same(originalElement, result);
    }

    [Fact]
    public void GivenNullIEnumerableCollection_WhenCallingToTypedElements_ThenThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<IElement>? elements = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => elements!.ToTypedElements());
        Assert.Equal("elements", exception.ParamName);
    }

    [Fact]
    public void GivenIgnixaIEnumerableCollection_WhenCallingToTypedElements_ThenConvertsAllElements()
    {
        // Arrange
        var elements = new List<IElement>
        {
            new MockElement { Name = "first" },
            new MockElement { Name = "second" },
            new MockElement { Name = "third" }
        };

        // Act
        var result = elements.ToTypedElements().ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("first", result[0].Name);
        Assert.Equal("second", result[1].Name);
        Assert.Equal("third", result[2].Name);
    }

    [Fact]
    public void GivenNullReadOnlyListCollection_WhenCallingToTypedElements_ThenThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<IElement>? elements = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => elements!.ToTypedElements());
        Assert.Equal("elements", exception.ParamName);
    }

    [Fact]
    public void GivenIgnixaReadOnlyListCollection_WhenCallingToTypedElements_ThenConvertsAllElements()
    {
        // Arrange
        IReadOnlyList<IElement> elements = new List<IElement>
        {
            new MockElement { Name = "first" },
            new MockElement { Name = "second" },
            new MockElement { Name = "third" }
        };

        // Act
        var result = elements.ToTypedElements().ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("first", result[0].Name);
        Assert.Equal("second", result[1].Name);
        Assert.Equal("third", result[2].Name);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void GivenFirelyElement_WhenConvertingToIgnixaAndBackToFirely_ThenPreservesData()
    {
        // Arrange
        var original = new MockTypedElement
        {
            Name = "family",
            Value = "Smith",
            InstanceType = "string",
            Location = "Patient.name[0].family"
        };

        // Act - Firely → Ignixa → Firely
        var ignixaElement = original.ToIgnixaElement();
        var result = ignixaElement.ToTypedElement();

        // Assert - Should unwrap to original
        Assert.Same(original, result);
    }

    [Fact]
    public void GivenIgnixaElement_WhenConvertingToFirelyAndBackToIgnixa_ThenPreservesData()
    {
        // Arrange
        var original = new MockElement
        {
            Name = "family",
            Value = "Smith",
            InstanceType = "string",
            Location = "Patient.name[0].family"
        };

        // Act - Ignixa → Firely → Ignixa
        var firelyElement = original.ToTypedElement();
        var result = firelyElement.ToIgnixaElement();

        // Assert - Should unwrap to original
        Assert.Same(original, result);
    }

    [Fact]
    public void GivenFirelyElementWithNestedChildren_WhenConvertingRoundTrip_ThenPreservesStructure()
    {
        // Arrange
        var grandchild = new MockTypedElement { Name = "text", Value = "Dr." };
        var child = new MockTypedElement
        {
            Name = "prefix",
            ChildElements = new List<ITypedElement> { grandchild }
        };
        var parent = new MockTypedElement
        {
            Name = "name",
            ChildElements = new List<ITypedElement> { child }
        };

        // Act - Convert to Ignixa and access nested children
        var ignixaParent = parent.ToIgnixaElement();
        var ignixaChild = ignixaParent.Children("prefix").First();
        var ignixaGrandchild = ignixaChild.Children("text").First();

        // Assert
        Assert.Equal("prefix", ignixaChild.Name);
        Assert.Equal("text", ignixaGrandchild.Name);
        Assert.Equal("Dr.", ignixaGrandchild.Value);
    }

    [Fact]
    public void GivenIgnixaElementWithNestedChildren_WhenConvertingRoundTrip_ThenPreservesStructure()
    {
        // Arrange
        var grandchild = new MockElement { Name = "text", Value = "Dr." };
        var child = new MockElement
        {
            Name = "prefix",
            ChildElements = new List<IElement> { grandchild }
        };
        var parent = new MockElement
        {
            Name = "name",
            ChildElements = new List<IElement> { child }
        };

        // Act - Convert to Firely and access nested children
        var firelyParent = parent.ToTypedElement();
        var firelyChild = firelyParent.Children("prefix").First();
        var firelyGrandchild = firelyChild.Children("text").First();

        // Assert
        Assert.Equal("prefix", firelyChild.Name);
        Assert.Equal("text", firelyGrandchild.Name);
        Assert.Equal("Dr.", firelyGrandchild.Value);
    }

    #endregion

    #region Mock Implementations

    /// <summary>
    /// Mock implementation of Firely SDK's ITypedElement for testing.
    /// </summary>
    private class MockTypedElement : ITypedElement
    {
        public string Name { get; init; } = string.Empty;
        public object? Value { get; init; }
        public string? InstanceType { get; init; }
        public string Location { get; init; } = string.Empty;
        public IElementDefinitionSummary? Definition { get; init; }
        public List<ITypedElement> ChildElements { get; init; } = new List<ITypedElement>();

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            if (name == null)
                return ChildElements;

            return ChildElements.Where(c => c.Name == name);
        }

        public T? Annotation<T>() where T : class => null;
    }

    /// <summary>
    /// Mock implementation of Firely SDK's IElementDefinitionSummary for testing.
    /// </summary>
    private class MockElementDefinitionSummary : IElementDefinitionSummary
    {
        public string ElementName { get; init; } = string.Empty;
        public bool IsCollection { get; init; }
        public bool IsRequired { get; init; }
        public bool InSummary { get; init; }
        public bool IsChoiceElement { get; init; }
        public bool IsResource { get; init; }
        public bool IsModifier { get; init; }
        public ITypeSerializationInfo[] Type { get; init; } = Array.Empty<ITypeSerializationInfo>();
        public string? DefaultTypeName { get; init; }
        public string? NonDefaultNamespace { get; init; }
        public XmlRepresentation Representation { get; init; }
        public int Order { get; init; }
    }

    /// <summary>
    /// Mock implementation of Ignixa's IElement for testing.
    /// </summary>
    private class MockElement : IElement
    {
        public string Name { get; init; } = string.Empty;
        public object? Value { get; init; }
        public string InstanceType { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
        public IType? Type { get; init; }
        public List<IElement> ChildElements { get; init; } = new List<IElement>();
        public object? Metadata { get; init; }

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
                return ChildElements.AsReadOnly();

            return ChildElements.Where(c => c.Name == name).ToArray();
        }

        public T? Meta<T>() where T : class => Metadata as T;
    }

    /// <summary>
    /// Mock implementation of Ignixa's IType for testing.
    /// </summary>
    private class MockType : IType
    {
        public TypeInfo Info { get; init; }
        public bool IsCollection { get; init; }
        public bool IsRequired { get; init; }
        public bool InSummary { get; init; }
        public int Order { get; init; }
        public IReadOnlyList<IType> Children { get; init; } = Array.Empty<IType>();
    }

    #endregion
}
