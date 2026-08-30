using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class IndeterminateTypeAnalysisTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "birthDate": "1980-04"
        }
        """;

    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R5.GetSchemaProvider());
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

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

    /// <summary>
    /// The boundary functions stay analysable, which is the property this pins; they do not keep the
    /// focus's own type. FHIRPath defines the boundary of a <c>date</c> as a <c>dateTime</c>, so
    /// <c>Patient.birthDate.lowBoundary()</c> narrows from <c>date</c> to <c>dateTime</c>.
    /// </summary>
    /// <remarks>
    /// The expected type is asserted against the evaluator rather than against the analyzer's own
    /// answer. This file previously pinned <c>date</c> here, which was never what either engine
    /// produced, and nothing failed because nothing evaluated the expression.
    /// </remarks>
    [Theory]
    [InlineData("Patient.birthDate.lowBoundary()")]
    [InlineData("Patient.birthDate.highBoundary()")]
    public void GivenBoundaryFunction_WhenAnalyzed_ThenInfersTheEvaluatorsBoundaryTypeRatherThanBecomingIndeterminate(
        string expression)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);

        // Act
        var result = _analyzer.Analyze(expression, "Patient");
        var evaluated = _evaluator
            .Evaluate(
                subject,
                _parser.Parse(expression),
                new EvaluationContext { Resource = subject, RootResource = subject, Schema = schema })
            .ToList();

        // Assert
        evaluated.ShouldHaveSingleItem().InstanceType.ShouldBe("dateTime");
        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.IsValid.ShouldBeTrue();
        result.InferredTypes.Types.ShouldHaveSingleItem().TypeName.ShouldBe("dateTime");
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
