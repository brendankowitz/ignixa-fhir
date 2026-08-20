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

    [Theory]
    [InlineData("Patient.birthDate.lowBoundary()")]
    [InlineData("Patient.birthDate.highBoundary()")]
    public void GivenBoundaryFunction_WhenAnalyzed_ThenKeepsFocusTypeRatherThanBecomingIndeterminate(
        string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.IsValid.ShouldBeTrue();
        result.InferredTypes.Types.ShouldHaveSingleItem().TypeName.ShouldBe("date");
    }

    [Fact]
    public void GivenIndeterminateTypeInUnion_WhenNavigated_ThenStaysIndeterminateInsteadOfErroring()
    {
        var result = _analyzer.Analyze(
            "(Patient.name | Patient.descendants()).notKnownStatically",
            "Patient");

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeTrue();
    }

    [Fact]
    public void GivenDistinctTypesInUnion_WhenAnalyzed_ThenRetainsEveryBranchType()
    {
        var result = _analyzer.Analyze("Patient.name | Patient.telecom", "Patient");

        result.Errors.ShouldBeEmpty();
        result.InferredTypes.Types.Select(type => type.TypeName)
            .ShouldBe(["HumanName", "ContactPoint"], ignoreOrder: true);
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

    [Fact]
    public void GivenIndeterminateExpression_WhenCheckingValidity_ThenIsValidOrIndeterminateAdmitsIt()
    {
        var result = _analyzer.Analyze("Patient.descendants().definitelyNotAProperty", "Patient");

        result.IsValid.ShouldBeFalse();
        result.IsValidOrIndeterminate.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidExpression_WhenCheckingValidity_ThenIsValidOrIndeterminateRejectsIt()
    {
        var result = _analyzer.Analyze("Patient.definitelyNotAProperty", "Patient");

        result.IsValid.ShouldBeFalse();
        result.IsValidOrIndeterminate.ShouldBeFalse();
    }

    [Fact]
    public void GivenTypeFilterOnIndeterminateFocus_WhenAnalyzed_ThenDoesNotClaimTheFilterIsAlwaysEmpty()
    {
        var result = _analyzer.Analyze("Patient.descendants().ofType(Quantity)", "Patient");

        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldNotContain(warning =>
            warning.Contains("Type filter 'Quantity' will always be empty", StringComparison.Ordinal));
    }
}
