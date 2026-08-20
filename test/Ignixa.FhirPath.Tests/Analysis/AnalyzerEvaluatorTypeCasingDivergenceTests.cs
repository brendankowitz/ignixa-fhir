/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * These tests pin a KNOWN GAP, not desired behaviour. Every assertion below describes an outcome the
 * project considers wrong and has deferred fixing. Read the class remarks before changing any of it.
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
/// Pins the divergence between the analyzer's case-insensitive type resolution and the evaluator's
/// <c>Ordinal</c>-exact matching, so it cannot widen or disappear unnoticed.
/// </summary>
/// <remarks>
/// <para>
/// This is a guard over a defect, not over intended behaviour. From R5 the evaluator matches type
/// names <c>Ordinal</c>-exact and carries a cast alias set only for the pre-R5 versions, while the
/// analyzer resolves them case-insensitively. A mis-cased cast therefore analyses clean and then
/// evaluates empty - precisely the failure class <see cref="AnalysisResult.HasAlwaysEmptySubexpression"/>
/// exists to surface, and it is blind to it here.
/// </para>
/// <para>
/// Where the case-insensitivity actually lives, measured rather than assumed: the analyzer resolves a
/// cast target twice over. It first matches the focus's own types with an <c>OrdinalIgnoreCase</c>
/// comparison, and only when that finds nothing does it fall back to the schema's type lookup, which
/// is itself backed by an <c>OrdinalIgnoreCase</c> dictionary. Closing either path alone changes
/// nothing, because the other still resolves the mis-cased name; both must be version-gated together.
/// That is the work this pass deferred.
/// </para>
/// <para>
/// The load-bearing assertion is <see cref="AnalysisResult.InferredTypes"/>: the analyzer resolves the
/// mis-cased spelling to the same concrete type as the correct one. Closing both resolution paths was
/// measured to fail all cases here on that assertion. When the analyzer is aligned, delete this class
/// and move the cases into the analyzer's own always-empty coverage.
/// </para>
/// <para>
/// Every case carries a correctly-cased control asserted non-empty, so "the evaluator returned empty"
/// cannot be satisfied by a fixture that never held the data or a path that never resolved.
/// </para>
/// </remarks>
public class AnalyzerEvaluatorTypeCasingDivergenceTests
{
    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "coding": [ { "system": "http://loinc.org", "code": "1234-5" } ] },
          "valueString": "typed"
        }
        """;

    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "name": [ { "family": "Doe", "given": [ "Jane" ] } ]
        }
        """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// Lower-cased complex type names, which no version has ever aliased, plus the System spelling of
    /// a FHIR primitive, which only the pre-R5 alias set accepts.
    /// </summary>
    public static TheoryData<FhirVersion, string, string, string> MisCasedCasts
    {
        get
        {
            var data = new TheoryData<FhirVersion, string, string, string>();

            foreach (var version in new[]
                     {
                         FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6,
                     })
            {
                data.Add(version, "Observation", "code.as(codeableconcept)", "code.as(CodeableConcept)");
                data.Add(version, "Observation", "code.ofType(codeableconcept)", "code.ofType(CodeableConcept)");
                data.Add(version, "Patient", "name.as(humanname)", "name.as(HumanName)");
                data.Add(version, "Patient", "name.ofType(humanname)", "name.ofType(HumanName)");
            }

            // The System spelling of a primitive survives below R5 through the cast alias set, so only
            // R5 and R6 diverge.
            foreach (var version in new[] { FhirVersion.R5, FhirVersion.R6 })
            {
                data.Add(version, "Observation", "value.as(String)", "value.as(string)");
                data.Add(version, "Observation", "value.ofType(String)", "value.ofType(string)");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MisCasedCasts))]
    public void GivenAMisCasedTypeName_WhenAnalysedAndEvaluated_ThenTheAnalyzerResolvesWhatTheEvaluatorEmpties(
        FhirVersion version,
        string rootType,
        string misCased,
        string correctlyCased)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(rootType == "Patient" ? PatientJson : ObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var control = Evaluate(element, correctlyCased, schema);
        var evaluated = Evaluate(element, misCased, schema);
        var analysedMisCased = analyzer.Analyze(misCased, rootType);
        var analysedControl = analyzer.Analyze(correctlyCased, rootType);

        // Assert
        control.ShouldNotBeEmpty($"'{correctlyCased}' must resolve, or the empty result below proves nothing.");
        evaluated.ShouldBeEmpty($"The evaluator matches type names Ordinal-exact, so '{misCased}' must be empty on {version}.");

        analysedMisCased.InferredTypes.Types.Select(t => t.TypeName).ShouldBe(
            analysedControl.InferredTypes.Types.Select(t => t.TypeName),
            $"KNOWN GAP: the analyzer resolves '{misCased}' to the same type as '{correctlyCased}'. Version-gating both of the analyzer's type-resolution paths must fail here.");
        analysedMisCased.InferredTypes.Types.ShouldNotBeEmpty(
            $"KNOWN GAP: '{misCased}' resolves to a concrete type rather than to nothing.");

        analysedMisCased.IsValid.ShouldBeTrue(
            $"KNOWN GAP: the analyzer certifies '{misCased}', which the evaluator empties.");
        analysedMisCased.HasAlwaysEmptySubexpression.ShouldBeFalse(
            $"KNOWN GAP: the always-empty signal does not see '{misCased}'.");
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();
}
