using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchPlanTests
{
    [Fact]
    public async Task GivenAPlan_WhenCompilingIt_ThenTheSqlAndTheOriginatingPlanAreBothReturned()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.SimplePatientSearchAsync() };

        var compiled = plan.Compile();

        compiled.Sql.ShouldNotBeNullOrWhiteSpace();
        compiled.Query.ShouldBeSameAs(plan.Query);
    }

    [Fact]
    public async Task GivenAPlan_WhenRewritingTheQueryWithAWithExpression_ThenTheOriginalPlanIsUnchanged()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.SimplePatientSearchAsync() };

        var rewritten = plan with { Query = plan.Query with { Top = 5 } };

        rewritten.Query.Top.ShouldBe(5);
        plan.Query.Top.ShouldBeNull();
    }

    [Fact]
    public async Task GivenARewrittenPlan_WhenCompilingBoth_ThenTheRewriteReachesTheEmittedSql()
    {
        // The whole point of splitting CreatePlanAsync from Compile is that a caller can rewrite the plan
        // in between and have the change reach the SQL. Asserting the `with` alone would only test the
        // compiler's record semantics.
        var plan = new SearchPlan { Query = await PlanFixtures.SimplePatientSearchAsync() };
        var rewritten = plan with { Query = plan.Query with { Top = 5 } };

        var original = plan.Compile();
        var capped = rewritten.Compile();

        capped.Sql.ShouldNotBe(original.Sql);
        capped.Sql.ShouldContain("TOP");
        original.Sql.ShouldNotContain("TOP");
    }

    [Fact]
    public async Task GivenAPlanThatCannotEmit_WhenCompilingIt_ThenItThrowsASearchCompilationExceptionAtTheEmitStage()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.IncoherentPlanAsync() };

        var exception = Should.Throw<SearchCompilationException>(() => plan.Compile());

        exception.Failure.Stage.ShouldBe(CompilationStage.Emit);
    }

    [Fact]
    public async Task GivenAPlanThatCannotEmit_WhenTryCompilingIt_ThenItReturnsAFailureRatherThanThrowing()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.IncoherentPlanAsync() };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
    }

    [Fact]
    public async Task GivenAPlanAtDiagnosticsLevelNone_WhenCompilingIt_ThenNoDiagnosticsAreAttached()
    {
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.None,
        };

        plan.Compile().Diagnostics.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAPlanAtDiagnosticsLevelFull_WhenCompilingIt_ThenSqlTextRangesAreAttached()
    {
        // Created through the compiler, not hand-constructed: a plan that never ran Build/Resolve/Lower has no
        // diagnostics to merge into, and Compile refuses to fabricate an empty envelope for it.
        var compiler = CompilerFixtures.ForPatient();
        var result = await compiler.TryCreatePlanAsync(
            "Patient",
            [new QueryParameter("name", "smith")],
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full });

        result.Succeeded.ShouldBeTrue();
        result.Plan!.Compile().Diagnostics!.SqlTextRanges.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenAHandConstructedPlanAtAFullDiagnosticsLevel_WhenCompilingIt_ThenNoEmptyDiagnosticsAreFabricated()
    {
        // A plan built directly carries no Diagnostics because it never ran the earlier stages. Synthesising
        // an empty record here would be type-indistinguishable from a real compile in which nothing resolved,
        // so a caller reading "0 parameters resolved" could not tell a bug from a plan that skipped the stage.
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.Full,
        };

        plan.Compile().Diagnostics.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAPageBoundaryThatDoesNotMatchTheSortPhase_WhenTryCreatingThePlan_ThenItFailsAtLowering()
    {
        // PageSpec.Boundary is client input decoded from a continuation token, so its length has to be checked
        // against the phase's active key count. Lowering owns that check: a caller gets the failure from plan
        // creation, before it holds a SearchPlan it believes is emittable.
        var compiler = CompilerFixtures.ForPatient();
        var options = new SearchPlanOptions
        {
            Shape = new ResultShape.Matches(new SearchPaging.Keyset(Boundary: new PageSpec(
                [new SqlParameterRef("Smith")], new SqlParameterRef((short)103), new SqlParameterRef(9000L)))),
        };

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")], options);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
        result.Failure.Exception.ShouldBeOfType<NotSupportedException>();
        result.Failure.Message.ShouldContain("1 value(s)");
    }

    [Fact]
    public async Task GivenAPlanAtDiagnosticsLevelParameters_WhenCompilingIt_ThenPlanPhaseDiagnosticsSurviveWithoutTextRanges()
    {
        // The merge in TryCompile rebuilds the diagnostics record to attach SQL text ranges. At Parameters
        // level there are no ranges to attach, and the plan-phase members must come through untouched.
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.Parameters,
            Diagnostics = new SearchCompilationDiagnostics
            {
                Implicit = [new ImplicitParameter("_count", "10", "server default")],
            },
        };

        var diagnostics = plan.Compile().Diagnostics;

        diagnostics.ShouldNotBeNull();
        diagnostics.Implicit.ShouldHaveSingleItem().Name.ShouldBe("_count");
        diagnostics.SqlTextRanges.ShouldBeEmpty();
    }
}
