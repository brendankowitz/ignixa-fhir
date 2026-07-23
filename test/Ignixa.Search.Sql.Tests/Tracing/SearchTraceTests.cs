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
    public async Task GivenADateApComparatorQueryWithAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnceAndProducesTheWidenedSqlGolden()
    {
        // Arrange -- unlike GivenAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnce above
        // (a plain name=Smith query whose value never reads the reference time at all), this query's
        // :ap comparator only compiles successfully if SearchCompiler's single GetUtcNow() call supplied
        // a non-null reference instant all the way to Lower's approximationReferenceTime -- Approximate
        // DateRange.Widen throws InvalidOperationException otherwise, so an absent Failure here is itself
        // proof the captured value reached the widening logic, not merely that it was read once.
        var fixedTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var trace = await SearchTraceFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert -- the captured reference instant reached Lower and widened the :ap range successfully
        provider.CallCount.ShouldBe(1);
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull();

        // Assert -- complete SQL golden: the same shape already pinned in EndToEndCompilationTests'
        // Lower.Run-based date :ap case, proving the SearchCompiler orchestration boundary (Build ->
        // Resolve -> Lower -> Emit, with the reference time captured once up front) reaches the
        // identical emitted SQL as calling Lower.Run directly with that same widened reference.
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.DateTimeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 203 AND (StartDateTime <= @p0 AND EndDateTime >= @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenTheSameDateApComparatorQueryCompiledTwiceWithTheSameFixedTimeProvider_WhenCompiled_ThenTheTracedSqlIsByteIdentical()
    {
        // Arrange -- two independent CompileWithTimeProviderAsync calls, each capturing its own
        // GetUtcNow() from the same FixedTimeProvider instance (proven by CallCount reaching 2, one per
        // compile, never more), must reach byte-identical widened SQL -- proving the compiler boundary's
        // determinism holds across separate compiles sharing one fixed clock, not just within a single one.
        var fixedTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var trace1 = await SearchTraceFixtures.TraceObservationDateApWithTimeProviderAsync(provider);
        var trace2 = await SearchTraceFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert
        provider.CallCount.ShouldBe(2);
        trace1.Failure.ShouldBeNull();
        trace2.Failure.ShouldBeNull();
        trace1.Sql.ShouldNotBeNull();
        trace2.Sql.ShouldNotBeNull();
        trace2.Sql!.Sql.ShouldBe(trace1.Sql!.Sql);
    }

    [Fact]
    public async Task GivenTheOriginalOverloadWithPositionalDefault_WhenCompiled_ThenItCompilesAndDelegatesSuccessfully()
    {
        // Arrange — exercises the original 7-parameter CompileAsync with a positional `default`
        // as the final argument, proving the pre-existing signature compiles unambiguously.
        var trace = await SearchTraceFixtures.TracePatientNameSmithWithCancellationTokenAsync(default);

        // Assert
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAPreCancelledToken_WhenCompiled_ThenTheCancellationReachesTheResolver()
    {
        // Arrange — a pre-cancelled token must propagate through CompileAsync into Resolve and
        // reach the resolver, proving the delegation does not substitute CancellationToken.None.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            SearchTraceFixtures.TracePatientNameSmithWithCancellationCheckAsync(cts.Token));
    }

    [Fact]
    public async Task GivenAParameterThatLoweredToAnUnsatisfiablePredicate_WhenTraced_ThenItIsReportedAsAKnownMiss()
    {
        // Arrange & Act — a token system no resource uses compiles cleanly but can never match, which is
        // a property of the data, not a failure of the query; reporting it as Compiled would hide it.
        var trace = await SearchTraceFixtures.TraceUnresolvableTokenSystemAsync();

        // Assert
        var parameter = trace.Parameters.ShouldHaveSingleItem();
        var knownMiss = parameter.Outcome.ShouldBeOfType<ParameterOutcome.KnownMiss>();
        knownMiss.Reason.ShouldBe("No resource uses the token system 'http://unknown.org/mrn'.");
        knownMiss.Span.ShouldBe(new SourceSpan(SourceOrigin.Value, 0, 31));
        trace.Failure.ShouldBeNull();
        trace.Plan.ShouldNotBeNull();
        trace.Sql.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenOneSatisfiableAndOneUnsatisfiableParameter_WhenTraced_ThenOnlyTheUnsatisfiableOneIsAKnownMiss()
    {
        // Arrange & Act
        var trace = await SearchTraceFixtures.TraceSatisfiableAndUnresolvableTokenSystemAsync();

        // Assert — the miss is attributed to its own parameter, and the other stays Compiled
        trace.Parameters.Count.ShouldBe(2);
        trace.Parameters[0].Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        var knownMiss = trace.Parameters[1].Outcome.ShouldBeOfType<ParameterOutcome.KnownMiss>();
        knownMiss.Reason.ShouldBe("No resource uses the token system 'http://unknown.org/mrn'.");
        knownMiss.Span.ShouldBe(new SourceSpan(SourceOrigin.Value, 10, 28));
    }

    [Fact]
    public async Task GivenAllParametersSatisfiable_WhenTraced_ThenNoneAreReportedAsAKnownMiss()
    {
        // Arrange & Act
        var trace = await SearchTraceFixtures.TracePatientActiveTrueAsync();

        // Assert
        trace.Parameters.ShouldAllBe(p => !(p.Outcome is ParameterOutcome.KnownMiss));
    }

    [Fact]
    public async Task GivenAMovingClock_WhenCompiled_ThenTheReferenceInstantIsReadExactlyOnceSoEveryConsumerSeesTheSameValue()
    {
        // Arrange -- a clock that advances a day on every read. Against the fixed provider used above,
        // a compile that read the clock twice would still widen consistently and only trip the call
        // count; here a second read genuinely returns a different instant, so "captured once" and "every
        // consumer saw the same instant" become the same assertion rather than two hopeful ones.
        var provider = new IncrementingTimeProvider(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1));

        // Act
        var trace = await SearchTraceFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert
        provider.CallCount.ShouldBe(1);
        trace.Failure.ShouldBeNull();
        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.DateTimeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 203 AND (StartDateTime <= @p0 AND EndDateTime >= @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    /// <summary>
    /// Returns a different instant on every <see cref="GetUtcNow"/> call, so a compile that captures the
    /// reference time more than once cannot silently agree with itself.
    /// </summary>
    private sealed class IncrementingTimeProvider(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => start + (step * CallCount++);
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
