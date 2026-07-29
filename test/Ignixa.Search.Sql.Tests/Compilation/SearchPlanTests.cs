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
        compiled.Plan.ShouldBeSameAs(plan.Query);
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
}
