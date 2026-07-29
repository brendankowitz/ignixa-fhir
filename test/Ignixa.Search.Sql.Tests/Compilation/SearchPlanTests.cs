using Ignixa.Search.Sql;
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
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.Full,
        };

        plan.Compile().Diagnostics!.SqlTextRanges.ShouldNotBeEmpty();
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
