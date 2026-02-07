/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for SerializeComplexElement in JsonNodeMutator.
 * Verifies that single-element arrays are preserved as JsonArray (not collapsed to JsonObject)
 * when the child's IType.IsCollection is true.
 *
 * These tests exercise the actual serialization path by using IElement wrappers
 * that strip JsonNode metadata, forcing SerializeValue to fall through to SerializeComplexElement.
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Mutator;

public class SerializeComplexElementTests
{
    private readonly JsonNodeMutator _mutator;
    private readonly IFhirSchemaProvider _schemaProvider;

    public SerializeComplexElementTests()
    {
        _schemaProvider = FhirVersion.R4.GetSchemaProvider();
        _mutator = new JsonNodeMutator(
            new FhirPathEvaluator(),
            new FhirPathParser(),
            () => _schemaProvider);
    }

    [Fact]
    public void GivenCodeableConceptWithSingleCoding_WhenSerializedViaSetProperty_ThenCodingIsJsonArray()
    {
        var sourceJson = """
        {
          "resourceType": "Observation",
          "id": "test",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "1234-5",
                "display": "Test Code"
              }
            ],
            "text": "Test"
          }
        }
        """;

        var source = ResourceJsonNode.Parse(sourceJson);
        var sourceElement = source.ToElement(_schemaProvider);
        var codeElement = sourceElement.Children("code")[0];

        var strippedElement = new MetadataStrippingElement(codeElement);

        var target = ResourceJsonNode.Parse("""
        {
          "resourceType": "Observation",
          "id": "target",
          "status": "final",
          "code": {"text": "placeholder"}
        }
        """);

        _mutator.SetProperty(target, "Observation.code", strippedElement, PropertyMutationMode.Replace);

        var resultCode = target.MutableNode["code"]?.AsObject();
        resultCode.ShouldNotBeNull();
        resultCode.ContainsKey("coding").ShouldBeTrue();

        var codingNode = resultCode["coding"];
        codingNode.ShouldNotBeNull();
        codingNode.ShouldBeOfType<JsonArray>();

        var codingArray = (JsonArray)codingNode;
        codingArray.Count.ShouldBe(1);
        codingArray[0]?["system"]?.GetValue<string>().ShouldBe("http://loinc.org");
    }

    [Fact]
    public void GivenIdentifierWithSingleCodingInType_WhenSerializedViaSetProperty_ThenNestedArraysPreserved()
    {
        var sourceJson = """
        {
          "resourceType": "Patient",
          "id": "test",
          "identifier": [
            {
              "system": "http://example.org/mrn",
              "value": "12345",
              "type": {
                "coding": [
                  {
                    "system": "http://terminology.hl7.org/CodeSystem/v2-0203",
                    "code": "MR"
                  }
                ]
              }
            }
          ]
        }
        """;

        var source = ResourceJsonNode.Parse(sourceJson);
        var sourceElement = source.ToElement(_schemaProvider);
        var identifierElement = sourceElement.Children("identifier")[0];
        var typeElement = identifierElement.Children("type")[0];

        var strippedType = new MetadataStrippingElement(typeElement);

        var target = ResourceJsonNode.Parse("""
        {
          "resourceType": "Patient",
          "id": "target",
          "identifier": [{"type": {"text": "old"}}]
        }
        """);

        _mutator.SetProperty(target, "Patient.identifier.type", strippedType, PropertyMutationMode.Replace);

        var resultIdentifier = target.MutableNode["identifier"]?[0]?.AsObject();
        resultIdentifier.ShouldNotBeNull();

        var resultType = resultIdentifier["type"]?.AsObject();
        resultType.ShouldNotBeNull();
        resultType.ContainsKey("coding").ShouldBeTrue();

        var codingNode = resultType["coding"];
        codingNode.ShouldNotBeNull();
        codingNode.ShouldBeOfType<JsonArray>();
    }

    [Fact]
    public void GivenElementWithoutTypeMetadata_WhenSingleChildSerialized_ThenNotWrappedInArray()
    {
        var sourceJson = """
        {
          "resourceType": "Observation",
          "id": "test",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "1234-5"
              }
            ],
            "text": "Test"
          }
        }
        """;

        var source = ResourceJsonNode.Parse(sourceJson);
        var sourceElement = source.ToElement(_schemaProvider);
        var codeElement = sourceElement.Children("code")[0];

        var noTypeElement = new NoTypeMetadataElement(codeElement);

        var target = ResourceJsonNode.Parse("""
        {
          "resourceType": "Observation",
          "id": "target",
          "status": "final",
          "code": {"text": "placeholder"}
        }
        """);

        _mutator.SetProperty(target, "Observation.code", noTypeElement, PropertyMutationMode.Replace);

        var resultCode = target.MutableNode["code"]?.AsObject();
        resultCode.ShouldNotBeNull();

        var codingNode = resultCode["coding"];
        codingNode.ShouldNotBeNull();
        codingNode.ShouldBeOfType<JsonObject>("Without Type metadata, single-element arrays degrade to objects");
    }

    /// <summary>
    /// Wraps an IElement to strip JsonNode metadata while preserving Type metadata.
    /// Forces SerializeValue to fall through to SerializeComplexElement.
    /// </summary>
    private class MetadataStrippingElement(IElement inner) : IElement
    {
        public string Name => inner.Name;
        public string InstanceType => inner.InstanceType;
        public object? Value => inner.Value;
        public string Location => inner.Location;
        public IType? Type => inner.Type;
        public bool HasPrimitiveValue => inner.HasPrimitiveValue;

        public IReadOnlyList<IElement> Children(string? name = null)
            => inner.Children(name).Select(c => (IElement)new MetadataStrippingElement(c)).ToList();

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Wraps an IElement to strip BOTH JsonNode metadata AND Type metadata.
    /// Demonstrates the degraded behavior when schema context is unavailable.
    /// </summary>
    private class NoTypeMetadataElement(IElement inner) : IElement
    {
        public string Name => inner.Name;
        public string InstanceType => inner.InstanceType;
        public object? Value => inner.Value;
        public string Location => inner.Location;
        public IType? Type => null;
        public bool HasPrimitiveValue => inner.HasPrimitiveValue;

        public IReadOnlyList<IElement> Children(string? name = null)
            => inner.Children(name).Select(c => (IElement)new NoTypeMetadataElement(c)).ToList();

        public T? Meta<T>() where T : class => null;
    }
}
