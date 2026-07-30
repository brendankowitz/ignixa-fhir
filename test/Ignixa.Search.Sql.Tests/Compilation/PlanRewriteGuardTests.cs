using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// SearchPlan documents <c>plan with { Query = rewritten }</c> as the way to inspect and rewrite a plan
/// before emitting, and TryCompile promises to hand failures back as data. A rewrite that leaves a stale
/// index behind must therefore land as a CompilationStage.Emit failure, not as a raw index-out-of-range
/// throw from inside the emitter and not as SQL naming a CTE that does not exist.
/// </summary>
public class PlanRewriteGuardTests
{
    [Fact]
    public async Task GivenAMatchPastTheEndOfTheCteList_WhenTryCompiling_ThenItIsAnEmitFailureNamingTheReference()
    {
        var query = await PlanFixtures.SimplePatientSearchAsync();
        var plan = new SearchPlan { Query = query with { Match = new CteRef(query.Ctes.Count) } };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
        result.Failure.Message.ShouldContain("QueryPlan.Match");
    }

    [Fact]
    public async Task GivenANegativeMatchIndex_WhenTryCompiling_ThenItIsAnEmitFailure()
    {
        var query = await PlanFixtures.SimplePatientSearchAsync();
        var plan = new SearchPlan { Query = query with { Match = new CteRef(-1) } };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
    }

    [Fact]
    public async Task GivenACteReferencingOneDefinedAfterIt_WhenTryCompiling_ThenItIsAnEmitFailure()
    {
        // T-SQL binds CTEs in order, so a forward reference emits SQL naming a CTE that is not in scope yet.
        // That is worse than a crash -- without this guard it reaches the database as invalid SQL.
        var query = await PlanFixtures.SimplePatientSearchAsync();
        var plan = new SearchPlan
        {
            Query = query with
            {
                Ctes = [new CteDefinition.Intersect(new CteRef(1), new CteRef(1)), query.Ctes[0]],
                Match = new CteRef(0),
            },
        };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
        result.Failure.Message.ShouldContain("Ctes[0].Left");
    }

    [Fact]
    public async Task GivenAnIncludeStageSeedingFromItself_WhenTryCompiling_ThenItIsAnEmitFailure()
    {
        var query = await PlanFixtures.SimplePatientSearchAsync();
        var plan = new SearchPlan { Query = query with { Includes = [StageSeededFrom([0])] } };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
        result.Failure.Message.ShouldContain("Includes[0].SeedStages");
    }

    [Fact]
    public async Task GivenAnIncludeConstraintPastTheEndOfTheCteList_WhenTryCompiling_ThenItIsAnEmitFailure()
    {
        // Include stages index their seeds against the stage list but their constraints against QueryPlan.Ctes,
        // so the two bounds are checked separately.
        var query = await PlanFixtures.SimplePatientSearchAsync();
        var stage = StageSeededFrom([]) with { Constraints = [new IncludeConstraint(103, query.Ctes.Count)] };
        var plan = new SearchPlan { Query = query with { Includes = [stage] } };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
        result.Failure.Message.ShouldContain("Includes[0].Constraints");
    }

    private static IncludeStage StageSeededFrom(IReadOnlyList<int> seedStages) => new(
        IncludeDirection.Forward,
        ReferenceSearchParamId: 55,
        SeedTypeIds: [103],
        OutputTypeIds: [105],
        SeedStages: seedStages,
        SeedFromMatch: true,
        Iterate: false,
        Limit: 1000);
}
