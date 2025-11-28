// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.Specification.Generated;
using Xunit;

// Alias to avoid ambiguity with Ignixa.Abstractions.ISourceNode
using SdkISourceNode = Hl7.Fhir.ElementModel.ISourceNode;

namespace Ignixa.Extensions.Tests.FirelySdk;

/// <summary>
/// Tests for SourceNavigatorAdapter and ISourceNode → IElement conversions.
/// </summary>
public class SourceNavigatorAdapterTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    #region SourceNavigatorAdapter Tests

    [Fact]
    public void GivenNullSourceNode_WhenCreatingAdapter_ThenThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new SourceNavigatorAdapter(null!));
        Assert.Equal("sourceNode", exception.ParamName);
    }

    [Fact]
    public void GivenFirelySourceNode_WhenAccessingName_ThenReturnsCorrectValue()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Name = "family" };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Name;

        // Assert
        Assert.Equal("family", result);
    }

    [Fact]
    public void GivenFirelySourceNode_WhenAccessingText_ThenReturnsCorrectValue()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Text = "Smith" };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Text;

        // Assert
        Assert.Equal("Smith", result);
    }

    [Fact]
    public void GivenFirelySourceNode_WhenAccessingLocation_ThenReturnsCorrectValue()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Location = "Patient.name[0].family" };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Location;

        // Assert
        Assert.Equal("Patient.name[0].family", result);
    }

    [Fact]
    public void GivenFirelySourceNodeWithResourceTypeChild_WhenAccessingResourceType_ThenDeriveFromChild()
    {
        // Arrange
        var resourceTypeNode = new MockSourceNode { Name = "resourceType", Text = "Patient" };
        var sourceNode = new MockSourceNode
        {
            Name = "Patient",
            ChildNodes = new List<SdkISourceNode> { resourceTypeNode }
        };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.ResourceType;

        // Assert
        Assert.Equal("Patient", result);
    }

    [Fact]
    public void GivenFirelySourceNodeWithoutResourceTypeChild_WhenAccessingResourceType_ThenReturnsEmptyString()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Name = "name" };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.ResourceType;

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GivenFirelySourceNodeWithChildren_WhenCallingChildrenWithoutName_ThenReturnsAllChildren()
    {
        // Arrange
        var child1 = new MockSourceNode { Name = "given", Text = "John" };
        var child2 = new MockSourceNode { Name = "family", Text = "Smith" };
        var sourceNode = new MockSourceNode
        {
            Name = "name",
            ChildNodes = new List<SdkISourceNode> { child1, child2 }
        };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Children().ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("given", result[0].Name);
        Assert.Equal("family", result[1].Name);
    }

    [Fact]
    public void GivenFirelySourceNodeWithChildren_WhenCallingChildrenWithName_ThenFiltersCorrectly()
    {
        // Arrange
        var child1 = new MockSourceNode { Name = "given", Text = "John" };
        var child2 = new MockSourceNode { Name = "family", Text = "Smith" };
        var child3 = new MockSourceNode { Name = "given", Text = "James" };
        var sourceNode = new MockSourceNode
        {
            Name = "name",
            ChildNodes = new List<SdkISourceNode> { child1, child2, child3 }
        };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Children("given").ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, child => Assert.Equal("given", child.Name));
    }

    [Fact]
    public void GivenFirelySourceNodeChildren_WhenAccessed_ThenReturnsSourceNavigatorAdapters()
    {
        // Arrange
        var child = new MockSourceNode { Name = "family" };
        var sourceNode = new MockSourceNode { ChildNodes = new List<SdkISourceNode> { child } };
        var adapter = new SourceNavigatorAdapter(sourceNode);

        // Act
        var result = adapter.Children().First();

        // Assert
        Assert.IsType<SourceNavigatorAdapter>(result);
    }

    #endregion

    #region ToSourceNavigator Extension Tests

    [Fact]
    public void GivenNullSourceNode_WhenCallingToSourceNavigator_ThenThrowsArgumentNullException()
    {
        // Arrange
        SdkISourceNode? sourceNode = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => sourceNode!.ToSourceNavigator());
        Assert.Equal("sourceNode", exception.ParamName);
    }

    [Fact]
    public void GivenFirelySourceNode_WhenCallingToSourceNavigator_ThenReturnsAdapter()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Name = "test" };

        // Act
        var result = sourceNode.ToSourceNavigator();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SourceNavigatorAdapter>(result);
        Assert.Equal("test", result.Name);
    }

    #endregion

    #region ToElement Extension Tests

    [Fact]
    public void GivenNullSourceNode_WhenCallingToElement_ThenThrowsArgumentNullException()
    {
        // Arrange
        SdkISourceNode? sourceNode = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => sourceNode!.ToElement(_schema));
        Assert.Equal("sourceNode", exception.ParamName);
    }

    [Fact]
    public void GivenSourceNodeWithNullSchema_WhenCallingToElement_ThenThrowsArgumentNullException()
    {
        // Arrange
        var sourceNode = new MockSourceNode { Name = "test" };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => sourceNode.ToElement(null!));
        Assert.Equal("schema", exception.ParamName);
    }

    [Fact]
    public void GivenPatientSourceNode_WhenCallingToElement_ThenReturnsIElement()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "123",
                "active": true
            }
            """;
        var sourceNode = FhirJsonNode.Parse(json);

        // Act
        var result = sourceNode.ToElement(_schema);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Patient", result.InstanceType);
    }

    [Fact]
    public void GivenPatientSourceNode_WhenCallingToElement_ThenChildrenAreAccessible()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "123",
                "active": true,
                "name": [
                    {
                        "family": "Smith",
                        "given": ["John", "James"]
                    }
                ]
            }
            """;
        var sourceNode = FhirJsonNode.Parse(json);

        // Act
        var element = sourceNode.ToElement(_schema);
        var names = element.Children("name");
        var family = names.First().Children("family").FirstOrDefault();

        // Assert
        Assert.Single(names);
        Assert.NotNull(family);
        Assert.Equal("Smith", family.Value);
    }

    [Fact]
    public void GivenObservationSourceNode_WhenCallingToElement_ThenHasCorrectInstanceType()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Observation",
                "id": "obs-1",
                "status": "final",
                "code": {
                    "coding": [
                        {
                            "system": "http://loinc.org",
                            "code": "12345-6"
                        }
                    ]
                }
            }
            """;
        var sourceNode = FhirJsonNode.Parse(json);

        // Act
        var element = sourceNode.ToElement(_schema);

        // Assert
        Assert.Equal("Observation", element.InstanceType);
    }

    [Fact]
    public void GivenSourceNodeWithChoiceType_WhenCallingToElement_ThenResolvesCorrectly()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Observation",
                "id": "obs-1",
                "status": "final",
                "code": { "text": "Test" },
                "valueQuantity": {
                    "value": 42,
                    "unit": "mg"
                }
            }
            """;
        var sourceNode = FhirJsonNode.Parse(json);

        // Act
        var element = sourceNode.ToElement(_schema);
        var valueChildren = element.Children("value");

        // Assert - Choice element "value" should find "valueQuantity"
        Assert.Single(valueChildren);
        Assert.Equal("valueQuantity", valueChildren.First().Name);
    }

    [Fact]
    public void GivenSourceNodeWithNestedElements_WhenNavigating_ThenPreservesStructure()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "name": [
                    {
                        "use": "official",
                        "family": "Smith",
                        "given": ["John"]
                    }
                ],
                "address": [
                    {
                        "city": "Boston",
                        "state": "MA"
                    }
                ]
            }
            """;
        var sourceNode = FhirJsonNode.Parse(json);

        // Act
        var element = sourceNode.ToElement(_schema);
        var name = element.Children("name").First();
        var address = element.Children("address").First();

        // Assert
        Assert.Equal("official", name.Children("use").First().Value);
        Assert.Equal("Smith", name.Children("family").First().Value);
        Assert.Equal("Boston", address.Children("city").First().Value);
        Assert.Equal("MA", address.Children("state").First().Value);
    }

    #endregion

    #region Integration Tests with Real Firely Parser

    [Fact]
    public void GivenRealFirelyParsedNode_WhenConvertingToElement_ThenWorksCorrectly()
    {
        // Arrange
        var json = """
            {
                "resourceType": "Patient",
                "id": "example",
                "meta": {
                    "versionId": "1"
                },
                "active": true,
                "name": [
                    {
                        "use": "official",
                        "family": "Chalmers",
                        "given": ["Peter", "James"]
                    }
                ]
            }
            """;

        // Parse with Firely SDK
        var firelySourceNode = FhirJsonNode.Parse(json);

        // Act - Convert to Ignixa IElement
        var element = firelySourceNode.ToElement(_schema);

        // Assert
        Assert.Equal("Patient", element.InstanceType);

        var id = element.Children("id").FirstOrDefault();
        Assert.NotNull(id);
        Assert.Equal("example", id.Value);

        var active = element.Children("active").FirstOrDefault();
        Assert.NotNull(active);
        Assert.Equal(true, active.Value);

        var name = element.Children("name").FirstOrDefault();
        Assert.NotNull(name);
        Assert.Equal("HumanName", name.InstanceType);
    }

    #endregion

    #region Mock Implementation

    /// <summary>
    /// Mock implementation of Firely SDK's ISourceNode for unit testing.
    /// </summary>
    private class MockSourceNode : SdkISourceNode
    {
        public string Name { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
        public List<SdkISourceNode> ChildNodes { get; init; } = new();

        public IEnumerable<SdkISourceNode> Children(string? name = null)
        {
            if (name == null)
                return ChildNodes;

            return ChildNodes.Where(c => c.Name == name);
        }
    }

    #endregion
}
