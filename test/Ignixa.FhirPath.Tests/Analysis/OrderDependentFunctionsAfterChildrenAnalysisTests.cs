using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests.Analysis;

public class OrderDependentFunctionsAfterChildrenAnalysisTests
{
    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R4.GetSchemaProvider());

    [Theory]
    [InlineData("Patient.children().skip(1)")]
    [InlineData("Patient.children().take(2)")]
    [InlineData("Patient.children().tail()")]
    [InlineData("Patient.descendants().skip(1)")]
    [InlineData("Patient.descendants().take(1)")]
    [InlineData("Patient.descendants().tail()")]
    public void GivenPositionalFunctionAfterUnordered_WhenAnalyzing_ThenReturnsError(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Error &&
            issue.Message.Contains("positional", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Patient.children().where(true).skip(1)")]
    [InlineData("Patient.children().ofType(HumanName).take(1)")]
    [InlineData("Patient.descendants().where(true).tail()")]
    public void GivenPositionalFunctionAfterUnorderedIndirect_WhenAnalyzing_ThenReturnsError(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Error &&
            issue.Message.Contains("positional", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Patient.children().first()")]
    [InlineData("Patient.children().last()")]
    [InlineData("Patient.descendants().first()")]
    [InlineData("Patient.descendants().last()")]
    public void GivenExistentialFunctionAfterUnordered_WhenAnalyzing_ThenReturnsWarning(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Warning &&
            issue.Message.Contains("non-deterministic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenIndexerAfterChildren_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.children()[0]", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Error &&
            issue.Message.Contains("Indexer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenIndexerAfterChildrenIndirect_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.children().where(true)[0]", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Error &&
            issue.Message.Contains("Indexer", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Patient.children().sort().skip(1)")]
    [InlineData("Patient.children().sort().first()")]
    [InlineData("Patient.children().sort()[0]")]
    [InlineData("Patient.children().sortBy($this).skip(1)")]
    [InlineData("Patient.children().sortBy($this).first()")]
    [InlineData("Patient.children().sortBy($this)[0]")]
    public void GivenSortBreaksChain_WhenAnalyzing_ThenNoOrderIssues(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        Assert.DoesNotContain(result.Issues, issue =>
            issue.Message.Contains("unordered", StringComparison.OrdinalIgnoreCase) ||
            issue.Message.Contains("positional", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Patient.name.skip(1)")]
    [InlineData("Patient.name.first()")]
    [InlineData("Patient.name[0]")]
    public void GivenOrderedPathAccess_WhenAnalyzing_ThenNoOrderIssues(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        Assert.DoesNotContain(result.Issues, issue =>
            issue.Message.Contains("unordered", StringComparison.OrdinalIgnoreCase) ||
            issue.Message.Contains("positional", StringComparison.OrdinalIgnoreCase));
    }
}
