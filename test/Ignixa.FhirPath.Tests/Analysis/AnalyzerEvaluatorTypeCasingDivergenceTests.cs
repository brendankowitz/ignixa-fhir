/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * These tests previously pinned the analyzer/evaluator type-casing divergence. They now pin the aligned
 * behaviour over the same 24-case coverage matrix.
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
/// Pins alignment between analyzer type resolution and the evaluator's <c>Ordinal</c>-exact matching.
/// </summary>
/// <remarks>
/// <para>
/// These 24 cases are a straight inversion of the matrix that previously pinned the known gap. The
/// evaluator matches type names <c>Ordinal</c>-exact and carries a cast alias set only for pre-R5
/// versions; the analyzer must make the same decision and surface a provably empty cast through
/// <see cref="AnalysisResult.HasAlwaysEmptySubexpression"/>.
/// </para>
/// <para>
/// Each case exercises both analyzer resolution paths. The analyzer first compares the target with the
/// focus's own types. Once that rejects the spelling, it reaches the schema fallback; because generated
/// providers resolve case-insensitively, the analyzer must validate the canonical name returned by the
/// provider with the same matcher before accepting it. Leaving either path case-insensitive makes all
/// 24 cases infer the control type and fail.
/// </para>
/// <para>
/// The load-bearing assertions are the empty inferred type set and the always-empty diagnostic. Together
/// they prevent either the focus match or the case-insensitive schema fallback from reintroducing the
/// divergence.
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

    private const string DateTimeObservationJson = """
        {
          "resourceType": "Observation",
          "id": "obs-date",
          "status": "final",
          "code": { "text": "test" },
          "valueDateTime": "2024-06-15T08:00:00Z"
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
    public void GivenAMisCasedTypeName_WhenAnalysedAndEvaluated_ThenBothReportAnEmptyCast(
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

        analysedControl.InferredTypes.Types.ShouldNotBeEmpty(
            $"'{correctlyCased}' must infer a type, or the empty inference below proves nothing.");
        analysedMisCased.InferredTypes.Types.ShouldBeEmpty(
            $"The analyzer must not accept '{misCased}' through either its focus match or schema fallback.");
        analysedMisCased.IsValid.ShouldBeTrue();
        analysedMisCased.HasAlwaysEmptySubexpression.ShouldBeTrue(
            $"The analyzer must surface the same provably empty cast as the evaluator for '{misCased}'.");
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    public void GivenPreR5_WhenUsingTheCanonicalSystemStringAlias_ThenAnalysisAndEvaluationKeepTheValue(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, "value.as(String)", schema);
        var analysed = analyzer.Analyze("value.as(String)", "Observation");

        // Assert
        evaluated.ShouldHaveSingleItem().InstanceType.ShouldBe("string");
        analysed.InferredTypes.Types.Select(type => type.TypeName).ShouldBe(["string"]);
        analysed.IsValid.ShouldBeTrue();
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

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
    public void GivenArbitraryTypeCasing_WhenAnalysedAndEvaluated_ThenBothReportAnEmptyCast(
        FhirVersion version,
        string typeName)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(DateTimeObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        string expression = $"value.as({typeName})";

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Observation");

        // Assert
        evaluated.ShouldBeEmpty();
        analysed.InferredTypes.Types.ShouldBeEmpty();
        analysed.IsValid.ShouldBeTrue();
        analysed.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAMisCasedBinaryAsTarget_WhenAnalysedAndEvaluated_ThenBothReportAnEmptyCast(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        const string expression = "code as codeableconcept";

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Observation");

        // Assert
        evaluated.ShouldBeEmpty();
        analysed.InferredTypes.Types.ShouldBeEmpty();
        analysed.IsValid.ShouldBeTrue();
        analysed.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    [Theory]
    [InlineData(FhirVersion.Stu3, true)]
    [InlineData(FhirVersion.R4, true)]
    [InlineData(FhirVersion.R4B, true)]
    [InlineData(FhirVersion.R5, false)]
    [InlineData(FhirVersion.R6, false)]
    public void GivenAnIndeterminateFocus_WhenSchemaFallbackReturnsString_ThenItsCanonicalNameIsValidated(
        FhirVersion version,
        bool expectsString)
    {
        // descendants() is statically indeterminate, so the analyzer cannot match String against a known
        // focus type and must use the case-insensitive schema fallback. The provider returns canonical
        // string; the shared matcher accepts that alias below R5 and rejects it from R5 onward.

        // Arrange
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);
        const string expression = "descendants().ofType(String)";

        // Act
        var evaluated = Evaluate(element, expression, schema);
        var analysed = analyzer.Analyze(expression, "Patient");

        // Assert
        evaluated.Any().ShouldBe(expectsString);
        analysed.InferredTypes.Types.Any(type => type.TypeName == "string").ShouldBe(expectsString);
    }

    [Fact]
    public void GivenAnUnspecifiedSchemaVersion_WhenUsingTheCanonicalSystemStringAlias_ThenMatchingFailsOpen()
    {
        // Arrange
        var schema = new UnspecifiedSchemaProvider(FhirVersion.R5.GetSchemaProvider());
        var element = ResourceJsonNode.Parse(ObservationJson).ToElement(schema);
        var analyzer = new FhirPathAnalyzer(schema);

        // Act
        var evaluated = Evaluate(element, "value.as(String)", schema);
        var analysed = analyzer.Analyze("value.as(String)", "Observation");

        // Assert
        evaluated.ShouldHaveSingleItem().InstanceType.ShouldBe("string");
        analysed.InferredTypes.Types.Select(type => type.TypeName).ShouldBe(["string"]);
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

    private IReadOnlyList<IElement> Evaluate(IElement element, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                element,
                _parser.Parse(expression),
                new EvaluationContext { Resource = element, RootResource = element, Schema = schema })
            .ToList();

    private sealed class UnspecifiedSchemaProvider(IFhirSchemaProvider inner) : IFhirSchemaProvider
    {
        public FhirVersion Version => FhirVersion.Unspecified;

        public IReadOnlySet<string> ResourceTypeNames => inner.ResourceTypeNames;

        public string FullVersion => inner.FullVersion;

        public IReferenceMetadataProvider ReferenceMetadataProvider => inner.ReferenceMetadataProvider;

        public IValueSetProvider ValueSetProvider => inner.ValueSetProvider;

        public IType? GetTypeDefinition(string typeName) => inner.GetTypeDefinition(typeName);

        public bool IsKnownType(string typeName) => inner.IsKnownType(typeName);
    }
}
