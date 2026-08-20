/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The two errors FHIRPath mandates for 'is', and the one case it deliberately does not.
 *
 * "If the identifier cannot be resolved to a valid type identifier, the evaluator will throw an error.
 * If the input collections contains more than one item, the evaluator will throw an error." That text is
 * byte-identical in FHIRPath N1 6.3.1 and the 3.0.0 build, so it is normative for every published FHIR
 * version. The operator enforced neither: it returned empty for a multi-item input and never checked the
 * identifier, while is() the function threw on the first and ignored the second. Same question, two
 * answers, depending on spelling.
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

public class TypeTestOperatorErrorTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _schemaProvider = FhirVersion.R4.GetSchemaProvider();

    private const string PatientJson = """
    {"resourceType": "Patient", "id": "example", "active": true, "gender": "male"}
    """;

    [Theory]
    [InlineData("gender is NotAType")]
    [InlineData("gender.is(NotAType)")]
    public void GivenAnUnresolvableTypeIdentifier_WhenTypeTested_ThenThrows(string expression)
    {
        // Checked before the data is consulted, exactly as 'as' does: whether an identifier names a type
        // is a fact about the expression, so deferring it would let a typo lurk until some resource
        // happened to populate the path.

        // Arrange
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(_schemaProvider);
        var expr = _parser.Parse(expression);
        var context = new EvaluationContext { Schema = _schemaProvider };

        // Act & Assert
        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(element, expr, context).ToList());
        exception.Message.ShouldContain("NotAType");
    }

    [Theory]
    [InlineData("nonexistent is Patient")]
    [InlineData("nonexistent.is(Patient)")]
    public void GivenEmptyInput_WhenTypeTested_ThenEmptyRatherThanFalse(string expression)
    {
        // Deliberate, and the specs genuinely disagree. FHIRPath N1 (2.0.0) - the version every published
        // FHIR release normatively references - ends the paragraph "In all other cases this operator
        // returns the empty collection". The 3.0.0 build changed that same sentence to "returns false".
        // Empty is what N1 and both reference engines do, so empty is what we do; treat the build's
        // "false" as drift, not as a bug report against this test.

        // Arrange
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(_schemaProvider);
        var expr = _parser.Parse(expression);
        var context = new EvaluationContext { Schema = _schemaProvider };

        // Act
        var result = _evaluator.Evaluate(element, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("name.given is string")]
    [InlineData("name.given.is(string)")]
    public void GivenARepeatingPath_WhenTypeTested_ThenThrowsRegardlessOfSpelling(string expression)
    {
        // A repeating path is the shape that actually reaches this in practice, as opposed to the
        // synthetic (1 | 2 | 3) unions asserted in EdgeCaseAndErrorTests.

        // Arrange
        var json = """
        {
          "resourceType": "Patient",
          "id": "example",
          "name": [{"family": "Smith", "given": ["John", "Jacob"]}]
        }
        """;
        var element = ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
        var expr = _parser.Parse(expression);
        var context = new EvaluationContext { Schema = _schemaProvider };

        // Act & Assert
        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(element, expr, context).ToList());
        exception.Message.ShouldContain("single item");
    }

    [Fact]
    public void GivenNoSchema_WhenTypeTestedAgainstAnUnknownIdentifier_ThenDoesNotThrow()
    {
        // EvaluationContext.Schema is optional, and with no model there is no table to fail a lookup
        // against. Treating "we were given no model" as "the identifier is wrong" would reject valid
        // expressions, so the identifier check fails open - the same stance 'as' and ofType() take.

        // Arrange
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(_schemaProvider);
        var expr = _parser.Parse("gender is NotAType");

        // Act
        var result = _evaluator.Evaluate(element, expr, new EvaluationContext()).ToList();

        // Assert
        result.Single().Value.ShouldBe(false);
    }

    [Theory]
    [InlineData("Condition.abatement.is(dateTime)")]
    [InlineData("Condition.abatement.is(Age)")]
    public void GivenTheOnlyShippedIsFunctionExpression_WhenEvaluated_ThenItStillEvaluates(string expression)
    {
        // STU3's Condition-abatement-boolean is the single SearchParameter in any version that spells a
        // type test with the function form. abatement[x] is 0..1, so the singleton rule cannot bite - and
        // this asserts that, because it is the concrete artifact the decision not to version-gate 'is'
        // was justified against.

        // Arrange
        var json = """
        {
          "resourceType": "Condition",
          "id": "example",
          "subject": {"reference": "Patient/example"},
          "abatementDateTime": "2024-01-01"
        }
        """;
        var element = ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
        var expr = _parser.Parse(expression);
        var context = new EvaluationContext { Schema = _schemaProvider };

        // Act
        var result = _evaluator.Evaluate(element, expr, context).ToList();

        // Assert
        result.ShouldHaveSingleItem();
    }
}
