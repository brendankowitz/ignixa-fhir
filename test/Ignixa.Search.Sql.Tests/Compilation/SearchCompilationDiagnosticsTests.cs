using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
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
    public void GivenAnAttributedRefusal_WhenRecorded_ThenItRestampsTheOwningParameterAsFailed()
    {
        // Establishes that the restamping this PR's plan-trace guard has to avoid is real and live. Without
        // this, the test below asserts the absence of something that might never have happened.
        var (parameter, attributed) = AttributedLoweringFailure();
        var outcomes = new List<ParameterTrace> { TraceFor(parameter) };

        CompilationDiagnosticsBuilder.RecordFailure(outcomes, CompilationStage.Emit, attributed);

        outcomes[0].Outcome.ShouldBeOfType<ParameterOutcome.Failed>();
    }

    [Fact]
    public void GivenARefusalWrappedForThePlanTrace_WhenRecorded_ThenNoParameterIsMarkedFailed()
    {
        // What SearchSqlCompiler does when BuildPlanTrace refuses: wrap in a fresh exception, whose Data is
        // empty, so RecordFailure returns a failure WITHOUT restamping. That is load-bearing because the
        // compile still succeeds -- restamping would report Failed parameters on a successful result.
        //
        // This pins the mechanism rather than the wiring. The compiler's catch cannot be driven end to end
        // from a test: Lower only produces plans Describe accepts, so the catch is defence-in-depth against
        // a future explain/emit divergence -- the class of defect EmittedParameterCursor exists to surface,
        // and which did occur once (PrintMultiTypeResourceSource). If that path ever becomes reachable from
        // a lowered plan, add the end-to-end test; until then this is the strongest assertion available.
        var (parameter, attributed) = AttributedLoweringFailure();
        var outcomes = new List<ParameterTrace> { TraceFor(parameter) };

        var failure = CompilationDiagnosticsBuilder.RecordFailure(
            outcomes,
            CompilationStage.Emit,
            new NotSupportedException($"Plan trace unavailable: {attributed.Message}", attributed));

        outcomes[0].Outcome.ShouldNotBeOfType<ParameterOutcome.Failed>();
        failure.ParameterCode.ShouldBeNull();
        failure.Span.ShouldBeNull();
        failure.Exception!.InnerException.ShouldBeSameAs(attributed);
    }

    /// <summary>An exception carrying the parameter attribution the leaf dispatcher stamps on the way out.</summary>
    private static (SearchParameterInfo Parameter, Exception Failure) AttributedLoweringFailure()
    {
        var parameter = new SearchParameterInfo(
            "active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 4),
        };
        var context = new LeafContext(new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>()));

        return (parameter, Should.Throw<KeyNotFoundException>(() => LeafLoweringDispatcher.Lower(predicate, context, 103)));
    }

    /// <summary>A trace whose IR names <paramref name="parameter"/>, so RecordFailure can match it.</summary>
    private static ParameterTrace TraceFor(SearchParameterInfo parameter)
        => new(
            ordinal: 0,
            key: "active",
            keySyntax: null,
            value: "true",
            valueSyntax: null,
            ir: new SearchParameterExpression(
                parameter,
                new SearchParameterPredicateExpression(
                    parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))),
            outcome: new ParameterOutcome.Compiled(),
            dataType: SearchParamType.Token);
}
