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

    /// <summary>
    /// A mis-cased type identifier yields empty rather than an error, on every published version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a deliberate engine choice, not an accident of the comparers, and the empty result is
    /// knowingly more lenient than the specification. FHIRPath says an identifier that cannot be
    /// resolved is an error, and <c>DATETIME</c> is not a valid identifier in any FHIR model - so a
    /// strictly conformant engine would throw here, exactly as <c>as(long)</c> already does.
    /// </para>
    /// <para>
    /// It does not, because resolution and matching use different comparers by design. Matching is
    /// <see cref="StringComparer.Ordinal"/>, so no amount of casing will make <c>DATETIME</c> select a
    /// <c>dateTime</c>; resolution runs through <c>ISchema.IsKnownType</c>, which the generated schema
    /// providers back with <see cref="StringComparer.OrdinalIgnoreCase"/>, so the identifier resolves
    /// and the cast simply selects nothing. Making the two agree means tightening the generated
    /// providers' lookup, which is a schema-wide decision affecting every caller of
    /// <c>IsKnownType</c> - not a FHIRPath one - and is out of scope for the type-alias work this file
    /// pins. Until that decision is taken, empty is the contract.
    /// </para>
    /// <para>
    /// If a later change tightens those providers to ordinal, this test will start throwing. That is
    /// the conformant answer and the expectation should be updated to it deliberately, not worked
    /// around.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(FhirVersion.Stu3, "DATETIME")]
    [InlineData(FhirVersion.Stu3, "dAtEtImE")]
    [InlineData(FhirVersion.R4, "DATETIME")]
    [InlineData(FhirVersion.R4, "dAtEtImE")]
    [InlineData(FhirVersion.R4B, "DATETIME")]
    [InlineData(FhirVersion.R4B, "dAtEtImE")]
    [InlineData(FhirVersion.R5, "DATETIME")]
    [InlineData(FhirVersion.R5, "dAtEtImE")]
    [InlineData(FhirVersion.R6, "DATETIME")]
    [InlineData(FhirVersion.R6, "dAtEtImE")]
    public void GivenAnyPublishedVersion_WhenCastingWithArbitraryCasing_ThenItDoesNotMatch(
        FhirVersion version,
        string typeName)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(DateTimeObservationJson).ToElement(schema);

        // Act
        var result = Evaluate(element, $"value.as({typeName})", schema);

        // Assert
        result.ShouldBeEmpty($"FHIRPath identifiers are case-sensitive in {version}; only explicit pre-R5 aliases are permitted");
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

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema? schema) =>
        _evaluator.Evaluate(
            element,
            _parser.Parse(expression),
            new EvaluationContext { Resource = element, Schema = schema })
        .ToList();
}
