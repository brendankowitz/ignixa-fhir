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
 *
 * The one attribute string moves two independent mechanisms - the generator's return-type delegate and
 * SystemTypeConstructionAnalyzer - so a test that observes only the always-empty verdict goes red on
 * revert without being able to say which half it was measuring. The provenance axis is therefore asserted
 * against SystemTypeConstructionAnalyzer directly, in its own statements, so a regression confined to it
 * cannot hide behind the type-set half.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;
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

    /// <summary>
    /// One cast per provenance class the fix moved: a quantity literal focus, whose construction was
    /// reported as <c>Quantity</c>, and navigated FHIR data, whose construction was reported as "nothing".
    /// Both must now fail open.
    /// </summary>
    public static TheoryData<FhirVersion, string> RepeatProjectionsUnderACast
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();

            foreach (var version in AllVersions)
            {
                data.Add(version, "((1 'mg').repeat(value)).ofType(System.Decimal)");
                data.Add(version, "(name.repeat(family)).ofType(string)");
                data.Add(version, "(name.repeatAll(family)).ofType(string)");
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
    /// The provenance axis on its own, asserted against
    /// <see cref="SystemTypeConstructionAnalyzer"/> directly rather than through a diagnostic that the
    /// type-set axis can satisfy by itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declared <c>ReturnType</c> string drives two independent mechanisms: the generator's return-type
    /// delegate, which produces the static type set, and <see cref="SystemTypeConstructionAnalyzer"/>,
    /// which decides what System value the focus constructs. One attribute moves both. An assertion on
    /// <see cref="AnalysisResult.HasAlwaysEmptySubexpression"/> alone therefore cannot tell them apart:
    /// once the type set is Unknown, every site that raises the diagnostic is short-circuited on
    /// <c>!focusTypes.HasUnknown</c> regardless of what provenance says, so such an assertion would stay
    /// green through a provenance regression and go red on revert for the other reason.
    /// </para>
    /// <para>
    /// This asserts the provenance verdict itself, so it discriminates. Fail-open is the only sound answer
    /// for a <c>repeat()</c>: the evaluator returns the projection, so the focus's own construction -
    /// <c>Quantity</c> for a quantity literal, "constructs nothing" for navigated FHIR data - describes a
    /// value the function does not return.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RepeatProjectionsUnderACast))]
    public void GivenARepeatProjection_WhenItsCastProvenanceIsAnalysed_ThenItFailsOpen(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();

        // Act
        var construction = AnalyzeCastFocusProvenance(schema, expression);

        // Assert
        construction.MayConstructAny.ShouldBeTrue(
            $"The cast focus in '{expression}' is a repeat() projection, so its System-value provenance is "
            + $"not knowable on {version} and must fail open rather than report the focus's own.");
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
        AnalyzeCastFocusProvenance(schema, expression).MayConstructAny.ShouldBeTrue(
            "Asserted separately from the always-empty verdict above, which an Unknown focus type satisfies "
            + "on its own: this is the assertion that fails if only the provenance half regresses.");
    }

    /// <summary>
    /// The controls: always-empty claims that must survive untouched, because the fix is scoped to the
    /// type of a <c>repeat()</c>'s <em>result</em>, not to the expression containing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tight one is <c>repeat(status)</c>: the claim is raised inside the changed function's own
    /// projection argument, which is analysed against the focus, not against the function's return type.
    /// That is the same function whose attribute moved, so it is the closest control the change site
    /// admits.
    /// </para>
    /// <para>
    /// The loose one is a disjoint sibling of a <c>repeat()</c> in one expression, which guards a
    /// different thing: that the fix did not widen from the <c>repeat()</c> subtree to the whole
    /// expression.
    /// </para>
    /// <para>
    /// What is deliberately absent is a control <em>downstream</em> of a <c>repeat()</c>. There is none:
    /// Unknown fails open at every site that raises the diagnostic, so <c>name.repeat(family).status</c>,
    /// <c>.nosuchproperty</c> and <c>.ofType(Quantity)</c> all stop being flagged. That is the measured
    /// cost of the fix, not an oversight, and inventing a case there would be theatre.
    /// </para>
    /// </remarks>
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
        const string projectionArgumentExpression = "repeat(status)";
        const string siblingExpression = "name.repeat(family).exists() or active.ofType(Quantity).exists()";
        const string isolatedExpression = "active.ofType(Quantity)";

        // Act
        var evaluatedProjectionArgument = Evaluate(element, projectionArgumentExpression, schema);
        var evaluatedSibling = Evaluate(element, siblingExpression, schema);
        var evaluatedIsolated = Evaluate(element, isolatedExpression, schema);
        var analysedProjectionArgument = analyzer.Analyze(projectionArgumentExpression, "Patient");
        var analysedSibling = analyzer.Analyze(siblingExpression, "Patient");
        var analysedIsolated = analyzer.Analyze(isolatedExpression, "Patient");

        // Assert
        evaluatedProjectionArgument.ShouldBeEmpty(
            $"Patient declares no 'status' property, so repeat(status) projects nothing on {version}.");
        analysedProjectionArgument.HasAlwaysEmptySubexpression.ShouldBeTrue(
            "The claim is raised while analysing repeat()'s own projection argument against the focus, not "
            + "its return type, so the ReturnType change must leave it standing.");

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

    /// <summary>
    /// Runs <see cref="SystemTypeConstructionAnalyzer"/> over the focus of the outermost
    /// <c>ofType()</c>/<c>as()</c> call in <paramref name="expression"/>, reproducing the construction
    /// <see cref="FhirPathAnalyzer"/> performs so the verdict is the production one rather than a
    /// re-derivation.
    /// </summary>
    private SystemTypeConstruction AnalyzeCastFocusProvenance(IFhirSchemaProvider schema, string expression)
    {
        var rootPropertyNames = schema.ResourceTypeNames
            .Select(schema.GetTypeDefinition)
            .Where(type => type is not null)
            .SelectMany(type => type!.Children)
            .Select(child => child.Info.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var construction = new SystemTypeConstructionAnalyzer(
            new SymbolTable(schema),
            propertyName => rootPropertyNames.Contains(propertyName));

        var castFocus = FindCastFocus(_parser.Parse(expression))
            ?? throw new InvalidOperationException($"'{expression}' contains no ofType()/as() call to probe.");

        return construction.Analyze(castFocus);
    }

    private static Expression? FindCastFocus(Expression? node) =>
        node switch
        {
            FunctionCallExpression { FunctionName: "ofType" or "as" } cast => cast.Focus,
            ParenthesizedExpression parenthesized => FindCastFocus(parenthesized.InnerExpression),
            FunctionCallExpression function => FindCastFocus(function.Focus)
                ?? function.Arguments.Select(FindCastFocus).FirstOrDefault(found => found is not null),
            PropertyAccessExpression property => FindCastFocus(property.Focus),
            _ => null,
        };

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
