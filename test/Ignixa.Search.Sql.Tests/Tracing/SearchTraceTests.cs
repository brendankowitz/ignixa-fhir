using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
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

            trace.Sql!.Ranges.ShouldContain(r => r.Label == SqlLabels.CteLabel(i), $"{scenario}: {SqlLabels.CteLabel(i)} has no SQL text range");
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

        trace.Failure.ShouldNotBeNull($"{scenario}: the trace records no failure");
        trace.Failure!.Stage.ShouldBe(TraceStage.Lower);
    }

    [Fact]
    public async Task GivenTwoParametersSharingASpan_WhenOneFailsToLower_ThenOnlyThatParameterIsMarkedFailed()
    {
        var trace = await SearchTraceFixtures.TraceCollidingSpansWithOneFailureAsync();

        var gender = trace.Parameters.Single(p => p.Key == "gender");
        var name = trace.Parameters.Single(p => p.Key == "name");

        gender.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>("the innocent same-length neighbour was smeared with the failure");
        name.Outcome.ShouldBeOfType<ParameterOutcome.Failed>();
    }

    [Fact]
    public async Task GivenAResourceColumnParameter_WhenTraced_ThenItIsNotReportedUnresolvedAndTheQueryStillCompiles()
    {
        var trace = await SearchTraceFixtures.TraceResourceColumnIdAsync();

        trace.Parameters.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull("_id needs no SearchParamId, so Lower should have run");
        trace.Sql.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAnUnresolvedChainReferenceParameter_WhenTraced_ThenItIsAttributedToTheChainsParameter()
    {
        var trace = await SearchTraceFixtures.TraceUnresolvedChainReferenceParameterAsync();

        var failed = trace.Parameters.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Failed>();
        failed.Stage.ShouldBe(TraceStage.Resolve);
        failed.Message.ShouldContain("organization");
    }

    [Fact]
    public async Task GivenAnUnresolvedIncludeOwnedByNoParameterTrace_WhenTraced_ThenTheTraceStillStatesWhyThePlanIsMissing()
    {
        var trace = await SearchTraceFixtures.TraceUnresolvedIncludeAsync();

        trace.Plan.ShouldBeNull();
        trace.Parameters.ShouldAllBe(p => p.Outcome is ParameterOutcome.Compiled);

        trace.Failure.ShouldNotBeNull("an absent plan with every parameter Compiled is an unexplained trace");
        trace.Failure!.Stage.ShouldBe(TraceStage.Resolve);
        trace.Failure.Message.ShouldContain("organization");
    }

    [Fact]
    public async Task GivenAFailureNamingNoParameter_WhenTraced_ThenItsMessageSurvivesOnTheTrace()
    {
        var trace = await SearchTraceFixtures.TraceSortKeyCapExceededAsync();

        trace.Plan.ShouldBeNull();
        trace.Parameters.ShouldAllBe(p => p.Outcome is ParameterOutcome.Compiled);

        trace.Failure.ShouldNotBeNull("the sort-key cap message would otherwise be lost entirely");
        trace.Failure!.Stage.ShouldBe(TraceStage.Lower);
        trace.Failure.Message.ShouldContain("_sort supports at most 3 keys");
        trace.Failure.Span.ShouldBeNull();
    }

    [Fact]
    public async Task GivenALeafCte_WhenTraced_ThenItContributesOnlyItsOwnParameter()
    {
        var trace = await SearchTraceFixtures.TracePatientActiveTrueAsync();

        trace.Plan.ShouldNotBeNull();
        var leaf = trace.Plan!.Ctes.First(c => c.ParameterOrdinal == 0);

        leaf.ContributingOrdinals.ShouldBe([0]);
    }

    [Fact]
    public async Task GivenAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnce()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var trace = await SearchTraceFixtures.TracePatientNameSmithWithTimeProviderAsync(provider);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull();
        provider.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenTheOriginalOverloadWithPositionalCancellationToken_WhenCompiled_ThenItDelegatesToTheTimeProviderOverload()
    {
        // Arrange — exercises the pre-existing 7-parameter overload with a positional
        // CancellationToken at position 7, proving the old signature compiles and delegates.
        var trace = await SearchTraceFixtures.TracePatientNameSmithWithCancellationTokenAsync(CancellationToken.None);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull();
    }

    private sealed class CountingFixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return utcNow;
        }
    }
}
