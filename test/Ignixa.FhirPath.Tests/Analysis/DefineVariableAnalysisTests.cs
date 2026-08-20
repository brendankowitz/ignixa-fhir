// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Pins the analyzer to the evaluator on <c>defineVariable</c>: every expression the evaluator refuses
/// to run must be reported as an error at analysis time, and every expression it accepts must not be.
/// </summary>
/// <remarks>
/// An analyzer that is more permissive than the evaluator is worse than no analyzer, because a clean
/// report is then evidence of nothing. Each case below asserts both halves - the diagnostic and the
/// runtime throw - so the two cannot drift apart without a test failing.
/// </remarks>
public class DefineVariableAnalysisTests
{
    private readonly IFhirSchemaProvider _schema = FhirVersion.R4.GetSchemaProvider();
    private readonly FhirPathAnalyzer _analyzer;
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    public DefineVariableAnalysisTests()
    {
        _analyzer = new FhirPathAnalyzer(_schema);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("resource")]
    [InlineData("rootResource")]
    [InlineData("ucum")]
    public void GivenDefineVariableOverASystemVariable_WhenAnalyzing_ThenReportsTheSameErrorTheEvaluatorThrows(string reservedName)
    {
        // Arrange
        var expression = $"Patient.defineVariable('{reservedName}', 'oops')";

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        result.IsValid.ShouldBeFalse(string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Message}")));
        result.Errors.ShouldContain(e => e.Contains($"system variable '%{reservedName}'", StringComparison.Ordinal));
        EvaluatingShouldThrow(expression).Message.ShouldContain($"system variable '%{reservedName}'");
    }

    [Fact]
    public void GivenDefineVariableRedefinedInTheSameChain_WhenAnalyzing_ThenReportsTheSameErrorTheEvaluatorThrows()
    {
        // Arrange — the official dvRedefiningVariableThrowsError shape.
        const string expression = "Patient.defineVariable('v1').defineVariable('v1').select(%v1)";

        // Act
        var result = _analyzer.Analyze(expression, "Patient");

        // Assert
        result.IsValid.ShouldBeFalse(string.Join(" | ", result.Issues.Select(i => $"{i.Severity}:{i.Message}")));
        result.Errors.ShouldContain(e => e.Contains("'%v1' is already defined", StringComparison.Ordinal));
        EvaluatingShouldThrow(expression).Message.ShouldContain("'%v1' is already defined");
    }

    [Fact]
    public void GivenTwoDistinctVariablesInOneChain_WhenAnalyzing_ThenNoErrorIsReported()
    {
        // Arrange & Act
        var result = _analyzer.Analyze("Patient.defineVariable('v1').defineVariable('v2').select(%v1 | %v2)", "Patient");

        // Assert
        result.IsValid.ShouldBeTrue(string.Join(" | ", result.Errors));
    }

    /// <summary>
    /// Negative control for the redefinition rule's deliberate narrowness: the same name defined in two
    /// sibling arguments is not a redefinition (official <c>dvParametersDontColide</c>), and the analyzer
    /// must not invent an error the evaluator does not raise.
    /// </summary>
    [Fact]
    public void GivenTheSameNameDefinedInSiblingScopes_WhenAnalyzing_ThenNoErrorIsReported()
    {
        // Arrange & Act
        var result = _analyzer.Analyze(
            "Patient.name.defineVariable('n').select(%n.given) | Patient.name.defineVariable('n').select(%n.family)",
            "Patient");

        // Assert
        result.IsValid.ShouldBeTrue(string.Join(" | ", result.Errors));
    }

    private FhirPathEvaluationException EvaluatingShouldThrow(string expression)
    {
        var parsed = _parser.Parse(expression);
        var patient = EmptyPatient();

        return Should.Throw<FhirPathEvaluationException>(() => _evaluator.Evaluate(patient, parsed).ToList());
    }

    private IElement EmptyPatient()
        => Serialization.SourceNodes.ResourceJsonNode
            .Parse("""{ "resourceType": "Patient", "id": "example" }""")
            .ToElement(_schema);
}
