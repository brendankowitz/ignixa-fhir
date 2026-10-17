/* Copyright (c) 2025, Ignixa Contributors */

using Shouldly;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

/// <summary>
/// <see cref="MappingEvaluatorOptions.TransformTimeout"/> bounds a whole transformation, and a caller's cancellation stops one; neither is
/// collected as an error in lenient mode.
/// </summary>
public class TransformTimeoutTests
{
    // Far longer than any timeout here: a run that is still going after this never stopped.
    private static readonly TimeSpan NeverStopped = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(ErrorMode.Strict)]
    [InlineData(ErrorMode.Lenient)]
    public async Task GivenAMapWhoseGroupsCallTheNextTwice_WhenTheTransformTimeoutPasses_ThenItStopsWithATransformTimeout(ErrorMode mode)
    {
        var evaluator = new MappingEvaluator(new MappingEvaluatorOptions { TransformTimeout = TimeSpan.FromMilliseconds(200), ErrorMode = mode });
        var context = new MappingContext { ErrorMode = mode };

        Task run = Task.Run(() => evaluator.Execute(FanOut(depth: 40), context));

        (await Task.WhenAny(run, Task.Delay(NeverStopped))).ShouldBe(run, "the transformation did not stop at its timeout");
        MappingExecutionException timedOut = await Should.ThrowAsync<MappingExecutionException>(run);
        timedOut.Code.ShouldBe("TRANSFORM_TIMEOUT");
        context.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ErrorMode.Strict)]
    [InlineData(ErrorMode.Lenient)]
    public async Task GivenACancelledToken_WhenExecuted_ThenItIsCancelledAndNotCollectedAsAnError(ErrorMode mode)
    {
        var options = new MappingEvaluatorOptions { ErrorMode = mode };
        var context = new MappingContext { ErrorMode = mode };
        var groupContext = new MappingContext { ErrorMode = mode };
        var cancelled = new CancellationToken(canceled: true);

        // An evaluator holds the state of one call, so each call has its own.
        Task execute = Task.Run(() => new MappingEvaluator(options).Execute(FanOut(depth: 40), context, cancelled));
        Task executeGroup = Task.Run(() => new MappingEvaluator(options).ExecuteGroup(FanOut(depth: 40), "G1", groupContext, cancelled));

        Task both = Task.WhenAll(execute, executeGroup);
        (await Task.WhenAny(both, Task.Delay(NeverStopped))).ShouldBe(both, "the transformation did not stop at its cancellation");
        await Should.ThrowAsync<OperationCanceledException>(execute);
        await Should.ThrowAsync<OperationCanceledException>(executeGroup);
        context.Errors.ShouldBeEmpty();
        groupContext.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAMapThatFinishesInTime_WhenExecutedWithAToken_ThenItRunsToTheEnd()
    {
        var evaluator = new MappingEvaluator(new MappingEvaluatorOptions());
        var context = new MappingContext();

        evaluator.ExecuteGroup(FanOut(depth: 4), "G1", context, CancellationToken.None);

        context.Errors.ShouldBeEmpty();
    }

    // G1 to G<depth>, each calling the next twice: 2^depth calls, none of them recursive and none creating an element.
    private static MapExpression FanOut(int depth)
    {
        List<GroupExpression> groups = [];
        for (int i = 1; i <= depth; i++)
        {
            List<RuleExpression> rules = i == depth
                ? [new RuleExpression(name: "leaf", sources: [], targets: [], dependent: null)]
                : [Call($"G{i + 1}"), Call($"G{i + 1}")];
            groups.Add(new GroupExpression(name: $"G{i}", parameters: [], extends: null, rules: rules));
        }

        return new MapExpression(url: "http://example.org/fan", identifier: "Fan", uses: [], imports: [], groups: groups);
    }

    private static RuleExpression Call(string group) =>
        new(name: "call", sources: [], targets: [], dependent: new GroupInvocationExpression(groupName: group, arguments: []));
}
