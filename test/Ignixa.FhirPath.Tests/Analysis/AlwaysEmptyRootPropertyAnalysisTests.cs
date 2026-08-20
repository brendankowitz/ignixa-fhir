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
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
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

    /// <summary>
    /// <c>IsAbstract</c> is not sufficient to make a root's runtime type unknowable: the abstract set also
    /// contains <c>Element</c>, <c>BackboneElement</c>, <c>DataType</c>, <c>BackboneType</c> and
    /// <c>PrimitiveType</c>, none of which a resource's top-level elements can appear on. A runtime
    /// <c>Element</c> is never an <c>Appointment</c>, so the miss is as decidable as it is on
    /// <c>Patient</c>. Not reachable through the corpus, whose roots are always resources.
    /// </summary>
    [Theory]
    [InlineData("Element")]
    [InlineData("BackboneElement")]
    [InlineData("DataType")]
    [InlineData("BackboneType")]
    [InlineData("PrimitiveType")]
    public void GivenPropertyOfAnotherResource_WhenAnalyzedOnAbstractNonResourceRoot_ThenReportsAlwaysEmpty(
        string rootType)
    {
        var result = _analyzer.Analyze("status", rootType);

        result.Errors.ShouldBeEmpty();
        result.IsIndeterminate.ShouldBeFalse();
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    /// <summary>
    /// The always-empty branch contributes no type, which is the precondition for the cascade the corpus
    /// observes: the next navigation step then runs against an emptied focus.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenAnalyzed_ThenInfersNoTypes()
    {
        var result = _analyzer.Analyze("requestedPeriod", "Patient");

        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
        result.InferredTypes.Types.ShouldBeEmpty();
    }

    /// <summary>
    /// Pins the cascade itself. The error is raised by the empty-context guard in <c>VisitChild</c>, not by
    /// the reclassification, and that guard is over-strict against strict FHIRPath, where navigation off an
    /// empty collection yields empty. Pinned so a deliberate change to it is visible here rather than as an
    /// unexplained corpus movement.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenNavigatedFurther_ThenCascadesToEmptyContextError()
    {
        var result = _analyzer.Analyze("requestedPeriod.start", "Patient");

        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
        result.IsIndeterminate.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("Cannot access child 'start' on empty context", StringComparison.Ordinal));
    }

    /// <summary>
    /// The decision recorded for a probable typo: a decidable miss is not an error, so <c>IsValid</c> stays
    /// true and the always-empty predicate is the only signal that distinguishes it from a correct
    /// expression. A caller wanting typo rejection must consult that predicate deliberately.
    /// </summary>
    [Fact]
    public void GivenAlwaysEmptyRootProperty_WhenAnalyzed_ThenOnlyTheAlwaysEmptyPredicateSignalsIt()
    {
        var result = _analyzer.Analyze("status", "Patient");

        result.IsValid.ShouldBeTrue();
        result.IsValidOrIndeterminate.ShouldBeTrue();
        result.IsIndeterminate.ShouldBeFalse();
        result.HasAlwaysEmptySubexpression.ShouldBeTrue();
    }

    /// <summary>
    /// Records the pre-existing asymmetry: the classifier keys on root-relativity, not on decidability, so
    /// only a bare name reaches the always-empty branch. Every qualified form of the same fact — including
    /// the resource-qualified <c>Patient.status</c>, which most authors would call the same expression —
    /// stays an error. Reconciling this would reclassify every "property not found" error the analyzer
    /// raises, so it is pinned rather than changed.
    /// </summary>
    [Theory]
    [InlineData("Patient.status")]
    [InlineData("$this.status")]
    [InlineData("%resource.status")]
    [InlineData("Patient.where(status='active')")]
    public void GivenSameMissQualifiedByFocus_WhenAnalyzed_ThenStillReportsError(string expression)
    {
        var result = _analyzer.Analyze(expression, "Patient");

        result.HasAlwaysEmptySubexpression.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Contains("Property 'status' not found", StringComparison.Ordinal));
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
