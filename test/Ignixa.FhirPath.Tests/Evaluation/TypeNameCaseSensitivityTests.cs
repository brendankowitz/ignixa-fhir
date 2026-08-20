/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR R4/R4B explicitly allowed as() to cross the FHIR/System namespace boundary. R5 withdrew that
 * allowance, while HL7's shipped artifacts follow the same boundary. These tests pin both sides without
 * turning the legacy allowance into general case-insensitive identifier matching.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class TypeNameCaseSensitivityTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private const string StringObservationJson = """
    {
      "resourceType": "Observation",
      "id": "example",
      "status": "final",
      "code": { "text": "test" },
      "valueString": "typed"
    }
    """;

    private const string DateTimeObservationJson = """
    {
      "resourceType": "Observation",
      "id": "example",
      "status": "final",
      "code": { "text": "test" },
      "valueDateTime": "2024-06-15T08:00:00Z"
    }
    """;

    [Theory]
    [InlineData(FhirVersion.R5, "value as String")]
    [InlineData(FhirVersion.R5, "value.as(String)")]
    [InlineData(FhirVersion.R5, "value.ofType(String)")]
    [InlineData(FhirVersion.R6, "value as String")]
    [InlineData(FhirVersion.R6, "value.as(String)")]
    [InlineData(FhirVersion.R6, "value.ofType(String)")]
    public void GivenR5OrLater_WhenCastingAFhirStringWithTheSystemSpelling_ThenEmpty(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(StringObservationJson).ToElement(schema);

        // Act
        var result = Evaluate(element, expression, schema);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    public void GivenPreR5_WhenCastingAFhirStringWithTheCanonicalSystemSpelling_ThenTheValueSurvives(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(StringObservationJson).ToElement(schema);

        // Act
        var result = Evaluate(element, "value.as(String)", schema);

        // Assert
        result.ShouldHaveSingleItem().InstanceType.ShouldBe("string");
    }

    [Theory]
    [InlineData("DATETIME")]
    [InlineData("dAtEtImE")]
    public void GivenAnyPublishedVersion_WhenCastingWithArbitraryCasing_ThenItDoesNotMatch(string typeName)
    {
        foreach (FhirVersion version in PublishedVersions)
        {
            // Arrange
            var schema = version.GetSchemaProvider();
            var element = ResourceJsonNode.Parse(DateTimeObservationJson).ToElement(schema);

            // Act
            var result = Evaluate(element, $"value.as({typeName})", schema);

            // Assert
            result.ShouldBeEmpty($"FHIRPath identifiers are case-sensitive in {version}; only explicit pre-R5 aliases are permitted");
        }
    }

    [Fact]
    public void GivenStu3_WhenCastingUriWithTheShippedErratumSpelling_ThenTheValueSurvives()
    {
        // Uri is not a FHIRPath System type. This is the exact misspelling shipped by the STU3
        // ConceptMap-source-uri and ConceptMap-target-uri search parameters.

        // Arrange
        var schema = FhirVersion.Stu3.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(
            """{"resourceType":"ConceptMap","status":"draft","sourceUri":"http://example.org/source"}""")
            .ToElement(schema);

        // Act
        var result = Evaluate(element, "source.as(Uri)", schema);

        // Assert
        result.ShouldHaveSingleItem().InstanceType.ShouldBe("uri");
    }

    [Fact]
    public void GivenNoSchema_WhenCastingAFhirStringWithTheSystemSpelling_ThenMatchingFailsOpen()
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(StringObservationJson).ToElement(schema);

        // Act
        var result = Evaluate(element, "value.as(String)", schema: null);

        // Assert
        result.ShouldHaveSingleItem().InstanceType.ShouldBe("string");
    }

    [Theory]
    [InlineData("value.is(string)", true)]
    [InlineData("value.is(String)", false)]
    public void GivenR5_WhenTypeTestingAFhirString_ThenTheExistingNamespaceRuleIsUnchanged(
        string expression,
        bool expected)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(StringObservationJson).ToElement(schema);

        // Act
        var result = Evaluate(element, expression, schema);

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(expected);
    }

    private static IReadOnlyList<FhirVersion> PublishedVersions =>
        [FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6];

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema? schema) =>
        _evaluator.Evaluate(
            element,
            _parser.Parse(expression),
            new EvaluationContext { Resource = element, Schema = schema })
        .ToList();
}
