/*
 * Copyright (c) 2026, Ignixa Contributors
 *
 * Regression coverage for issue #423: repeat()/repeatAll() declared ReturnType = "context", which the
 * generator wires to SymbolTable.ReturnsContext - the focus type, verbatim. The evaluator returns the
 * projection's results and never the focus items (CollectionFunctions.Repeat), so the analyzer typed
 * (name.repeat(family)) as HumanName, found no string in it, and reported a cast to string as provably
 * empty while the evaluator returned the family names. A false AlwaysEmpty=True is the dangerous
 * direction for that signal: a consumer acts on it.
 *
 * Both functions now declare ReturnType = "any", matching descendants() - the same shape of unbounded
 * recursion, which already declines to name its result type. Every case below asserts the evaluator's
 * own result first, so the analyzer assertion that follows cannot pass vacuously.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Pins the analyzer against the evaluator for casts whose focus is a <c>repeat()</c> projection.
/// </summary>
public class RepeatReturnTypeInferenceRegressionTests
{
    /// <summary>
    /// A single <c>name</c> entry, so <c>name.repeat(family)</c> yields exactly one item and the
    /// <c>as()</c> spellings stay evaluable on R5 and R6, where <c>as()</c> rejects a multi-item input.
    /// </summary>
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "repeat-inference",
          "active": true,
          "name": [ { "family": "Doe", "given": [ "Jane" ] } ]
        }
        """;

    private static readonly FhirVersion[] AllVersions =
    [
        FhirVersion.Stu3,
        FhirVersion.R4,
        FhirVersion.R4B,
        FhirVersion.R5,
        FhirVersion.R6,
    ];

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// The three spellings reported in #423, plus their <c>repeatAll()</c> counterparts, on every
    /// supported version rather than only the R4-R6 range the issue quoted: the defect was never
    /// version-specific, only its measured population was.
    /// </summary>
    public static TheoryData<FhirVersion, string> CastsOnRepeatProjections
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();

            foreach (var version in AllVersions)
            {
                data.Add(version, "(name.repeat(family)).ofType(string)");
                data.Add(version, "(name.repeat(family)).as(string)");
                data.Add(version, "(name.repeat(family)).ofType(FHIR.string)");
                data.Add(version, "(name.repeatAll(family)).ofType(string)");
                data.Add(version, "(name.repeatAll(family)).as(string)");
                data.Add(version, "(name.repeatAll(family)).ofType(FHIR.string)");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CastsOnRepeatProjections))]
    public void GivenACastOnARepeatProjection_WhenAnalysed_ThenItIsNotReportedAsAlwaysEmpty(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.ShouldNotBeEmpty(
            $"'{expression}' must return data on {version}, or the analyzer assertion below proves nothing.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"The analyzer must not report '{expression}' as provably empty while the evaluator returns data.");
        analysed.IsValidOrIndeterminate.ShouldBeTrue(
            $"'{expression}' is a conformant expression, so the analyzer must not raise an error for it on {version}.");
    }

    /// <summary>
    /// Names the cost of the fix rather than leaving it implicit. Declaring <c>"any"</c> makes the
    /// downstream type Unknown, and <see cref="AnalysisResult.IsValid"/> is deliberately stricter than
    /// "no errors were reported": an expression the analyzer could not fully reason about is not
    /// certified valid. So these expressions moved from <c>IsValid == true</c> (with a wrong
    /// always-empty warning attached) to <c>IsValid == false</c> with an <c>Indeterminate</c> issue,
    /// which is the honest answer and the same answer <c>descendants()</c> already produces.
    /// </summary>
    /// <remarks>
    /// Callers that would rather admit an undecidable expression than reject a conformant one should
    /// read <see cref="AnalysisResult.IsValidOrIndeterminate"/>, which stays true here. If a future
    /// change teaches the analyzer a fixpoint over the projection type, this expectation legitimately
    /// flips back; update it deliberately rather than deleting it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CastsOnRepeatProjections))]
    public void GivenACastOnARepeatProjection_WhenAnalysed_ThenTheVerdictIsIndeterminateRatherThanValid(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        analysed.IsIndeterminate.ShouldBeTrue(
            $"A repeat() projection cannot be typed statically, so '{expression}' must be admitted as undecided.");
        analysed.IsValid.ShouldBeFalse(
            $"IsValid requires a fully determined verdict, which '{expression}' no longer has on {version}.");
    }

    /// <summary>
    /// The provenance half of the same defect. <c>"context"</c> also drove
    /// <c>SystemTypeConstructionAnalyzer</c>, so a <c>repeat()</c> over a Quantity reported the focus's
    /// <c>Quantity</c> provenance while the evaluator projected the System.Decimal member, and the cast
    /// to that member's own type was called provably empty.
    /// </summary>
    /// <remarks>
    /// This expression additionally carries an unrelated pre-existing error - the analyzer does not model
    /// <c>Quantity.value</c> as a navigable member - so only the always-empty verdict is asserted here.
    /// </remarks>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenARepeatOverAQuantity_WhenCastToTheProjectedSystemType_ThenItIsNotReportedAsAlwaysEmpty(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        const string expression = "((1 'mg').repeat(value)).ofType(System.Decimal)";

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.ShouldNotBeEmpty(
            $"repeat(value) over a Quantity literal projects its System.Decimal member on {version}.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse(
            "The focus's Quantity provenance must not survive a repeat(), which returns the projection.");
    }

    /// <summary>
    /// The control. Nothing <em>downstream</em> of a <c>repeat()</c> can still be flagged always-empty -
    /// Unknown fails open at every site that raises the diagnostic - so there is no such case to pin, and
    /// inventing one would be theatre. What must survive is the diagnostic for a sibling subexpression:
    /// the fix is scoped to the <c>repeat()</c> result, not to the whole expression containing it.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAnAlwaysEmptyCastBesideARepeat_WhenAnalysed_ThenTheAlwaysEmptyClaimSurvives(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        const string siblingExpression = "name.repeat(family).exists() or active.ofType(Quantity).exists()";
        const string isolatedExpression = "active.ofType(Quantity)";

        // Act
        var evaluatedSibling = Evaluate(element, siblingExpression, schema);
        var evaluatedIsolated = Evaluate(element, isolatedExpression, schema);
        var analysedSibling = analyzer.Analyze(siblingExpression, "Patient");
        var analysedIsolated = analyzer.Analyze(isolatedExpression, "Patient");

        // Assert
        evaluatedIsolated.ShouldBeEmpty(
            $"Patient.active is a boolean, so a Quantity filter over it is empty on {version}.");
        analysedIsolated.HasAlwaysEmptySubexpression.ShouldBeTrue(
            "An always-empty cast on a statically known focus must still be reported.");
        analysedIsolated.IsValid.ShouldBeTrue(
            "An always-empty warning on a fully typed focus leaves the analysis decided.");

        evaluatedSibling.ShouldHaveSingleItem();
        analysedSibling.HasAlwaysEmptySubexpression.ShouldBeTrue(
            "The always-empty claim about the sibling cast must survive the repeat() in the same expression.");
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
