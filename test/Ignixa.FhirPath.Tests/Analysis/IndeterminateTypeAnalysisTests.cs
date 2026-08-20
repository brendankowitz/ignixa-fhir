using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class IndeterminateTypeAnalysisTests
{
    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R5.GetSchemaProvider());

    [Theory]
    [InlineData("Patient.children().notKnownStatically", "children")]
    [InlineData("Patient.descendants().notKnownStatically", "descendants")]
    [InlineData("Patient.birthDate.lowBoundary().notKnownStatically", "lowBoundary")]
    [InlineData("Patient.birthDate.highBoundary().notKnownStatically", "highBoundary")]
    public void GivenAnyReturnType_WhenNavigated_ThenPropagatesIndeterminateType(
        string expression,
        string functionName)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldBeEmpty();
        result.IsValid.ShouldBeFalse();
        result.IsIndeterminate.ShouldBeTrue();
        result.InferredTypes.Types.ShouldHaveSingleItem().IsUnknown.ShouldBeTrue();
        result.Issues.ShouldContain(issue =>
            issue.Severity == ValidationIssueSeverity.Warning &&
            issue.IsIndeterminate &&
            issue.Message.Contains($"{functionName}()", StringComparison.Ordinal) &&
            issue.Message.Contains("cannot be analysed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenInvalidPropertyAfterIndeterminateType_WhenAnalyzed_ThenDoesNotCertifyExpressionAsValid()
    {
        var result = _analyzer.Analyze(
            "Patient.descendants().definitelyNotAProperty",
            "Patient");

        result.Errors.ShouldBeEmpty();
        result.IsValid.ShouldBeFalse();
        result.IsIndeterminate.ShouldBeTrue();
    }

}
