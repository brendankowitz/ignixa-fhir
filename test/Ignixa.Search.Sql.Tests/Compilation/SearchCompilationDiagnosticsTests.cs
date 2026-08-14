using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchCompilationDiagnosticsTests
{
    [Fact]
    public void GivenDefaultDiagnostics_WhenReadingThem_ThenTheCollectionsAreEmptyRatherThanNull()
    {
        var diagnostics = new SearchCompilationDiagnostics();

        diagnostics.Parameters.ShouldBeEmpty();
        diagnostics.Implicit.ShouldBeEmpty();
        diagnostics.SqlTextRanges.ShouldBeEmpty();
        diagnostics.PlanTrace.ShouldBeNull();
    }
    [Fact]
    public void GivenDefaultDiagnostics_WhenInspected_ThenPlanTraceFailureIsNull()
    {
        // PlanTraceFailure exists so an absent trace can say why. Null is "the trace was built, or none was
        // requested" -- the two states a caller must be able to tell apart from "it refused".
        var diagnostics = new SearchCompilationDiagnostics();

        diagnostics.PlanTraceFailure.ShouldBeNull();
    }

    [Fact]
    public void GivenATraceFailure_WhenCarried_ThenItTravelsWithTheDiagnosticsWithoutMarkingAnyParameterFailed()
    {
        // The regression this pins: a plan-trace refusal must not restamp per-parameter outcomes, or a
        // SUCCESSFUL compile reports Failed parameters. The compiler wraps the refusal in a fresh exception
        // precisely so RecordFailure finds no parameter to attribute it to; this asserts the resulting shape.
        var failure = new SearchCompilationFailure(
            CompilationStage.Emit,
            "Plan trace unavailable: something refused",
            ParameterCode: null,
            Span: null,
            new NotSupportedException("something refused"));

        var diagnostics = new SearchCompilationDiagnostics { PlanTraceFailure = failure };

        diagnostics.PlanTraceFailure.ShouldBe(failure);
        diagnostics.PlanTraceFailure!.Message.ShouldStartWith("Plan trace unavailable:");
        diagnostics.PlanTraceFailure.ParameterCode.ShouldBeNull();
        diagnostics.Parameters.ShouldBeEmpty();
        diagnostics.PlanTrace.ShouldBeNull();
    }
}