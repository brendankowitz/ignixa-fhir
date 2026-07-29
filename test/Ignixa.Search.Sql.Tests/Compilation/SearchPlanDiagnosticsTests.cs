using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchPlanDiagnosticsTests
{
    public static TheoryData<string, Func<Task<SearchPlanResult>>, int[]> ChainCompletenessCases()
    {
        var data = new TheoryData<string, Func<Task<SearchPlanResult>>, int[]>();
        data.Add("leaf", CompilationFixtures.TracePatientActiveTrueAsync, [0]);
        data.Add("composite", CompilationFixtures.TraceObservationTokenTokenCompositeAsync, [0]);
        data.Add("chain", CompilationFixtures.TracePatientOrganizationNameChainAsync, [0]);
        data.Add("include", CompilationFixtures.TracePatientActiveWithIncludeAsync, [0]);
        data.Add("sort", CompilationFixtures.TracePatientActiveWithSortAsync, [0]);
        data.Add(":not", CompilationFixtures.TracePatientNameNotAsync, [0]);
        data.Add(":missing", CompilationFixtures.TracePatientNameMissingAsync, []);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChainCompletenessCases))]
    public async Task GivenEachSupportedShape_WhenTraced_ThenSpansCtesAndSqlRangesLineUp(
        string scenario, Func<Task<SearchPlanResult>> build, int[] expectedOrdinalIndices)
    {
        var result = await build();

        foreach (var parameter in result.Plan!.Diagnostics!.Parameters)
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

        result.Plan!.Diagnostics!.PlanTrace.ShouldNotBeNull($"{scenario}: expected a plan");
        var compiled = result.Plan!.Compile();
        var ctes = result.Plan!.Diagnostics!.PlanTrace!.Ctes;

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

            compiled.Diagnostics!.SqlTextRanges.ShouldContain(r => r.Label == SqlLabels.CteLabel(i), $"{scenario}: {SqlLabels.CteLabel(i)} has no SQL text range");
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
        var result = await CompilationFixtures.TracePatientNameSmithAsync();

        var parameter = result.Plan!.Diagnostics!.Parameters.ShouldHaveSingleItem();
        parameter.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        parameter.Ir.ShouldNotBeNull();

        result.Plan!.Diagnostics!.PlanTrace.ShouldNotBeNull();
        result.Plan!.Diagnostics!.PlanTrace!.Ctes.ShouldContain(c => c.ParameterOrdinal == parameter.Ordinal);

        var compiled = result.Plan!.Compile();
        compiled.Diagnostics!.SqlTextRanges.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenAnUnregisteredParameter_WhenTraced_ThenItIsReportedAtTheResolveStage()
    {
        var result = await CompilationFixtures.TraceUnregisteredParameterAsync();

        result.Succeeded.ShouldBeFalse();
        var failed = result.Failure!.Diagnostics!.Parameters
            .Select(p => p.Outcome)
            .OfType<ParameterOutcome.Failed>()
            .ShouldHaveSingleItem();

        failed.Stage.ShouldBe(TraceStage.Resolve);
    }

    public static TheoryData<string, Func<Task<SearchPlanResult>>> LowerFailureCases() => new()
    {
        { "leaf", CompilationFixtures.TraceUnsupportedLeafValueAsync },
        { ":not leaf", CompilationFixtures.TraceUnsupportedNotLeafValueAsync },
        { "composite", CompilationFixtures.TraceUnsupportedCompositeAsync },
    };

    [Theory]
    [MemberData(nameof(LowerFailureCases))]
    public async Task GivenAShapeLowerCannotHandle_WhenTraced_ThenItIsAttributedToTheOwningParameterAtTheLowerStage(
        string scenario, Func<Task<SearchPlanResult>> build)
    {
        var result = await build();

        result.Succeeded.ShouldBeFalse($"{scenario}: Lower should not have produced a plan");
        result.Plan.ShouldBeNull($"{scenario}: Emit should never have run");

        var parameter = result.Failure!.Diagnostics!.Parameters.ShouldHaveSingleItem();
        var failed = parameter.Outcome.ShouldBeOfType<ParameterOutcome.Failed>($"{scenario}: the failure was not attributed to its parameter");
        failed.Stage.ShouldBe(TraceStage.Lower);
        failed.Span.ShouldNotBeNull($"{scenario}: the attributed failure carries no source span");
        failed.Message.ShouldNotBeNullOrWhiteSpace();

        result.Failure.ShouldNotBeNull($"{scenario}: the result records no failure");
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
    }

    [Fact]
    public async Task GivenTwoParametersSharingASpan_WhenOneFailsToLower_ThenOnlyThatParameterIsMarkedFailed()
    {
        var result = await CompilationFixtures.TraceCollidingSpansWithOneFailureAsync();

        result.Succeeded.ShouldBeFalse();
        var gender = result.Failure!.Diagnostics!.Parameters.Single(p => p.Key == "gender");
        var name = result.Failure!.Diagnostics!.Parameters.Single(p => p.Key == "name");

        gender.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>("the innocent same-length neighbour was smeared with the failure");
        name.Outcome.ShouldBeOfType<ParameterOutcome.Failed>();
    }

    [Fact]
    public async Task GivenAResourceColumnParameter_WhenTraced_ThenItIsNotReportedUnresolvedAndTheQueryStillCompiles()
    {
        var result = await CompilationFixtures.TraceResourceColumnIdAsync();

        result.Plan!.Diagnostics!.Parameters.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        result.Failure.ShouldBeNull();
        result.Plan!.Diagnostics!.PlanTrace.ShouldNotBeNull("_id needs no SearchParamId, so Lower should have run");
        result.Plan!.Compile().Sql.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenAnUnresolvedChainReferenceParameter_WhenTraced_ThenItIsAttributedToTheChainsParameter()
    {
        var result = await CompilationFixtures.TraceUnresolvedChainReferenceParameterAsync();

        result.Succeeded.ShouldBeFalse();
        var failed = result.Failure!.Diagnostics!.Parameters.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Failed>();
        failed.Stage.ShouldBe(TraceStage.Resolve);
        failed.Message.ShouldContain("organization");
    }

    [Fact]
    public async Task GivenAnUnresolvedIncludeOwnedByNoParameterTrace_WhenTraced_ThenTheTraceStillStatesWhyThePlanIsMissing()
    {
        var result = await CompilationFixtures.TraceUnresolvedIncludeAsync();

        result.Plan.ShouldBeNull();
        result.Failure!.Diagnostics!.Parameters.ShouldAllBe(p => p.Outcome is ParameterOutcome.Compiled);

        result.Failure.ShouldNotBeNull("an absent plan with every parameter Compiled is an unexplained result");
        result.Failure!.Stage.ShouldBe(CompilationStage.Resolve);
        result.Failure.Message.ShouldContain("organization");
    }

    [Fact]
    public async Task GivenAFailureNamingNoParameter_WhenTraced_ThenItsMessageSurvivesOnTheTrace()
    {
        var result = await CompilationFixtures.TraceSortKeyCapExceededAsync();

        result.Plan.ShouldBeNull();
        result.Failure!.Diagnostics!.Parameters.ShouldAllBe(p => p.Outcome is ParameterOutcome.Compiled);

        result.Failure.ShouldNotBeNull("the sort-key cap message would otherwise be lost entirely");
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
        result.Failure.Message.ShouldContain("_sort supports at most 3 keys");
        result.Failure.Span.ShouldBeNull();
    }

    [Fact]
    public async Task GivenALeafCte_WhenTraced_ThenItContributesOnlyItsOwnParameter()
    {
        var result = await CompilationFixtures.TracePatientActiveTrueAsync();

        result.Plan!.Diagnostics!.PlanTrace.ShouldNotBeNull();
        var leaf = result.Plan!.Diagnostics!.PlanTrace!.Ctes.First(c => c.ParameterOrdinal == 0);

        leaf.ContributingOrdinals.ShouldBe([0]);
    }

    [Fact]
    public async Task GivenAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnce()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var result = await CompilationFixtures.TracePatientNameSmithWithTimeProviderAsync(provider);

        // Assert
        result.Failure.ShouldBeNull();
        result.Plan.ShouldNotBeNull();
        provider.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenADateApComparatorQueryWithAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnceAndProducesTheWidenedSqlGolden()
    {
        // Arrange -- unlike GivenAFixedTimeProvider_WhenCompiled_ThenGetUtcNowIsCalledExactlyOnce above
        // (a plain name=Smith query whose value never reads the reference time at all), this query's
        // :ap comparator only compiles successfully if SearchSqlCompiler's single GetUtcNow() call supplied
        // a non-null reference instant all the way to Lower's approximationReferenceTime -- Approximate
        // DateRange.Widen throws InvalidOperationException otherwise, so an absent Failure here is itself
        // proof the captured value reached the widening logic, not merely that it was read once.
        var fixedTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var result = await CompilationFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert -- the captured reference instant reached Lower and widened the :ap range successfully
        provider.CallCount.ShouldBe(1);
        result.Failure.ShouldBeNull();
        result.Plan.ShouldNotBeNull();

        // Assert -- complete SQL golden: the same shape already pinned in EndToEndCompilationTests'
        // Lower.Run-based date :ap case, proving the SearchSqlCompiler orchestration boundary (Build ->
        // Resolve -> Lower -> Emit, with the reference time captured once up front) reaches the
        // identical emitted SQL as calling Lower.Run directly with that same widened reference.
        var compiled = result.Plan!.Compile();
        compiled.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.DateTimeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 203 AND (StartDateTime <= @p0 AND EndDateTime >= @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenTheSameDateApComparatorQueryCompiledTwiceWithTheSameFixedTimeProvider_WhenCompiled_ThenTheEmittedSqlIsByteIdentical()
    {
        // Arrange -- two independent TryCreatePlanAsync calls, each capturing its own
        // GetUtcNow() from the same FixedTimeProvider instance (proven by CallCount reaching 2, one per
        // compile, never more), must reach byte-identical widened SQL -- proving the compiler boundary's
        // determinism holds across separate compiles sharing one fixed clock, not just within a single one.
        var fixedTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var provider = new CountingFixedTimeProvider(fixedTime);

        // Act
        var result1 = await CompilationFixtures.TraceObservationDateApWithTimeProviderAsync(provider);
        var result2 = await CompilationFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert
        provider.CallCount.ShouldBe(2);
        result1.Failure.ShouldBeNull();
        result2.Failure.ShouldBeNull();
        var compiled1 = result1.Plan!.Compile();
        var compiled2 = result2.Plan!.Compile();
        compiled2.Sql.ShouldBe(compiled1.Sql);
    }

    [Fact]
    public async Task GivenAPositionalDefaultCancellationToken_WhenCompiled_ThenItCompilesAndDelegatesSuccessfully()
    {
        // Arrange — exercises SearchSqlCompiler.TryCreatePlanAsync with a positional `default`
        // CancellationToken as the final argument, proving the facade signature binds unambiguously.
        var result = await CompilationFixtures.TracePatientNameSmithWithCancellationTokenAsync(default);

        // Assert
        result.Failure.ShouldBeNull();
        result.Plan.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAPreCancelledToken_WhenCompiled_ThenTheCancellationReachesTheResolver()
    {
        // Arrange — a pre-cancelled token must propagate through TryCreatePlanAsync into Resolve and
        // reach the resolver, proving the delegation does not substitute CancellationToken.None.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            CompilationFixtures.TracePatientNameSmithWithCancellationCheckAsync(cts.Token));
    }

    [Fact]
    public async Task GivenAParameterThatLoweredToAnUnsatisfiablePredicate_WhenTraced_ThenItIsReportedAsAKnownMiss()
    {
        // Arrange & Act — a token system no resource uses compiles cleanly but can never match, which is
        // a property of the data, not a failure of the query; reporting it as Compiled would hide it.
        var result = await CompilationFixtures.TraceUnresolvableTokenSystemAsync();

        // Assert
        var parameter = result.Plan!.Diagnostics!.Parameters.ShouldHaveSingleItem();
        var knownMiss = parameter.Outcome.ShouldBeOfType<ParameterOutcome.KnownMiss>();
        knownMiss.Reason.ShouldBe("No resource uses the token system 'http://unknown.org/mrn'.");
        knownMiss.Span.ShouldBe(new SourceSpan(SourceOrigin.Value, 0, 31));
        result.Failure.ShouldBeNull();
        result.Plan.ShouldNotBeNull();
        result.Plan!.Compile().Sql.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenOneSatisfiableAndOneUnsatisfiableParameter_WhenTraced_ThenOnlyTheUnsatisfiableOneIsAKnownMiss()
    {
        // Arrange & Act
        var result = await CompilationFixtures.TraceSatisfiableAndUnresolvableTokenSystemAsync();

        // Assert — the miss is attributed to its own parameter, and the other stays Compiled
        var parameters = result.Plan!.Diagnostics!.Parameters;
        parameters.Count.ShouldBe(2);
        parameters[0].Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        var knownMiss = parameters[1].Outcome.ShouldBeOfType<ParameterOutcome.KnownMiss>();
        knownMiss.Reason.ShouldBe("No resource uses the token system 'http://unknown.org/mrn'.");
        knownMiss.Span.ShouldBe(new SourceSpan(SourceOrigin.Value, 10, 28));
    }

    [Fact]
    public async Task GivenAllParametersSatisfiable_WhenTraced_ThenNoneAreReportedAsAKnownMiss()
    {
        // Arrange & Act
        var result = await CompilationFixtures.TracePatientActiveTrueAsync();

        // Assert
        result.Plan!.Diagnostics!.Parameters.ShouldAllBe(p => !(p.Outcome is ParameterOutcome.KnownMiss));
    }

    [Fact]
    public async Task GivenOptionsCarryingAnAccessConstraint_WhenCompiledThroughTheFacade_ThenTheConstraintReachesLowerAndNarrowsTheMatch()
    {
        // Arrange & Act -- Observation?status=final with an AccessConstraint("Observation", status eq
        // amended) set on the options. This exercises the wiring seam: SearchOptions.AccessConstraints must
        // be forwarded from SearchSqlCompiler into Lower.Run. The constraint is applied structurally by
        // intersecting the match set, so the emitted plan gains an Intersect node it never has for a bare
        // single-leaf search. Dropping the forwarding argument in SearchSqlCompiler leaves the match a plain
        // ParamSource and this assertion fails -- proving the test covers the wiring, not just Lower.
        var result = await CompilationFixtures.TraceObservationStatusWithAccessConstraintAsync();

        // Assert
        result.Failure.ShouldBeNull("the constrained search should compile end to end");
        result.Plan.ShouldNotBeNull();
        result.Plan!.Diagnostics!.PlanTrace!.Explain.ShouldContain("Intersect");
        var compiled = result.Plan!.Compile();
        compiled.Sql.ShouldContain("SearchParamId = 220");
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
        var result = await CompilationFixtures.TraceObservationDateApWithTimeProviderAsync(provider);

        // Assert
        provider.CallCount.ShouldBe(1);
        result.Failure.ShouldBeNull();
        var compiled = result.Plan!.Compile();
        compiled.Sql.ShouldBe(
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
