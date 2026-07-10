/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for FHIR-specific FhirPath functions.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FhirSpecificFunctionTests
{
    private const string ExtensionUrl = "http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject";

    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathParser _parser = new();
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public void GivenMatchingExtension_WhenHasExtensionEvaluated_ThenReturnsTrue()
    {
        var element = CreateQuestionnaireResponseElement(includeExtension: true);

        var result = Evaluate(element, $"item.hasExtension('{ExtensionUrl}')").Single();

        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenNonMatchingExtension_WhenHasExtensionEvaluated_ThenReturnsFalse()
    {
        var element = CreateQuestionnaireResponseElement(includeExtension: true);

        var result = Evaluate(element, "item.hasExtension('http://example.org/other')").Single();

        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenEmptyFocus_WhenHasExtensionEvaluated_ThenReturnsFalse()
    {
        var element = CreateQuestionnaireResponseElement(includeExtension: false);

        var result = Evaluate(element, $"item.answer.hasExtension('{ExtensionUrl}')").Single();

        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenQuestionnaireResponse_WhenFilteringByHasExtension_ThenReturnsSubjectReference()
    {
        var element = CreateQuestionnaireResponseElement(includeExtension: true);
        string expression = $"item.where(hasExtension('{ExtensionUrl}')).answer.value.ofType(Reference).reference";

        var result = Evaluate(element, expression).Single();

        result.Value.ShouldBe("Patient/123");
    }

    private IEnumerable<IElement> Evaluate(IElement element, string expression)
    {
        return _evaluator.Evaluate(element, _parser.Parse(expression), new EvaluationContext());
    }

    private IElement CreateQuestionnaireResponseElement(bool includeExtension)
    {
        string extension = includeExtension
            ? $$"""
              "extension": [
                {
                  "url": "{{ExtensionUrl}}",
                  "valueBoolean": true
                }
              ],
              """
            : string.Empty;

        string resourceJson = $$"""
        {
          "resourceType": "QuestionnaireResponse",
          "status": "completed",
          "item": [
            {
              {{extension}}
              "linkId": "subject",
              "answer": [
                {
                  "valueReference": {
                    "reference": "Patient/123"
                  }
                }
              ]
            }
          ]
        }
        """;

        return ResourceJsonNode.Parse(resourceJson).ToElement(_r4Provider);
    }
}
