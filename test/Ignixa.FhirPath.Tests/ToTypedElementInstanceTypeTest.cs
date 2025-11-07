using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests;

/// <summary>
/// Test to verify that ToTypedElement() properly sets InstanceType for child elements.
/// This reproduces the issue found in SQL on FHIR where answer elements have InstanceType=null.
/// </summary>
public class ToTypedElementInstanceTypeTest
{
    private readonly FhirPathCompiler _compiler = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IStructureDefinitionSummaryProvider _r4Provider;

    public ToTypedElementInstanceTypeTest()
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

    [Fact]
    public void GivenQuestionnaireResponseWithAnswer_WhenNavigatingToAnswer_ThenAnswerHasInstanceType()
    {
        // Arrange: QuestionnaireResponse with item that has answer with valueString
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

        // Act: Navigate to item using FhirPath
        var items = EvaluatePath(typedElement, "item");
        var firstItem = items.First();

        // Assert: item should have InstanceType
        Assert.NotNull(firstItem);
        Assert.NotNull(firstItem.InstanceType);
        Assert.NotEmpty(firstItem.InstanceType);

        // Act: Navigate to answer using FhirPath
        var answers = EvaluatePath(firstItem, "answer");
        var firstAnswer = answers.First();

        // Assert: answer should have InstanceType (THIS IS THE BUG!)
        Assert.NotNull(firstAnswer);
        Assert.NotNull(firstAnswer.InstanceType); // This will likely fail
        Assert.NotEmpty(firstAnswer.InstanceType);

        // Additional debug info
        var answerChildren = firstAnswer.Children().ToList();
        Assert.NotEmpty(answerChildren); // Should have valueString child

        // The children exist, but can we navigate to them via FhirPath?
        var valueResults = EvaluatePath(firstAnswer, "value.ofType(string)");
        var valueList = valueResults.ToList();

        // This should work if InstanceType is set correctly
        Assert.Single(valueList);
        Assert.Equal("Answer 1.1", valueList[0].Value);
    }

    [Fact]
    public void GivenNestedItems_WhenNavigatingRecursively_ThenAllItemsHaveInstanceType()
    {
        // Arrange: QuestionnaireResponse with nested items (item.item.answer.item structure)
        var questionnaireResponseJson = """
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
                  "text": "Question 1.1",
                  "answer": [
                    {
                      "valueString": "Answer 1.1",
                      "item": [
                        {
                          "linkId": "1.1.1",
                          "text": "Follow-up to 1.1"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(questionnaireResponseJson);
        var typedElement = resource.ToTypedElement(_r4Provider);

        // Act: Navigate to first level item
        var level1Items = EvaluatePath(typedElement, "item");
        var level1Item = level1Items.First();

        // Assert: Level 1 item should have InstanceType
        Assert.NotNull(level1Item.InstanceType);
        Assert.NotEmpty(level1Item.InstanceType);

        // Act: Navigate to second level item (nested within first item)
        var level2Items = EvaluatePath(level1Item, "item").ToList();

        // DEBUG: Show what we got
        Console.WriteLine($"Level2 items count: {level2Items.Count}");
        Assert.NotEmpty(level2Items);

        var level2Item = level2Items.First();
        Console.WriteLine($"Level2 item: Name='{level2Item.Name}', InstanceType='{level2Item.InstanceType ?? "null"}', Definition={(level2Item.Definition != null ? $"ElementName={level2Item.Definition.ElementName}, Types={level2Item.Definition.Type?.Length ?? 0}" : "null")}");

        // Assert: Level 2 item should have InstanceType (THIS MIGHT FAIL!)
        Assert.NotNull(level2Item);
        Assert.NotNull(level2Item.InstanceType); // Check if this is null
        Assert.NotEmpty(level2Item.InstanceType);

        // Act: Navigate to answer and then nested item within answer
        var answers = EvaluatePath(level2Item, "answer");
        var answer = answers.First();

        Assert.NotNull(answer.InstanceType); // answer should have InstanceType

        var level3Items = EvaluatePath(answer, "item");
        var level3Item = level3Items.First();

        // Assert: Level 3 item (inside answer) should have InstanceType (THIS MIGHT BE NULL!)
        Assert.NotNull(level3Item);
        Assert.NotNull(level3Item.InstanceType);
        Assert.NotEmpty(level3Item.InstanceType);
    }
}
