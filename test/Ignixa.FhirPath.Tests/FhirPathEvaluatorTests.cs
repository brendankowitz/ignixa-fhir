/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath expression evaluation, focusing on ofType() function
 * and choice type navigation per FHIR FHIRPath specification.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests;

/// <summary>
/// Tests for FhirPath expression evaluation with real FHIR resources.
/// Focuses on choice type navigation and ofType() filtering per spec.
/// </summary>
public class FhirPathEvaluatorTests
{
    private readonly FhirPathCompiler _compiler = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IStructureDefinitionSummaryProvider _r4Provider;

    public FhirPathEvaluatorTests()
    {
        _r4Provider = FhirSpecification.R4.GetSchemaProvider();
    }

    /// <summary>
    /// Helper method to evaluate a FHIRPath expression string against a typed element.
    /// </summary>
    private IEnumerable<ITypedElement> EvaluatePath(ITypedElement element, string pathExpression)
    {
        var expression = _compiler.Parse(pathExpression);
        return _evaluator.Evaluate(element, expression, new EvaluationContext());
    }

    #region Choice Type Navigation Tests

    /// <summary>
    /// Test that navigating to 'value' on Observation matches choice type element 'valueString'.
    /// Per FHIR spec: "choice elements are labeled according to the name without the '[x]' suffix"
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenNavigatingToValue_ThenReturnsValueStringElement()
    {
        // Arrange: Observation with valueString
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value' (not 'valueString')
        var result = EvaluatePath(typedElement, "value");

        // Assert: Should find the valueString element
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("foo", resultList[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' on Observation matches choice type element 'valueInteger'.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueInteger_WhenNavigatingToValue_ThenReturnsValueIntegerElement()
    {
        // Arrange: Observation with valueInteger
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value'
        var result = EvaluatePath(typedElement, "value");

        // Assert: Should find the valueInteger element
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal(42, resultList[0].Value);
    }

    /// <summary>
    /// Test that navigating to 'value' on Observation with no value returns empty collection.
    /// </summary>
    [Fact]
    public void GivenObservationWithoutValue_WhenNavigatingToValue_ThenReturnsEmpty()
    {
        // Arrange: Observation without value[x]
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs3",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value'
        var result = EvaluatePath(typedElement, "value");

        // Assert: Should return empty collection
        Assert.Empty(result);
    }

    #endregion

    #region ofType() Function Tests

    /// <summary>
    /// Test ofType(string) filters choice type value to only string-typed elements.
    /// This is the core test case from SQL on FHIR spec tests (fn_oftype.json).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenFilteringWithOfTypeString_ThenReturnsValue()
    {
        // Arrange: Observation with valueString
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(string)'
        var result = EvaluatePath(typedElement, "value.ofType(string)");

        // Assert: Should return the string value
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("foo", resultList[0].Value);
    }

    /// <summary>
    /// Test ofType(integer) filters choice type value to only integer-typed elements.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueInteger_WhenFilteringWithOfTypeInteger_ThenReturnsValue()
    {
        // Arrange: Observation with valueInteger
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(integer)'
        var result = EvaluatePath(typedElement, "value.ofType(integer)");

        // Assert: Should return the integer value
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal(42, resultList[0].Value);
    }

    /// <summary>
    /// Test ofType(string) on valueInteger returns empty (type mismatch).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueInteger_WhenFilteringWithOfTypeString_ThenReturnsEmpty()
    {
        // Arrange: Observation with valueInteger
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(string)' (type mismatch)
        var result = EvaluatePath(typedElement, "value.ofType(string)");

        // Assert: Should return empty collection
        Assert.Empty(result);
    }

    /// <summary>
    /// Test ofType(integer) on valueString returns empty (type mismatch).
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenFilteringWithOfTypeInteger_ThenReturnsEmpty()
    {
        // Arrange: Observation with valueString
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(integer)' (type mismatch)
        var result = EvaluatePath(typedElement, "value.ofType(integer)");

        // Assert: Should return empty collection
        Assert.Empty(result);
    }

    /// <summary>
    /// Test ofType() is case-insensitive per FHIRPath specification.
    /// Both 'String' and 'string' should work identically.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueString_WhenFilteringWithOfTypeCapitalizedString_ThenReturnsValue()
    {
        // Arrange: Observation with valueString
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(String)' with capital S
        var result = EvaluatePath(typedElement, "value.ofType(String)");

        // Assert: Should return the string value (case-insensitive matching)
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("foo", resultList[0].Value);
    }

    /// <summary>
    /// Test ofType() on empty collection returns empty.
    /// </summary>
    [Fact]
    public void GivenObservationWithoutValue_WhenFilteringWithOfType_ThenReturnsEmpty()
    {
        // Arrange: Observation without value[x]
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs3",
          "status": "final",
          "code": { "text": "test" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(string)' on missing value
        var result = EvaluatePath(typedElement, "value.ofType(string)");

        // Assert: Should return empty collection
        Assert.Empty(result);
    }

    /// <summary>
    /// Test ofType(string) on QuestionnaireResponse answer.value[x] choice type.
    /// This reproduces the issue found in SQL on FHIR repeat tests where
    /// answer.value.ofType(string) was returning empty instead of finding valueString.
    /// Related to: SQL on FHIR test "repeat/combined with forEach"
    /// </summary>
    [Fact]
    public void GivenQuestionnaireResponseAnswer_WhenFilteringAnswerValueWithOfTypeString_ThenReturnsValue()
    {
        // Arrange: QuestionnaireResponse with item that has answer with valueString
        // This is the exact structure from SQL on FHIR repeat test that was failing
        var questionnaireResponseJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1.1",
              "text": "Question 1.1",
              "answer": [
                {
                  "valueString": "Answer 1.1"
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(questionnaireResponseJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to answer and evaluate 'value.ofType(string)'
        var items = EvaluatePath(typedElement, "item");
        var firstItem = items.First();
        var answers = EvaluatePath(firstItem, "answer");
        var firstAnswer = answers.First();
        var result = EvaluatePath(firstAnswer, "value.ofType(string)");

        // Assert: Should return the string value "Answer 1.1"
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("Answer 1.1", resultList[0].Value);
    }

    #endregion

    #region Complex Type ofType() Tests

    /// <summary>
    /// Test ofType() with complex type (Quantity) on choice element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueQuantity_WhenFilteringWithOfTypeQuantity_ThenReturnsValue()
    {
        // Arrange: Observation with valueQuantity
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs4",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": {
            "value": 185,
            "unit": "cm",
            "system": "http://unitsofmeasure.org",
            "code": "cm"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(Quantity)'
        var result = EvaluatePath(typedElement, "value.ofType(Quantity)");

        // Assert: Should return the Quantity element
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("Quantity", resultList[0].InstanceType);
    }

    /// <summary>
    /// Test ofType() with CodeableConcept on choice element.
    /// </summary>
    [Fact]
    public void GivenObservationWithValueCodeableConcept_WhenFilteringWithOfTypeCodeableConcept_ThenReturnsValue()
    {
        // Arrange: Observation with valueCodeableConcept
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs5",
          "status": "final",
          "code": { "text": "test" },
          "valueCodeableConcept": {
            "coding": [{
              "system": "http://snomed.info/sct",
              "code": "439401001",
              "display": "Diagnosis"
            }]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'value.ofType(CodeableConcept)'
        var result = EvaluatePath(typedElement, "value.ofType(CodeableConcept)");

        // Assert: Should return the CodeableConcept element
        var resultList = result.ToList();
        Assert.Single(resultList);
        Assert.Equal("CodeableConcept", resultList[0].InstanceType);
    }

    #endregion

    #region Multiple Values Tests

    /// <summary>
    /// Test ofType() filters collections with multiple types correctly.
    /// Example: Observation.component with mixed value types.
    /// </summary>
    [Fact]
    public void GivenObservationWithMultipleComponents_WhenFilteringComponentValuesByType_ThenReturnsMatchingOnly()
    {
        // Arrange: Observation with multiple components having different value types
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs6",
          "status": "final",
          "code": { "text": "test" },
          "component": [
            {
              "code": { "text": "comp1" },
              "valueString": "text"
            },
            {
              "code": { "text": "comp2" },
              "valueInteger": 10
            },
            {
              "code": { "text": "comp3" },
              "valueString": "more text"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Evaluate 'component.value.ofType(string)'
        var result = EvaluatePath(typedElement, "component.value.ofType(string)");

        // Assert: Should return only the two string values
        var resultList = result.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.All(resultList, r => Assert.Equal("string", r.InstanceType));

        var values = resultList.Select(r => r.Value?.ToString()).ToList();
        Assert.Contains("text", values);
        Assert.Contains("more text", values);
    }

    #endregion

    #region InstanceType Property Tests

    /// <summary>
    /// Test that InstanceType property is correctly set for primitive choice elements.
    /// This validates the type name normalization fix in TypedElementOnSourceNode.
    /// </summary>
    [Fact]
    public void GivenValueStringElement_WhenCheckingInstanceType_ThenReturnsLowercaseString()
    {
        // Arrange: Observation with valueString
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "foo"
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value' and check InstanceType
        var valueElements = typedElement.Children("value").ToList();

        // Assert: InstanceType should be lowercase "string" not "String"
        Assert.Single(valueElements);
        Assert.Equal("string", valueElements[0].InstanceType);
    }

    /// <summary>
    /// Test that InstanceType property is correctly set for integer choice elements.
    /// </summary>
    [Fact]
    public void GivenValueIntegerElement_WhenCheckingInstanceType_ThenReturnsLowercaseInteger()
    {
        // Arrange: Observation with valueInteger
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs2",
          "status": "final",
          "code": { "text": "test" },
          "valueInteger": 42
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value' and check InstanceType
        var valueElements = typedElement.Children("value").ToList();

        // Assert: InstanceType should be lowercase "integer" not "Integer"
        Assert.Single(valueElements);
        Assert.Equal("integer", valueElements[0].InstanceType);
    }

    /// <summary>
    /// Test that InstanceType property for complex types remains capitalized.
    /// </summary>
    [Fact]
    public void GivenValueQuantityElement_WhenCheckingInstanceType_ThenReturnsCapitalizedQuantity()
    {
        // Arrange: Observation with valueQuantity
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs4",
          "status": "final",
          "code": { "text": "test" },
          "valueQuantity": {
            "value": 185,
            "unit": "cm"
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'value' and check InstanceType
        var valueElements = typedElement.Children("value").ToList();

        // Assert: InstanceType should remain capitalized "Quantity"
        Assert.Single(valueElements);
        Assert.Equal("Quantity", valueElements[0].InstanceType);
    }

    #endregion

    #region QuestionnaireResponse Item Navigation Tests

    /// <summary>
    /// Test that navigating to 'item' on QuestionnaireResponse returns top-level items.
    /// This verifies basic array navigation works correctly.
    /// </summary>
    [Fact]
    public void GivenQuestionnaireResponseWithItems_WhenNavigatingToItem_ThenReturnsTopLevelItems()
    {
        // Arrange: QuestionnaireResponse with 2 top-level items
        var qrJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1",
              "text": "Group 1"
            },
            {
              "linkId": "2",
              "text": "Group 2"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(qrJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to 'item'
        var result = EvaluatePath(typedElement, "item");

        // Assert: Should return 2 items
        var resultList = result.ToList();
        Assert.Equal(2, resultList.Count);

        // Verify linkIds
        var linkIds = resultList.SelectMany(item => EvaluatePath(item, "linkId"))
                                .Select(id => id.Value?.ToString())
                                .ToList();
        Assert.Contains("1", linkIds);
        Assert.Contains("2", linkIds);
    }

    /// <summary>
    /// Test that navigating to nested items works - item.item returns nested items.
    /// This tests recursive navigation which is needed for SQL on FHIR repeat directive.
    /// </summary>
    [Fact]
    public void GivenQuestionnaireResponseWithNestedItems_WhenNavigatingToItemItem_ThenReturnsNestedItems()
    {
        // Arrange: QuestionnaireResponse with nested structure
        var qrJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1",
              "text": "Group 1",
              "item": [
                {
                  "linkId": "1.1",
                  "text": "Question 1.1"
                },
                {
                  "linkId": "1.2",
                  "text": "Question 1.2"
                }
              ]
            },
            {
              "linkId": "2",
              "text": "Group 2"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(qrJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to nested items using item.item
        var result = EvaluatePath(typedElement, "item.item");

        // Assert: Should return 2 nested items (1.1 and 1.2)
        var resultList = result.ToList();
        Assert.Equal(2, resultList.Count);

        // Verify linkIds of nested items
        var linkIds = resultList.SelectMany(item => EvaluatePath(item, "linkId"))
                                .Select(id => id.Value?.ToString())
                                .ToList();
        Assert.Contains("1.1", linkIds);
        Assert.Contains("1.2", linkIds);
    }

    /// <summary>
    /// Test that evaluating 'item' on a single item element returns its nested items.
    /// This simulates what happens during recursive traversal in repeat directive.
    /// </summary>
    [Fact]
    public void GivenSingleItemElement_WhenNavigatingToItem_ThenReturnsItsNestedItems()
    {
        // Arrange: QuestionnaireResponse with nested items
        var qrJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1",
              "text": "Group 1",
              "item": [
                {
                  "linkId": "1.1",
                  "text": "Question 1.1"
                },
                {
                  "linkId": "1.2",
                  "text": "Question 1.2",
                  "item": [
                    {
                      "linkId": "1.2.1",
                      "text": "Question 1.2.1"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(qrJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Get first top-level item, then navigate to its nested items
        var firstItem = EvaluatePath(typedElement, "item").First();
        var nestedItems = EvaluatePath(firstItem, "item");

        // Assert: Should return 2 nested items (1.1 and 1.2)
        var nestedList = nestedItems.ToList();
        Assert.Equal(2, nestedList.Count);

        // Verify we can further navigate from the second nested item
        var secondNestedItem = nestedList[1];
        var deeplyNestedItems = EvaluatePath(secondNestedItem, "item");

        var deepList = deeplyNestedItems.ToList();
        Assert.Single(deepList);

        var deepLinkId = EvaluatePath(deepList[0], "linkId").First().Value?.ToString();
        Assert.Equal("1.2.1", deepLinkId);
    }

    /// <summary>
    /// Test that empty item arrays return empty collections.
    /// This ensures repeat directive correctly handles items without nested children.
    /// </summary>
    [Fact]
    public void GivenItemWithoutNestedItems_WhenNavigatingToItem_ThenReturnsEmpty()
    {
        // Arrange: QuestionnaireResponse with item that has no nested items
        var qrJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1",
              "text": "Group 1"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(qrJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Get first item and try to navigate to its (non-existent) nested items
        var firstItem = EvaluatePath(typedElement, "item").First();
        var nestedItems = EvaluatePath(firstItem, "item");

        // Assert: Should return empty collection
        Assert.Empty(nestedItems);
    }

    /// <summary>
    /// Test comprehensive recursive traversal similar to what repeat directive does.
    /// This test manually implements breadth-first traversal to verify all items can be reached.
    /// </summary>
    [Fact]
    public void GivenQuestionnaireResponseWithDeepNesting_WhenManuallyTraversingAllItems_ThenFindsAllItems()
    {
        // Arrange: QuestionnaireResponse matching the SQL on FHIR test data
        var qrJson = """
        {
          "resourceType": "QuestionnaireResponse",
          "id": "qr1",
          "item": [
            {
              "linkId": "1",
              "text": "Group 1",
              "item": [
                {
                  "linkId": "1.1",
                  "text": "Question 1.1"
                },
                {
                  "linkId": "1.2",
                  "text": "Question 1.2",
                  "item": [
                    {
                      "linkId": "1.2.1",
                      "text": "Question 1.2.1"
                    }
                  ]
                }
              ]
            },
            {
              "linkId": "2",
              "text": "Group 2"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(qrJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Manually implement breadth-first traversal using 'item' path
        var allItems = new List<ITypedElement>();
        var queue = new Queue<ITypedElement>();

        // Start with root.item
        foreach (var item in EvaluatePath(typedElement, "item"))
        {
            queue.Enqueue(item);
        }

        // Breadth-first traversal
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            allItems.Add(current);

            // Follow 'item' path from current element
            foreach (var child in EvaluatePath(current, "item"))
            {
                queue.Enqueue(child);
            }
        }

        // Assert: Should find all 5 items (1, 1.1, 1.2, 1.2.1, 2)
        Assert.Equal(5, allItems.Count);

        var linkIds = allItems.SelectMany(item => EvaluatePath(item, "linkId"))
                              .Select(id => id.Value?.ToString())
                              .ToList();

        Assert.Contains("1", linkIds);
        Assert.Contains("1.1", linkIds);
        Assert.Contains("1.2", linkIds);
        Assert.Contains("1.2.1", linkIds);
        Assert.Contains("2", linkIds);
    }

    #endregion
}
