/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The five FHIRPath type operators - is, is(), as, as() and ofType() - share one matcher, and this
 * pins the three things that must stay true of it.
 *
 * 1. They agree on subclasses. FHIRPath gives all five the identical clause, "of the type specified in
 *    the second operand, or a subclass thereof" (N1 6.3.1/6.3.3/5.2.4, unchanged in the 3.0.0 build).
 *    'as' and 'ofType()' used to compare InstanceType for equality instead, so on an Age
 *    "value is Quantity" was true while "value as Quantity" was empty - the same question answered two
 *    ways depending on which keyword you reached for.
 *
 * 2. They disagree about primitives, and must. FHIR overrides ofType() with "All primitives are
 *    considered to be independent types (so markdown is not a subclass of string)" (R5 2.1.9.1.5), and
 *    HL7's suite applies that to as() while pointedly exempting is(). A matcher that "fixed" the
 *    is/as split by walking primitive edges everywhere would turn testFHIRPathAsFunction11/16 red and
 *    would index every ConceptMap.sourceCanonical into the source-uri search parameter.
 *
 * 3. They disagree about the System/FHIR namespace, and must. R5 2.1.9.1.2 makes is() the exception
 *    and says of the cast: "Note that ofType() does not have such restrictions". STU3 relies on that -
 *    it spells its casts Patient.deceased.as(DateTime) - as does R4/R4B's code-value-date composite.
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

public class TypeOperatorSubclassMatchingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _schemaProvider = FhirVersion.R4.GetSchemaProvider();

    private const string ObservationWithAgeValue = """
    {
      "resourceType": "Observation",
      "id": "example",
      "status": "final",
      "code": {"text": "test"},
      "valueAge": {"value": 42, "unit": "years", "system": "http://unitsofmeasure.org", "code": "a"}
    }
    """;

    [Theory]
    [InlineData("value is Quantity")]
    [InlineData("value.is(Quantity)")]
    public void GivenAnAgeValue_WhenTypeTestedAgainstQuantity_ThenTrue(string expression)
    {
        // Arrange
        var element = ResourceJsonNode.Parse(ObservationWithAgeValue).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, expression);

        // Assert
        result.Single().Value.ShouldBe(true);
    }

    [Theory]
    [InlineData("value as Quantity")]
    [InlineData("value.as(Quantity)")]
    [InlineData("value.ofType(Quantity)")]
    public void GivenAnAgeValue_WhenCastToQuantity_ThenTheAgeSurvives(string expression)
    {
        // The regression this exists for: all three of these returned empty while the two type tests
        // above returned true. Asserting the surviving element is still an Age - rather than merely
        // non-empty - is what proves the cast filtered by subtype rather than by coincidence.

        // Arrange
        var element = ResourceJsonNode.Parse(ObservationWithAgeValue).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, expression);

        // Assert
        result.Single().InstanceType.ShouldBe("Age");
    }

    [Fact]
    public void GivenAnAgeValue_WhenCastToAnUnrelatedComplexType_ThenEmpty()
    {
        // Subclass-awareness must not degrade into matching anything complex. Duration and Age are
        // siblings under Quantity, so this is the case that separates "walks upward" from "walks the
        // whole table".

        // Arrange
        var element = ResourceJsonNode.Parse(ObservationWithAgeValue).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, "value.ofType(Duration)");

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("status as string")]
    [InlineData("status.as(string)")]
    [InlineData("status.ofType(string)")]
    public void GivenACode_WhenCastToString_ThenEmpty(string expression)
    {
        // FHIR's "All primitives are considered to be independent types" override, which the casts honour
        // and the type tests do not. The companion assertion - that status.is(string) is true - lives in
        // IsTypeHierarchyRegressionTests; the pair is the whole point.

        // Arrange
        var element = ResourceJsonNode.Parse(ObservationWithAgeValue).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, expression);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("deceased.as(DateTime)")]
    [InlineData("deceased.ofType(DateTime)")]
    [InlineData("deceased as DateTime")]
    public void GivenAFhirDateTime_WhenCastUsingTheCapitalizedSystemName_ThenItStillMatches(string expression)
    {
        // R5 2.1.9.1.2: "Note that ofType() does not have such restrictions". This is not a nicety -
        // Patient.deceased.as(DateTime) is the literal text of STU3's Patient-death-date SearchParameter,
        // and R4/R4B's code-value-date composite carries value.as(DateTime) to this day. Enforcing the
        // System/FHIR distinction on casts the way 'is' enforces it would empty those search parameters.

        // Arrange
        var json = """
        {"resourceType": "Patient", "id": "example", "deceasedDateTime": "2024-01-01T00:00:00Z"}
        """;
        var element = ResourceJsonNode.Parse(json).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, expression);

        // Assert
        result.Single().InstanceType.ShouldBe("dateTime");
    }

    [Fact]
    public void GivenAFhirBoolean_WhenTypeTestedAgainstTheCapitalizedSystemName_ThenFalse()
    {
        // The other side of the namespace axis: 'is' keeps the distinction the casts drop, so a FHIR
        // boolean is not a System Boolean. Asserted so that relaxing the casts cannot quietly relax
        // the type tests with them.

        // Arrange
        var json = """{"resourceType": "Patient", "id": "example", "active": true}""";
        var element = ResourceJsonNode.Parse(json).ToElement(_schemaProvider);

        // Act
        var result = Evaluate(element, "active.is(Boolean)");

        // Assert
        result.Single().Value.ShouldBe(false);
    }

    [Fact]
    public void GivenACompilableOfType_WhenEvaluatedByBothPaths_ThenTheySelectTheSameSubclass()
    {
        // ofType() has a compiled spelling in FhirPathDelegateCompiler that used to compare InstanceType
        // inline, so it was a third copy of the matcher and drifted from the two interpreted ones. An
        // expression must not change meaning because it happened to be compilable.

        // Arrange
        var element = ResourceJsonNode.Parse(ObservationWithAgeValue).ToElement(_schemaProvider);
        var ast = _parser.Parse("value.ofType(Quantity)");
        var compiled = new FhirPathDelegateCompiler(new FhirPathEvaluator()).TryCompile(ast);
        compiled.ShouldNotBeNull("ofType() must stay on the compiled fast path for this test to mean anything.");

        // Act
        var compiledResult = compiled(element, new EvaluationContext()).ToList();
        var interpretedResult = _evaluator.Evaluate(element, ast, new EvaluationContext()).ToList();

        // Assert
        compiledResult.Select(e => e.InstanceType).ShouldBe(interpretedResult.Select(e => e.InstanceType));
        compiledResult.Single().InstanceType.ShouldBe("Age");
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression) =>
        _evaluator.Evaluate(element, _parser.Parse(expression), new EvaluationContext()).ToList();
}
