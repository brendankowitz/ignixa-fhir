/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression coverage for the System-namespace exception in analyzer cast resolution.
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
/// Pins the analyzer against the evaluator for casts whose focus is a System value rather than a
/// navigated FHIR element.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator's cast matcher keeps the System-namespace exception above its R5 version gate: a System
/// value carries FHIR's lower camel case spelling in its instance type, so <c>System.Integer</c> has to
/// reach an <c>integer</c> instance on every version. The analyzer resolves casts through the same
/// matcher, so it has to supply the same namespace answer; a static type that cannot say which namespace
/// it came from forces the FHIR answer and reports <c>count().ofType(Integer)</c> as provably empty from
/// R5 onward, which the evaluator contradicts.
/// </para>
/// <para>
/// The distinction is carried by <see cref="Visitors.FhirPathType.IsSystemValue"/>, set where the
/// analyzer already knows a value was constructed rather than navigated to: literals, operator results
/// and functions declared to return a primitive.
/// </para>
/// <para>
/// Suppressing the diagnostic whenever the target is a System spelling would look equivalent and is not:
/// it would also accept <c>Observation.value.as(String)</c>, which
/// <see cref="AnalyzerEvaluatorTypeCasingAlignmentTests"/> pins as always-empty from R5 onward. Every
/// case here asserts the evaluator's own result, so the two directions cannot both be satisfied by a
/// blanket answer.
/// </para>
/// </remarks>
public class AnalyzerSystemValueCastAlignmentTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "active": true,
          "birthDate": "1980-04-01",
          "name": [ { "family": "Doe", "given": [ "Jane", "Q" ] } ]
        }
        """;

    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "coding": [ { "system": "http://loinc.org", "code": "1234-5" } ] },
          "valueString": "typed"
        }
        """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// System values reached through both analyzer cast entry points. The matching target must survive
    /// the R5 gate; the mismatched target in <see cref="SystemValueCastsThatStayEmpty"/> must not.
    /// </summary>
    public static TheoryData<FhirVersion, string> SystemValueCastsThatKeepTheValue
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();

            foreach (var version in new[] { FhirVersion.R5, FhirVersion.R6 })
            {
                // count() and exists() construct System values from a FHIR focus.
                data.Add(version, "name.given.count().ofType(Integer)");
                data.Add(version, "name.given.count().as(Integer)");
                data.Add(version, "name.exists().ofType(Boolean)");
                data.Add(version, "name.exists().as(Boolean)");

                // toString() re-types a FHIR element as a System string.
                data.Add(version, "birthDate.toString().ofType(String)");
                data.Add(version, "birthDate.toString().as(String)");

                // Literals and operator results never touch the schema at all.
                data.Add(version, "(1 + 1).ofType(Integer)");
                data.Add(version, "(1 + 1).as(Integer)");
                data.Add(version, "'lit'.ofType(String)");
                data.Add(version, "'lit'.as(String)");
                data.Add(version, "('a' & 'b').ofType(String)");
                data.Add(version, "(1.5 + 1).ofType(Decimal)");
                data.Add(version, "now().ofType(DateTime)");

                // The namespace-qualified spelling of the same cast.
                data.Add(version, "name.given.count().ofType(System.Integer)");
            }

            return data;
        }
    }

    /// <summary>
    /// The inverse set: a System focus whose type does not match the target. These stay always-empty, so
    /// the fix cannot be a blanket suppression keyed on the target spelling.
    /// </summary>
    public static TheoryData<FhirVersion, string> SystemValueCastsThatStayEmpty
    {
        get
        {
            var data = new TheoryData<FhirVersion, string>();

            foreach (var version in new[] { FhirVersion.R5, FhirVersion.R6 })
            {
                data.Add(version, "name.given.count().ofType(String)");
                data.Add(version, "name.given.count().as(String)");
                data.Add(version, "name.given.count().ofType(Boolean)");
                data.Add(version, "name.exists().ofType(Integer)");
                data.Add(version, "name.exists().as(Integer)");
                data.Add(version, "'lit'.ofType(Integer)");
                data.Add(version, "'lit'.as(Integer)");

                // A navigated FHIR element, not a constructed value: the R5 gate applies in full.
                data.Add(version, "name.given.first().ofType(String)");
                data.Add(version, "birthDate.ofType(Date)");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SystemValueCastsThatKeepTheValue))]
    public void GivenASystemValueFocus_WhenCastToItsOwnSystemTypeName_ThenAnalysisAgreesTheValueSurvives(
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
        analysed.InferredTypes.Types.ShouldNotBeEmpty(
            $"The analyzer must infer the surviving type for '{expression}'.");
        analysed.IsValid.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(SystemValueCastsThatStayEmpty))]
    public void GivenASystemValueFocus_WhenCastToADifferentTypeName_ThenAnalysisAgreesItIsAlwaysEmpty(
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
        evaluated.ShouldBeEmpty(
            $"'{expression}' must be empty on {version}, or the analyzer assertion below pins the wrong answer.");
        analysed.HasAlwaysEmptySubexpression.ShouldBeTrue(
            $"The analyzer must still report '{expression}' as provably empty.");
        analysed.InferredTypes.Types.ShouldBeEmpty();
        analysed.IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// The diagnostic contrast the System flag exists to draw. Both casts name <c>Boolean</c>/<c>String</c>
    /// against a focus the analyzer types as <c>boolean</c>/<c>string</c>; only the namespace separates
    /// them, and the evaluator answers them differently.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenTwoCastsDifferingOnlyByNamespace_WhenAnalysed_ThenOnlyTheFhirElementIsAlwaysEmpty(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        const string fhirElementCast = "active.ofType(Boolean)";
        const string systemValueCast = "birthDate.toString().ofType(String)";

        // Act
        var evaluatedFhirElement = Evaluate(element, fhirElementCast, schema);
        var evaluatedSystemValue = Evaluate(element, systemValueCast, schema);
        var analysedFhirElement = analyzer.Analyze(fhirElementCast, "Patient");
        var analysedSystemValue = analyzer.Analyze(systemValueCast, "Patient");

        // Assert
        evaluatedFhirElement.ShouldBeEmpty(
            "Patient.active is navigated to, so the R5 gate rejects the System spelling.");
        evaluatedSystemValue.ShouldNotBeEmpty(
            "toString() constructs a System string, which the System-namespace exception admits.");

        analysedFhirElement.HasAlwaysEmptySubexpression.ShouldBeTrue(
            "The analyzer must keep reporting the FHIR element cast as provably empty.");
        analysedSystemValue.HasAlwaysEmptySubexpression.ShouldBeFalse(
            "The analyzer must not report the System value cast as provably empty.");
    }

    /// <summary>
    /// Namespace-qualified targets reach the same contract as their unqualified spelling. Before the
    /// prefix was stripped ahead of the schema lookup, a qualified target that the evaluator empties was
    /// rejected as an invalid FHIR type instead.
    /// </summary>
    [Theory]
    [InlineData(FhirVersion.Stu3, false)]
    [InlineData(FhirVersion.R4, false)]
    [InlineData(FhirVersion.R4B, false)]
    [InlineData(FhirVersion.R5, true)]
    [InlineData(FhirVersion.R6, true)]
    public void GivenANamespaceQualifiedCastTarget_WhenAnalysed_ThenItIsValidAndMatchesTheEvaluator(
        FhirVersion version,
        bool expectsAlwaysEmpty)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        string[] expressions = ["value.as(System.String)", "value.as(FHIR.String)", "value.ofType(System.String)"];

        foreach (var expression in expressions)
        {
            // Act
            var evaluated = Evaluate(element, expression, schema);
            var analysed = analyzer.Analyze(expression, "Observation");

            // Assert
            analysed.IsValid.ShouldBeTrue(
                $"'{expression}' names a real type once the namespace prefix is parsed, so it is not an error on {version}.");
            evaluated.Any().ShouldBe(!expectsAlwaysEmpty, $"evaluator disagrees for '{expression}' on {version}");
            analysed.HasAlwaysEmptySubexpression.ShouldBe(
                expectsAlwaysEmpty,
                $"The analyzer must reach the same answer as the evaluator for '{expression}' on {version}.");
        }
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
