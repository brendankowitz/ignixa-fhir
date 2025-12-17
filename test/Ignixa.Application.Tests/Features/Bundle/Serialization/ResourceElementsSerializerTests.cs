// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Json;
using Shouldly;
using NSubstitute;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Abstractions;
using Xunit;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Comprehensive unit tests for ResourceElementsSerializer.
/// Tests streaming JSON filtering with zero-buffer implementation.
/// </summary>
public class ResourceElementsSerializerTests
{
    /// <summary>
    /// Test that only requested elements are included in output.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithRequestedElements_IncludesOnlyRequestedAndMandatory()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "123",
          "active": true,
          "name": [{"family": "Smith", "given": ["John"]}],
          "telecom": [{"system": "email", "value": "john@example.com"}],
          "birthDate": "1980-01-01"
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "active", "name", "telecom", "birthDate"]);
        var requestedElements = new HashSet<string> { "active", "name" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        result.ShouldContainKey("resource");

        var resourceElement = ((JsonElement)result["resource"]!);

        // Mandatory elements should be present
        resourceElement.TryGetProperty("resourceType", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("id", out _).ShouldBeTrue();

        // Requested elements should be present
        resourceElement.TryGetProperty("active", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("name", out _).ShouldBeTrue();

        // Non-requested elements should be absent
        resourceElement.TryGetProperty("telecom", out _).ShouldBeFalse();
        resourceElement.TryGetProperty("birthDate", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test that nested objects are written unfiltered when parent is included.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithNestedObject_IncludesEntireSubtree()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "123",
          "name": [
            {
              "use": "official",
              "family": "Smith",
              "given": ["John", "James"],
              "_family": {"extension": [{"url": "http://example.com", "valueString": "test"}]}
            }
          ],
          "active": true
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "active", "name"]);
        var requestedElements = new HashSet<string> { "name" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // name array should be present with all nested content
        resourceElement.TryGetProperty("name", out var nameArray).ShouldBeTrue();
        var nameElement = nameArray.EnumerateArray().First();

        // All properties in nested object should be present
        nameElement.TryGetProperty("use", out _).ShouldBeTrue();
        nameElement.TryGetProperty("family", out _).ShouldBeTrue();
        nameElement.TryGetProperty("given", out _).ShouldBeTrue();
        nameElement.TryGetProperty("_family", out _).ShouldBeTrue();

        // active should not be present (not requested)
        resourceElement.TryGetProperty("active", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test that mandatory elements are always included.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithMandatoryElements_AlwaysIncludesIdMetaResourceType()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "456",
          "meta": {"versionId": "1", "lastUpdated": "2024-01-01T00:00:00Z"},
          "name": [{"family": "Doe"}],
          "active": false
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "active", "name"]);
        var requestedElements = new HashSet<string>(); // Empty - no specific elements requested

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // Mandatory elements should always be present
        resourceElement.TryGetProperty("resourceType", out var resourceTypeValue).ShouldBeTrue();
        resourceTypeValue.GetString().ShouldBe("Patient");

        resourceElement.TryGetProperty("id", out var idValue).ShouldBeTrue();
        idValue.GetString().ShouldBe("456");

        resourceElement.TryGetProperty("meta", out _).ShouldBeTrue();

        // Non-mandatory elements should not be present
        resourceElement.TryGetProperty("name", out _).ShouldBeFalse();
        resourceElement.TryGetProperty("active", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test FHIR shorthand normalization: _id should become id.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithIdShorthand_NormalizesUnderscoreIdToId()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "789",
          "active": true
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "active"]);
        var requestedElements = new HashSet<string> { "_id" }; // Using FHIR shorthand

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // _id should be normalized to id
        resourceElement.TryGetProperty("id", out var idValue).ShouldBeTrue();
        idValue.GetString().ShouldBe("789");

        // active should not be present
        resourceElement.TryGetProperty("active", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test that schema-required elements are included even if not requested.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithSchemaRequiredElements_IncludesRequiredFields()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Observation",
          "id": "obs-1",
          "status": "final",
          "code": {"coding": [{"system": "http://loinc.org", "code": "12345"}]},
          "subject": {"reference": "Patient/123"},
          "effectiveDateTime": "2024-01-01T00:00:00Z",
          "value": {"Quantity": {"value": 98.6, "unit": "F"}}
        }
        """;

        // status and code are marked as required in Observation schema
        var schemaProvider = CreateMockSchemaProviderWithRequired("Observation",
            ["id", "resourceType", "meta", "status", "code", "subject", "effectiveDateTime", "value"],
            ["status", "code"]); // These are required

        var requestedElements = new HashSet<string> { "subject" }; // Only request subject

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Observation");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // Mandatory elements
        resourceElement.TryGetProperty("resourceType", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("id", out _).ShouldBeTrue();

        // Requested element
        resourceElement.TryGetProperty("subject", out _).ShouldBeTrue();

        // Schema-required elements should be included
        resourceElement.TryGetProperty("status", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("code", out _).ShouldBeTrue();

        // Other elements should not be present
        resourceElement.TryGetProperty("effectiveDateTime", out _).ShouldBeFalse();
        resourceElement.TryGetProperty("value", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test that arrays with complex objects are handled correctly.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithComplexArray_PreservesAllArrayElements()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-1",
          "telecom": [
            {"system": "phone", "value": "555-1234", "use": "home"},
            {"system": "email", "value": "patient@example.com", "use": "work"}
          ]
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "telecom"]);
        var requestedElements = new HashSet<string> { "telecom" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        resourceElement.TryGetProperty("telecom", out var telecomArray).ShouldBeTrue();
        var telecomElements = telecomArray.EnumerateArray().ToList();
        telecomElements.Count.ShouldBe(2);

        // First element
        telecomElements[0].TryGetProperty("system", out var system1).ShouldBeTrue();
        system1.GetString().ShouldBe("phone");
        telecomElements[0].TryGetProperty("value", out var value1).ShouldBeTrue();
        value1.GetString().ShouldBe("555-1234");
        telecomElements[0].TryGetProperty("use", out var use1).ShouldBeTrue();
        use1.GetString().ShouldBe("home");

        // Second element
        telecomElements[1].TryGetProperty("system", out var system2).ShouldBeTrue();
        system2.GetString().ShouldBe("email");
    }

    /// <summary>
    /// Test with null schema provider - should include all elements.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithNullSchemaProvider_IncludesAllRequestedElements()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-2",
          "active": true,
          "name": [{"family": "Johnson"}]
        }
        """;

        var requestedElements = new HashSet<string> { "active", "name" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            null, // No schema provider
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // Mandatory elements should always be present
        resourceElement.TryGetProperty("resourceType", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("id", out _).ShouldBeTrue();

        // Requested elements should be present
        resourceElement.TryGetProperty("active", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("name", out _).ShouldBeTrue();
    }

    /// <summary>
    /// Test with empty requested elements and no schema - should have only mandatory elements.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithEmptyElementsAndNoSchema_IncludesOnlyMandatory()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-3",
          "active": true,
          "gender": "male",
          "birthDate": "1990-01-01"
        }
        """;

        var requestedElements = new HashSet<string>(); // Empty

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            null,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        // Only mandatory elements
        resourceElement.TryGetProperty("resourceType", out _).ShouldBeTrue();
        resourceElement.TryGetProperty("id", out _).ShouldBeTrue();

        // All others should be absent
        resourceElement.TryGetProperty("active", out _).ShouldBeFalse();
        resourceElement.TryGetProperty("gender", out _).ShouldBeFalse();
        resourceElement.TryGetProperty("birthDate", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Test with deeply nested structures.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithDeeplyNestedStructure_PreservesCompleteHierarchy()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "pat-4",
          "contact": [
            {
              "relationship": [{"coding": [{"system": "http://terminology.hl7.org", "code": "C"}]}],
              "name": {"family": "Smith", "given": ["Jane"]},
              "telecom": [{"system": "phone", "value": "555-5678"}],
              "address": {"line": ["123 Main St"], "city": "Springfield", "state": "IL"}
            }
          ]
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Patient",
            ["id", "resourceType", "meta", "contact"]);
        var requestedElements = new HashSet<string> { "contact" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Patient");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        resourceElement.TryGetProperty("contact", out var contactArray).ShouldBeTrue();
        var contact = contactArray.EnumerateArray().First();

        // All nested properties should be present
        contact.TryGetProperty("relationship", out _).ShouldBeTrue();
        contact.TryGetProperty("name", out _).ShouldBeTrue();
        contact.TryGetProperty("telecom", out _).ShouldBeTrue();
        contact.TryGetProperty("address", out _).ShouldBeTrue();

        // Verify nested coding structure is preserved
        contact.TryGetProperty("relationship", out var relationship).ShouldBeTrue();
        var relationshipElement = relationship.EnumerateArray().First();
        relationshipElement.TryGetProperty("coding", out var coding).ShouldBeTrue();
        var codingElement = coding.EnumerateArray().First();
        codingElement.TryGetProperty("system", out var system).ShouldBeTrue();
        codingElement.TryGetProperty("code", out var code).ShouldBeTrue();
    }

    /// <summary>
    /// Test with boolean, number, and null values.
    /// </summary>
    [Fact]
    public void WriteFilteredResourceProperty_WithVariousValueTypes_PreservesTypes()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Observation",
          "id": "obs-2",
          "status": "final",
          "code": {"coding": [{"system": "http://loinc.org", "code": "12345"}]},
          "value": {"Quantity": {"value": 98.6, "unit": "F", "system": "http://unitsofmeasure.org", "code": "[degF]"}},
          "interpretation": [{"coding": [{"system": "http://terminology.hl7.org", "code": "N"}]}],
          "note": [{"text": "This is a note"}]
        }
        """;

        var schemaProvider = CreateMockSchemaProvider("Observation",
            ["id", "resourceType", "meta", "status", "code", "value", "interpretation", "note"]);
        var requestedElements = new HashSet<string> { "value" };

        // Act
        var output = CallWriteFilteredResourceProperty(
            resourceJson,
            schemaProvider,
            requestedElements,
            "Observation");

        // Assert
        var result = ParseOutput(output);
        var resourceElement = ((JsonElement)result["resource"]!);

        resourceElement.TryGetProperty("value", out var valueObj).ShouldBeTrue();
        valueObj.TryGetProperty("Quantity", out var quantity).ShouldBeTrue();

        // Numeric value should be preserved
        quantity.TryGetProperty("value", out var numValue).ShouldBeTrue();
        numValue.GetDouble().ShouldBe(98.6);

        // String values should be preserved
        quantity.TryGetProperty("unit", out var unit).ShouldBeTrue();
        unit.GetString().ShouldBe("F");
    }

    // Helper Methods

    private MemoryStream CallWriteFilteredResourceProperty(
        string resourceJson,
        ISchema? schemaProvider,
        IReadOnlySet<string> requestedElements,
        string resourceType)
    {
        var output = new MemoryStream();
        var writer = FhirJsonWriter.Create(output);

        writer.WriteStartObject();
        ResourceElementsSerializer.WriteFilteredResourceProperty(
            writer,
            "resource",
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(resourceJson)),
            schemaProvider,
            requestedElements,
            resourceType);
        writer.WriteEndObject();
        writer.Dispose();

        return output;
    }

    private Dictionary<string, object?> ParseOutput(MemoryStream stream)
    {
        stream.Position = 0;
        var json = Encoding.UTF8.GetString(stream.ToArray());
        var doc = JsonDocument.Parse(json);
        return JsonElementToDictionary(doc.RootElement);
    }

    private Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }
        return dict;
    }

    private ISchema CreateMockSchemaProvider(
        string resourceType,
        string[] elementNames)
    {
        var provider = Substitute.For<ISchema>();
        var typeDefinition = Substitute.For<IType>();

        var elements = elementNames.Select(name => CreateMockTypeForElement(name, isRequired: false)).ToList();
        typeDefinition.Children.Returns(elements);

        provider.GetTypeDefinition(resourceType).Returns(typeDefinition);
        provider.GetTypeDefinition(Arg.Is<string>(rt => rt != resourceType)).Returns((IType?)null);

        return provider;
    }

    private ISchema CreateMockSchemaProviderWithRequired(
        string resourceType,
        string[] elementNames,
        string[] requiredElementNames)
    {
        var provider = Substitute.For<ISchema>();
        var typeDefinition = Substitute.For<IType>();

        var requiredSet = new HashSet<string>(requiredElementNames);
        var elements = elementNames.Select(name =>
            CreateMockTypeForElement(name, isRequired: requiredSet.Contains(name))).ToList();

        typeDefinition.Children.Returns(elements);

        provider.GetTypeDefinition(resourceType).Returns(typeDefinition);
        provider.GetTypeDefinition(Arg.Is<string>(rt => rt != resourceType)).Returns((IType?)null);

        return provider;
    }

    private IType CreateMockTypeForElement(string name, bool isRequired)
    {
        var type = Substitute.For<IType>();
        var info = new TypeInfo(name);
        type.Info.Returns(info);
        type.IsRequired.Returns(isRequired);
        return type;
    }
}
