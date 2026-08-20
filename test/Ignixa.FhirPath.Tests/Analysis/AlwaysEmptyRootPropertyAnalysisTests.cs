using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

/// <summary>
/// Covers root-relative names that some other resource type declares. These are decidable, not
/// unanalysable: the analyzer knows the root type concretely and knows it has no such element.
/// </summary>
public class AlwaysEmptyRootPropertyAnalysisTests
{
    private readonly FhirPathAnalyzer _analyzer = new(FhirVersion.R5.GetSchemaProvider());

    [Theory]
    [InlineData("status")]
    [InlineData("vaccineCode")]
    [InlineData("requestedPeriod")]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnConcreteRoot_ThenReportsAlwaysEmptyNotIndeterminate(
        string propertyName)
    {
        var result = _analyzer.Analyze(propertyName, "Patient");

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.Warnings.ShouldContain(warning =>
            warning.Contains("will always be empty on root type 'Patient'", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnAbstractRoot_ThenReportsIndeterminate()
    {
        var result = _analyzer.Analyze("status", "Resource");

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeTrue();
        result.InferredTypes.Types.ShouldHaveSingleItem().IsUnknown.ShouldBeTrue();
    }

    [Fact]
    public void GivenPropertyNoResourceDeclares_WhenAnalyzed_ThenStillReportsError()
    {
        var result = _analyzer.Analyze("nosuchpropanywhere", "Patient");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("'nosuchpropanywhere' not found", StringComparison.Ordinal));
    }
}
