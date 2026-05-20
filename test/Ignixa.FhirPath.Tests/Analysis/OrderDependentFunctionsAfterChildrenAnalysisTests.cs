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

    [Fact]
    public void GivenSkipAfterChildren_WhenAnalyzing_ThenReturnsError()
    {
        var result = _analyzer.Analyze("Patient.children().skip(1)", "Patient");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Severity == ValidationIssueSeverity.Error &&
            issue.Message.Contains("skip()", StringComparison.Ordinal) &&
            issue.Message.Contains("children()", StringComparison.Ordinal));
    }
}
