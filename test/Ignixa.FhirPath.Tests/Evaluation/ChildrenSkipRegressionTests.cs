/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for GitHub issue #205:
 * children().skip() corrupts arrays by converting them to objects.
 *
 * The bug is in SerializeComplexElement: when building JsonObject from children,
 * it only creates arrays when it encounters duplicate child names. If an array
 * has only one element, it becomes a plain object property instead of a JsonArray.
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Regression tests for GitHub issue #205:
/// children().skip() corrupts arrays by converting them to objects.
/// </summary>
public class ChildrenSkipRegressionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _r4Provider;

    public ChildrenSkipRegressionTests()
    {
        _r4Provider = FhirVersion.R4.GetSchemaProvider();
    }

    private IEnumerable<IElement> EvaluatePath(IElement element, string pathExpression)
    {
        var expression = _parser.Parse(pathExpression);
        return _evaluator.Evaluate(element, expression, new EvaluationContext());
    }

    [Fact]
    public void GivenPatientWithIdentifierContainingCodingArray_WhenChildrenSkip_ThenCodingRemainsArray()
    {
        // Arrange - Patient with identifier.type.coding containing a single-element array
        var json = """
        {
          "resourceType": "Patient",
          "id": "test-patient",
          "identifier": [
            {
              "system": "http://example.org/ids",
              "value": "12345",
              "type": {
                "coding": [
                  {
                    "system": "http://terminology.hl7.org/CodeSystem/v2-0203",
                    "code": "MR",
                    "display": "Medical Record Number"
                  }
                ]
              }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Navigate to identifier using children().skip(1)
        // This skips "id" and gets "identifier" as the second child
        var results = EvaluatePath(element, "children().skip(1)").ToList();

        // Assert - children().skip(1) flattens arrays, so we get individual items
        // We should get one or more identifier elements
        Assert.NotEmpty(results);
        var identifierElement = results.First(r => r.Name == "identifier");

        // Navigate to type.coding - should still be an array
        var typeElements = identifierElement.Children("type").ToList();
        Assert.Single(typeElements);
        var typeElement = typeElements[0];

        var codingElements = typeElement.Children("coding").ToList();
        Assert.Single(codingElements);
        var codingElement = codingElements[0];

        // CRITICAL: Get the JsonNode of type element and verify coding is a JsonArray
        var typeNode = typeElement.Meta<JsonNode>();
        Assert.NotNull(typeNode);
        Assert.IsType<JsonObject>(typeNode);

        var typeObj = (JsonObject)typeNode;
        Assert.True(typeObj.ContainsKey("coding"));

        // BUG: This will fail if SerializeComplexElement corrupted the array
        var codingNode = typeObj["coding"];
        Assert.NotNull(codingNode);
        Assert.IsType<JsonArray>(codingNode);

        var codingArray = (JsonArray)codingNode;
        Assert.Single(codingArray);
    }

    [Fact]
    public void GivenPatientWithSingleElementNameArray_WhenChildrenSkip_ThenGivenPreservesArrayStructure()
    {
        // Arrange - Patient with name containing a single-element "given" array
        var json = """
        {
          "resourceType": "Patient",
          "id": "test-patient",
          "name": [
            {
              "family": "Smith",
              "given": ["Jane"]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Use children().skip(1) to get name (skipping id)
        var results = EvaluatePath(element, "children().skip(1)").ToList();

        // Assert - Verify we got name element (children().skip() flattens arrays)
        Assert.NotEmpty(results);
        var nameElement = results.First(r => r.Name == "name");

        // Get the given element
        var givenElements = nameElement.Children("given").ToList();
        Assert.Single(givenElements);

        // CRITICAL: Verify the name item's JsonNode has "given" as JsonArray
        var nameItemNode = nameElement.Meta<JsonNode>();
        Assert.NotNull(nameItemNode);
        Assert.IsType<JsonObject>(nameItemNode);

        var nameItemObj = (JsonObject)nameItemNode;
        Assert.True(nameItemObj.ContainsKey("given"));

        // BUG: This will fail if SerializeComplexElement corrupted the array
        var givenNode = nameItemObj["given"];
        Assert.NotNull(givenNode);
        Assert.IsType<JsonArray>(givenNode);

        var givenArray = (JsonArray)givenNode;
        Assert.Single(givenArray);
        Assert.Equal("Jane", givenArray[0]?.GetValue<string>());
    }

    [Fact]
    public void GivenPatientWithMultipleIdentifiers_WhenChildrenSkip_ThenAllArrayStructuresPreserved()
    {
        // Arrange - Patient with multiple identifiers, each with single-element coding arrays
        var json = """
        {
          "resourceType": "Patient",
          "id": "test-patient",
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
            },
            {
              "system": "http://example.org/ssn",
              "value": "987-65-4321",
              "type": {
                "coding": [
                  {
                    "system": "http://terminology.hl7.org/CodeSystem/v2-0203",
                    "code": "SS"
                  }
                ]
              }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Use children().skip(1) to get identifier elements (flattened from array)
        var results = EvaluatePath(element, "children().skip(1)").ToList();

        // Assert - children().skip() flattens arrays, so we get individual identifier elements
        Assert.NotEmpty(results);
        var identifierElements = results.Where(r => r.Name == "identifier").ToList();
        Assert.Equal(2, identifierElements.Count);

        // Check each identifier item's type.coding is still an array
        foreach (var identifier in identifierElements)
        {
            var typeElements = identifier.Children("type").ToList();
            Assert.Single(typeElements);
            var typeElement = typeElements[0];

            // Get the type's JsonNode
            var typeNode = typeElement.Meta<JsonNode>();
            Assert.IsType<JsonObject>(typeNode);

            var typeObj = (JsonObject)typeNode;
            Assert.True(typeObj.ContainsKey("coding"));

            // BUG: Each coding should be a JsonArray
            var codingNode = typeObj["coding"];
            Assert.NotNull(codingNode);
            Assert.IsType<JsonArray>(codingNode);
        }
    }

    [Fact]
    public void GivenObservationWithComponentContainingCodeCoding_WhenChildrenSkip_ThenCodingArrayPreserved()
    {
        // Arrange - Observation with component that has code.coding as single-element array
        var json = """
        {
          "resourceType": "Observation",
          "id": "bp-reading",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "85354-9",
                "display": "Blood pressure panel"
              }
            ]
          },
          "component": [
            {
              "code": {
                "coding": [
                  {
                    "system": "http://loinc.org",
                    "code": "8480-6",
                    "display": "Systolic blood pressure"
                  }
                ]
              },
              "valueQuantity": {
                "value": 120,
                "unit": "mmHg"
              }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Use children().skip(2) to get elements after id and status
        var skippedResults = EvaluatePath(element, "children().skip(2)").ToList();
        Assert.NotEmpty(skippedResults);
        var codeElement = skippedResults.First(r => r.Name == "code");

        // Verify code.coding is still an array
        var codeNode = codeElement.Meta<JsonNode>();
        Assert.IsType<JsonObject>(codeNode);
        var codeObj = (JsonObject)codeNode;
        Assert.True(codeObj.ContainsKey("coding"));
        Assert.IsType<JsonArray>(codeObj["coding"]);

        // Act - Navigate to component using FHIRPath
        var componentResults = EvaluatePath(element, "component").ToList();
        Assert.NotEmpty(componentResults);
        var componentElement = componentResults.First();

        // Check component.code.coding is still an array
        var componentCodeElements = componentElement.Children("code").ToList();
        Assert.Single(componentCodeElements);
        var componentCodeElement = componentCodeElements[0];

        var componentCodeNode = componentCodeElement.Meta<JsonNode>();
        Assert.IsType<JsonObject>(componentCodeNode);
        var componentCodeObj = (JsonObject)componentCodeNode;
        Assert.True(componentCodeObj.ContainsKey("coding"));

        // BUG: This should be JsonArray, not JsonObject
        var componentCodingNode = componentCodeObj["coding"];
        Assert.NotNull(componentCodingNode);
        Assert.IsType<JsonArray>(componentCodingNode);
    }

    [Fact]
    public void GivenNestedArraysWithSingleElements_WhenMultipleChildrenSkipOperations_ThenAllArraysPreserved()
    {
        // Arrange - Complex nested structure with multiple single-element arrays
        var json = """
        {
          "resourceType": "Patient",
          "id": "complex-test",
          "identifier": [
            {
              "system": "http://example.org/ids",
              "value": "ABC123",
              "type": {
                "coding": [
                  {
                    "system": "http://terminology.hl7.org/CodeSystem/v2-0203",
                    "code": "MR"
                  }
                ],
                "text": "Medical Record"
              }
            }
          ],
          "contact": [
            {
              "relationship": [
                {
                  "coding": [
                    {
                      "system": "http://terminology.hl7.org/CodeSystem/v2-0131",
                      "code": "E"
                    }
                  ]
                }
              ],
              "name": {
                "family": "Emergency",
                "given": ["Contact"]
              }
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Navigate through children().skip() chain
        var afterSkip1 = EvaluatePath(element, "children().skip(1)").ToList();
        Assert.NotEmpty(afterSkip1);

        // For each element returned, verify all nested arrays are preserved
        foreach (var childElement in afterSkip1)
        {
            var childNode = childElement.Meta<JsonNode>();
            Assert.NotNull(childNode);

            // If it's an array, check its items
            if (childNode is JsonArray jsonArray)
            {
                foreach (var arrayItem in jsonArray)
                {
                    if (arrayItem is JsonObject obj)
                    {
                        VerifyNoCorruptedArrays(obj);
                    }
                }
            }
        }

        // Act - Also test contact specifically
        var contactResults = EvaluatePath(element, "contact").ToList();
        Assert.Single(contactResults);
        var contactElement = contactResults[0];

        // contactElement is the individual contact item (array flattened by FHIRPath)
        var contactNode = contactElement.Meta<JsonNode>();
        Assert.IsType<JsonObject>(contactNode);

        // Verify relationship is still an array within the contact
        var relationshipElements = contactElement.Children("relationship").ToList();
        Assert.Single(relationshipElements);
        var relationshipElement = relationshipElements[0];

        // relationshipElement is also the individual item (flattened)
        var relationshipNode = relationshipElement.Meta<JsonNode>();
        Assert.IsType<JsonObject>(relationshipNode);

        // But within the contact JsonObject, relationship should be stored as JsonArray
        var contactObj = (JsonObject)contactNode;
        Assert.True(contactObj.ContainsKey("relationship"));
        var relationshipProperty = contactObj["relationship"];
        Assert.NotNull(relationshipProperty);
        Assert.IsType<JsonArray>(relationshipProperty);
    }

    [Fact]
    public void GivenSingleElementArray_WhenAccessedViaChildrenSkip_ThenArrayStructureIsCorrupted()
    {
        // This test explicitly demonstrates the bug in issue #205.
        // When SerializeComplexElement processes an element with a single-element array property,
        // it doesn't create a JsonArray - it creates a plain property instead.

        // Arrange - Simple patient with contact.relationship having ONE element
        var json = """
        {
          "resourceType": "Patient",
          "id": "bug-demo",
          "contact": [
            {
              "name": {
                "family": "Emergency"
              },
              "relationship": [
                {
                  "coding": [
                    {
                      "system": "http://terminology.hl7.org/CodeSystem/v2-0131",
                      "code": "E"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Navigate to contact via children().skip() and examine the relationship property
        var contactResults = EvaluatePath(element, "contact").ToList();
        Assert.Single(contactResults);
        var contactElement = contactResults[0];

        var contactNode = contactElement.Meta<JsonNode>();
        Assert.IsType<JsonObject>(contactNode);
        var contactObj = (JsonObject)contactNode;

        // Assert - BUG: relationship should be a JsonArray, but it's corrupted to JsonObject
        Assert.True(contactObj.ContainsKey("relationship"));
        var relationshipProperty = contactObj["relationship"];
        Assert.NotNull(relationshipProperty);

        // This assertion WILL FAIL until the bug is fixed:
        // Expected: JsonArray (because FHIR spec says relationship is 0..*)
        // Actual: JsonObject (because SerializeComplexElement only creates arrays for duplicate names)
        Assert.IsType<JsonArray>(relationshipProperty);
    }

    /// <summary>
    /// Helper method to recursively verify no arrays were corrupted into objects.
    /// Checks that properties expected to be arrays (coding, given, relationship, etc.)
    /// are actually JsonArray instances.
    /// </summary>
    private void VerifyNoCorruptedArrays(JsonObject obj)
    {
        var knownArrayProperties = new[] { "coding", "given", "relationship", "identifier", "contact", "name", "telecom", "address" };

        foreach (var kvp in obj)
        {
            if (knownArrayProperties.Contains(kvp.Key))
            {
                // These should always be arrays in FHIR (even with single elements)
                if (kvp.Value is not null)
                {
                    Assert.IsType<JsonArray>(kvp.Value);
                }
            }

            // Recurse into nested objects
            if (kvp.Value is JsonObject nestedObj)
            {
                VerifyNoCorruptedArrays(nestedObj);
            }
            else if (kvp.Value is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonObject itemObj)
                    {
                        VerifyNoCorruptedArrays(itemObj);
                    }
                }
            }
        }
    }
}
