/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression coverage for classifying temporal literals by node kind rather than by a leading sigil.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Pins that a string literal beginning with <c>@</c> is a string, and that a temporal literal is still
/// a date, dateTime or time.
/// </summary>
/// <remarks>
/// <para>
/// The two literal kinds are byte-identical by the time they reach the AST: the grammar keeps the sigil in
/// a temporal literal's value and strips only the quotes from a string literal, so <c>'@x'</c> and
/// <c>@x</c> both arrive as the CLR string <c>"@x"</c>. Classifying on the sigil therefore answered for
/// <c>'@x'</c> whatever it answered for <c>@x</c>: <c>'@'.length()</c> became a hard error against a
/// <c>date</c> focus, and <c>'@x' as String</c> became a confident always-empty verdict, which is the
/// direction a consumer acts on. Email-shaped literals are ordinary in FHIR invariants, so this is
/// reachable from shipped content rather than only from synthetic expressions.
/// </para>
/// <para>
/// Every case evaluates the expression before asserting anything about the analysis, so an analyzer
/// assertion cannot pass by agreeing with an evaluator that returns nothing. The evaluator carried the
/// same sigil sniff when building a literal's element, so these tests fail on both sides of the fix.
/// </para>
/// </remarks>
public class TemporalLiteralClassificationTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "active": true,
          "birthDate": "1980-04-01"
        }
        """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// The two literals whose source text differs only by the quotes the grammar discards. Nothing
    /// downstream can tell them apart from the value alone, which is the whole defect.
    /// </summary>
    [Fact]
    public void GivenAStringLiteralAndATemporalLiteralWithTheSameText_WhenParsed_ThenOnlyTheNodeKindSeparatesThem()
    {
        // Arrange
        const string stringLiteral = "'@2013'";
        const string temporalLiteral = "@2013";

        // Act
        var parsedString = _parser.Parse(stringLiteral);
        var parsedTemporal = _parser.Parse(temporalLiteral);

        // Assert
        parsedString.ShouldBeOfType<ConstantExpression>(
            "a string literal must stay an ordinary constant.");
        parsedTemporal.ShouldBeOfType<TemporalConstantExpression>(
            "a temporal literal must carry the parser's token choice into the AST.");
        ((ConstantExpression)parsedString).Value.ShouldBe(
            ((ConstantExpression)parsedTemporal).Value,
            "the two literals carry identical values, so no predicate over the value can classify them.");
    }

    /// <summary>
    /// The reported symptom: a valid string expression rejected because its focus was inferred as a date.
    /// </summary>
    [Fact]
    public void GivenAStringLiteralBeginningWithTheSigil_WhenLengthIsCalled_ThenItIsAStringOfItsFullLength()
    {
        // Arrange
        const string expression = "'@2013'.length()";
        var schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.Count.ShouldBe(1, $"'{expression}' must return one result.");
        evaluated[0].Value.ShouldBe(
            5,
            "the sigil is part of a string literal's value, so the string is five characters long.");
        analysed.IsValid.ShouldBeTrue(
            $"'{expression}' applies length() to a string, so the analyzer must not report an error.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"'{expression}' returns data, so no subexpression is provably empty.");
    }

    /// <summary>
    /// The dangerous direction of the signal: a confident always-empty verdict on a cast the evaluator
    /// answers with the value.
    /// </summary>
    [Theory]
    [InlineData("'@x' as String")]
    [InlineData("'@x'.ofType(String)")]
    public void GivenAStringLiteralBeginningWithTheSigil_WhenCastToString_ThenTheValueSurvives(string expression)
    {
        // Arrange
        var schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.Count.ShouldBe(1, $"'{expression}' must return the literal, not empty.");
        evaluated[0].Value.ShouldBe("@x", "a cast to String must not alter the value.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"The analyzer must not report '{expression}' as provably empty while the evaluator returns data.");
        analysed.IsValid.ShouldBeTrue(
            $"'{expression}' names a real type against a string focus, so it is not an error.");
    }

    /// <summary>
    /// The shape that makes this reachable: an email-matching invariant, where <c>@</c> is data.
    /// </summary>
    [Theory]
    [InlineData("'a@b.com'.matches('@')")]
    [InlineData("'a@b.com'.contains('@')")]
    [InlineData("'@'.length() = 1")]
    public void GivenAnInvariantShapedStringExpression_WhenAnalysed_ThenItEvaluatesTrueAndIsValid(string expression)
    {
        // Arrange
        var schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.Count.ShouldBe(1, $"'{expression}' must return a boolean.");
        evaluated[0].Value.ShouldBe(true, $"'{expression}' must hold for this input.");
        analysed.IsValid.ShouldBeTrue(
            $"The analyzer must not reject '{expression}', which the evaluator answers true.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"'{expression}' returns data, so no subexpression is provably empty.");
    }

    /// <summary>
    /// The controls. Removing the sigil sniff must not cost a genuine temporal literal its type; if these
    /// slip to <c>string</c> the fix has traded one wrong classification for another.
    /// </summary>
    [Theory]
    [InlineData("@2013", "date", "2013")]
    [InlineData("@2013-06-15", "date", "2013-06-15")]
    [InlineData("@2013-06-15T10:00:00", "dateTime", "2013-06-15T10:00:00")]
    [InlineData("@T10:00:00", "time", "10:00:00")]
    public void GivenATemporalLiteral_WhenAnalysed_ThenItKeepsItsTemporalType(
        string expression,
        string expectedTypeName,
        string expectedValue)
    {
        // Arrange
        var schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.Count.ShouldBe(1, $"'{expression}' must return one element.");
        evaluated[0].InstanceType.ShouldBe(
            expectedTypeName,
            $"the evaluator must still build a {expectedTypeName} element for '{expression}'.");
        evaluated[0].Value.ShouldBe(
            expectedValue,
            "the sigil and the time marker are FHIRPath syntax, not part of the value.");
        analysed.InferredTypes.Types.Select(t => t.TypeName).ShouldContain(
            expectedTypeName,
            $"the analyzer must still infer {expectedTypeName} for '{expression}'.");
        analysed.IsValid.ShouldBeTrue($"'{expression}' is a valid literal.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"'{expression}' returns data, so no subexpression is provably empty.");
    }

    /// <summary>
    /// The control for the type-dependent functions the classification feeds: a temporal literal must
    /// still reach the temporal overloads, and a string literal must not.
    /// </summary>
    [Theory]
    [InlineData("@2013-06-15.toString()", "2013-06-15")]
    [InlineData("'@2013-06-15'.toString()", "@2013-06-15")]
    public void GivenALiteralConvertedToString_WhenEvaluated_ThenTheSigilIsPartOfTheStringOnlyWhenItWasWritten(
        string expression,
        string expected)
    {
        // Arrange
        var schema = FhirVersion.R4.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);

        // Assert
        evaluated.Count.ShouldBe(1, $"'{expression}' must return one element.");
        evaluated[0].InstanceType.ShouldBe("string", "toString() constructs a string.");
        evaluated[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// Pins the classification of a constant carrying no value.
    /// </summary>
    /// <remarks>
    /// The rewrite that introduced the sigil sniff also dropped the base switch's <c>null</c> arm, so a
    /// valueless constant fell through to <c>string</c>. <see cref="ConstantExpression"/> rejects null in
    /// its constructor, so the arm is unreachable through a parsed AST and the classifier is asserted
    /// directly; the second assertion records why that is the only way to reach it.
    /// </remarks>
    [Fact]
    public void GivenAConstantWithNoValue_WhenClassified_ThenItIsEmptyRatherThanString()
    {
        // Act
        var typeName = SystemTypeConstructionAnalyzer.GetValueTypeName(null);

        // Assert
        typeName.ShouldBe(
            "empty",
            "a constant with no value constructs nothing; calling it a string invents a type.");
        Should.Throw<ArgumentNullException>(
            () => new ConstantExpression(null!),
            "the AST cannot carry a null constant, which is why the arm above is asserted directly.");
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
