using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class SearchTraceTests
{
    public static TheoryData<string, Func<Task<SearchTrace>>, int[]> ChainCompletenessCases()
    {
        var data = new TheoryData<string, Func<Task<SearchTrace>>, int[]>();
        data.Add("leaf", SearchTraceFixtures.TracePatientActiveTrueAsync, [0]);
        data.Add("composite", SearchTraceFixtures.TraceObservationTokenTokenCompositeAsync, [0]);
        data.Add("chain", SearchTraceFixtures.TracePatientOrganizationNameChainAsync, [0]);
        data.Add("include", SearchTraceFixtures.TracePatientActiveWithIncludeAsync, [0]);
        data.Add("sort", SearchTraceFixtures.TracePatientActiveWithSortAsync, [0]);
        data.Add(":not", SearchTraceFixtures.TracePatientNameNotAsync, [0]);
        data.Add(":missing", SearchTraceFixtures.TracePatientNameMissingAsync, []);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChainCompletenessCases))]
    public async Task GivenEachSupportedShape_WhenTraced_ThenSpansCtesAndSqlRangesLineUp(
        string scenario, Func<Task<SearchTrace>> build, int[] expectedOrdinalIndices)
    {
        var trace = await build();

        foreach (var parameter in trace.Parameters)
        {
            if (parameter.Ir is null)
            {
                continue;
            }

            foreach (var node in Flatten(parameter.Ir))
            {
                switch (node)
                {
                    case SearchParameterPredicateExpression predicate:
                        predicate.Span.ShouldNotBeNull($"{scenario}: predicate for '{predicate.Parameter.Code}' has no span");
                        break;
                    case CompositeComponentExpression component:
                        component.Span.ShouldNotBeNull($"{scenario}: composite component '{component.ComponentSearchParameter.Code}' has no span");
                        break;
                }
            }
        }

        trace.Plan.ShouldNotBeNull($"{scenario}: expected a plan");
        var ctes = trace.Plan!.Ctes;

        for (var i = 0; i < ctes.Count; i++)
        {
            if (expectedOrdinalIndices.Contains(i))
            {
                ctes[i].ParameterOrdinal.ShouldNotBeNull($"{scenario}: cte{i} should have a parameter ordinal");
            }
            else
            {
                ctes[i].ParameterOrdinal.ShouldBeNull($"{scenario}: cte{i} should be exempt from provenance");
            }

            trace.Sql!.Ranges.ShouldContain(r => r.Label == $"cte{i}", $"{scenario}: cte{i} has no SQL text range");
        }
    }

    private static IEnumerable<Expression> Flatten(Expression node)
    {
        yield return node;

        IReadOnlyList<Expression> children = node switch
        {
            MultiaryExpression m => m.Expressions,
            UnionExpression u => u.Expressions,
            NotExpression n => [n.Expression],
            SearchParameterExpression sp => [sp.Expression],
            ChainedExpression c => [c.Expression],
            CompositeComponentExpression cc => [cc.WrappedExpression],
            _ => [],
        };

        foreach (var child in children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public async Task GivenALeafSearch_WhenTraced_ThenTheChainReachesFromSpanToSqlRange()
    {
        var trace = await SearchTraceFixtures.TracePatientNameSmithAsync();

        var parameter = trace.Parameters.ShouldHaveSingleItem();
        parameter.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        parameter.Ir.ShouldNotBeNull();

        trace.Plan.ShouldNotBeNull();
        trace.Plan!.Ctes.ShouldContain(c => c.ParameterOrdinal == parameter.Ordinal);

        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Ranges.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenAnUnregisteredParameter_WhenTraced_ThenItIsReportedAtTheResolveStage()
    {
        var trace = await SearchTraceFixtures.TraceUnregisteredParameterAsync();

        var failed = trace.Parameters
            .Select(p => p.Outcome)
            .OfType<ParameterOutcome.Failed>()
            .ShouldHaveSingleItem();

        failed.Stage.ShouldBe(TraceStage.Resolve);
    }

    public static TheoryData<string, Func<Task<SearchTrace>>> LowerFailureCases() => new()
    {
        { "leaf", SearchTraceFixtures.TraceUnsupportedLeafValueAsync },
        { ":not leaf", SearchTraceFixtures.TraceUnsupportedNotLeafValueAsync },
        { "composite", SearchTraceFixtures.TraceUnsupportedCompositeAsync },
    };

    [Theory]
    [MemberData(nameof(LowerFailureCases))]
    public async Task GivenAShapeLowerCannotHandle_WhenTraced_ThenItIsAttributedToTheOwningParameterAtTheLowerStage(
        string scenario, Func<Task<SearchTrace>> build)
    {
        var trace = await build();

        trace.Plan.ShouldBeNull($"{scenario}: Lower should not have produced a plan");
        trace.Sql.ShouldBeNull($"{scenario}: Emit should never have run");

        var parameter = trace.Parameters.ShouldHaveSingleItem();
        var failed = parameter.Outcome.ShouldBeOfType<ParameterOutcome.Failed>($"{scenario}: the failure was not attributed to its parameter");
        failed.Stage.ShouldBe(TraceStage.Lower);
        failed.Span.ShouldNotBeNull($"{scenario}: the attributed failure carries no source span");
        failed.Message.ShouldNotBeNullOrWhiteSpace();
    }
}
