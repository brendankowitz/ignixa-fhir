using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public class BulkDeleteOrchestrationTests
{
    private readonly OrchestrationContext _context = Substitute.For<OrchestrationContext>();
    private readonly List<BulkDeleteBatchInput> _batches = [];
    private readonly List<CompleteBulkDeleteJobInput> _completions = [];
    private readonly List<RetryOptions> _retries = [];
    private readonly Queue<Func<BulkDeleteBatchOutput>> _outputs = new();
    private readonly List<object> _continued = [];
    private readonly List<TimeSpan> _waits = [];
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public BulkDeleteOrchestrationTests()
    {
        _context.CurrentUtcDateTime.Returns(_ => _now);
        _context.CreateTimer(Arg.Any<DateTime>(), true).Returns(call =>
        {
            var fireAt = call.Arg<DateTime>();
            _waits.Add(fireAt - _now);
            _now = fireAt;
            return Task.FromResult(true);
        });
        _context.When(context => context.ContinueAsNew(Arg.Any<object>())).Do(call => _continued.Add(call.Arg<object>()));
        _context.ScheduleWithRetry<BulkDeleteBatchOutput>(typeof(BulkDeleteBatchActivity), Arg.Any<RetryOptions>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                _retries.Add(call.ArgAt<RetryOptions>(1));
                _batches.Add((BulkDeleteBatchInput)call.ArgAt<object[]>(2)[0]);
                return Task.FromResult(_outputs.Dequeue()());
            });
        _context.ScheduleWithRetry<bool>(typeof(CompleteBulkDeleteJobActivity), Arg.Any<RetryOptions>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                _completions.Add((CompleteBulkDeleteJobInput)call.ArgAt<object[]>(2)[0]);
                return Task.FromResult(true);
            });
    }

    [Fact]
    public async Task GivenSeveralTypesAndPages_WhenRunning_ThenBatchesRunInOrderAndTheTotalsComplete()
    {
        Enqueue(Batch(new() { ["Patient"] = 2, ["Observation"] = 1 }, hasMore: true, firstMatch: "Patient/p1"));
        Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: false, firstMatch: "Patient/p3"));
        Enqueue(Batch(new() { ["Group"] = 4 }, hasMore: false, firstMatch: "Group/g1"));

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient", "Group"));

        _batches.Select(batch => batch.ResourceType).ShouldBe(["Patient", "Patient", "Group"]);
        _batches.ShouldAllBe(batch => batch.ContinuationToken == null && batch.BatchSize == 2 && batch.Mode == BulkDeleteMode.HardDelete);
        _batches[0].CumulativeCounts.ShouldBeEmpty();
        _batches[1].CumulativeCounts.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2, ["Observation"] = 1 }, ignoreOrder: true);
        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeTrue();
        completion.ResourceDeletedCount.ShouldBe(
            new Dictionary<string, long> { ["Patient"] = 3, ["Observation"] = 1, ["Group"] = 4 }, ignoreOrder: true);
        output.ShouldBe(output with { Success = true, Superseded = false, ErrorMessage = null });
        _retries.ShouldAllBe(retry => retry.FirstRetryInterval == TimeSpan.FromSeconds(5) &&
            retry.MaxNumberOfAttempts == 3 && retry.BackoffCoefficient == 2);
    }

    [Fact]
    public async Task GivenABatchDeferredForAStaleConformanceLease_WhenRunning_ThenTheSameBatchRunsAgainAfterADurableWait()
    {
        Enqueue(Batch(new() { ["Patient"] = 2 }, hasMore: true, firstMatch: "Patient/p1"));
        Enqueue(BulkDeleteBatchOutput.WaitForConformance());
        Enqueue(BulkDeleteBatchOutput.WaitForConformance());
        Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: false, firstMatch: "Patient/p3"));

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient"));

        _batches.Count.ShouldBe(4);
        _batches.Skip(1).ShouldAllBe(batch =>
            batch.ResourceType == "Patient" && batch.ContinuationToken == null && batch.CumulativeCounts["Patient"] == 2);
        _waits.ShouldBe([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)]);
        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeTrue();
        completion.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 3 });
        output.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenTheConformanceLeaseStaysLost_WhenTheWaitExceedsItsBound_ThenTheJobFailsWithTheReason()
    {
        for (var index = 0; index < 100; index++)
        {
            Enqueue(BulkDeleteBatchOutput.WaitForConformance());
        }

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient"));

        _waits.ShouldAllBe(wait => wait <= TimeSpan.FromMinutes(1));
        _waits.Aggregate(TimeSpan.Zero, (total, wait) => total + wait)
            .ShouldBeGreaterThanOrEqualTo(BulkDeleteOrchestration.ConformanceWaitTimeout);
        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeFalse();
        completion.ErrorMessage.ShouldContain("conformance staleness lease");
        completion.ResourceDeletedCount.ShouldBeEmpty();
        output.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenARestartModePageThatMadeNoProgress_WhenRunning_ThenTheJobFailsInsteadOfLooping()
    {
        Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: true, firstMatch: "Patient/p1"));
        Enqueue(Batch([], hasMore: true, firstMatch: "Patient/p1"));

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.SoftDelete, "Patient"));

        _batches.Count.ShouldBe(2);
        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeFalse();
        completion.ErrorMessage.ShouldContain("no progress");
        completion.ErrorMessage.ShouldContain("'Patient'");
        completion.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 1 });
        output.Success.ShouldBeFalse();
        output.Superseded.ShouldBeFalse();
        output.ErrorMessage.ShouldBe(completion.ErrorMessage);
    }

    [Fact]
    public async Task GivenAPurge_WhenPagesAdvance_ThenTheCursorIsForwardedAndARepeatedFirstMatchIsNotAFailure()
    {
        Enqueue(Batch(new() { ["Patient"] = 2 }, hasMore: true, firstMatch: "Patient/p1", continuation: "c1"));
        Enqueue(Batch(new() { ["Patient"] = 2 }, hasMore: true, firstMatch: "Patient/p1", continuation: "c2"));
        Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: false, firstMatch: "Patient/p5"));

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.PurgeHistory, "Patient"));

        _batches.Select(batch => batch.ContinuationToken).ShouldBe([null, "c1", "c2"]);
        _completions.ShouldHaveSingleItem().Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenASupersededBatch_WhenRunning_ThenTheOrchestrationStopsWithoutFinalizingTheJob()
    {
        Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: true, firstMatch: "Patient/p1"));
        Enqueue(new BulkDeleteBatchOutput([], false, null, null, Superseded: true));

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient", "Group"));

        output.Superseded.ShouldBeTrue();
        output.Success.ShouldBeFalse();
        _batches.Count.ShouldBe(2);
        _completions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenABatchFailsAfterRetries_WhenRunning_ThenTheFailureAndPartialCountsAreRecorded()
    {
        Enqueue(Batch(new() { ["Patient"] = 2 }, hasMore: false, firstMatch: "Patient/p1"));
        _outputs.Enqueue(() => throw new TaskFailedException("search unavailable"));

        var output = await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient", "Group"));

        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeFalse();
        completion.ErrorMessage.ShouldContain("'Group'");
        completion.ErrorMessage.ShouldContain("search unavailable");
        completion.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2 });
        output.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenTheFailureCannotBeRecorded_WhenRunning_ThenTheOrchestrationFailsRatherThanHidingIt()
    {
        _outputs.Enqueue(() => throw new TaskFailedException("search unavailable"));
        _context.ScheduleWithRetry<bool>(typeof(CompleteBulkDeleteJobActivity), Arg.Any<RetryOptions>(), Arg.Any<object[]>())
            .Returns(Task.FromException<bool>(new TaskFailedException("job store unavailable")));

        var failure = await Should.ThrowAsync<TaskFailedException>(() =>
            new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient")));

        failure.Message.ShouldContain("job store unavailable");
    }

    [Fact]
    public async Task GivenMoreBatchesThanOneExecutionRuns_WhenTheLimitIsReached_ThenItContinuesAsNewWithItsPositionAndCounts()
    {
        for (var index = 0; index < BulkDeleteOrchestration.BatchesPerExecution; index++)
        {
            Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: true, firstMatch: $"Patient/p{index}", continuation: $"c{index}"));
        }

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.PurgeHistory, "Observation", "Patient") with
        {
            TypeIndex = 1,
            CarriedCounts = new() { ["Observation"] = 7 },
        });

        _batches.Count.ShouldBe(BulkDeleteOrchestration.BatchesPerExecution);
        _completions.ShouldBeEmpty();
        var next = _continued.ShouldHaveSingleItem().ShouldBeOfType<BulkDeleteOrchestrationInput>();
        next.TypeIndex.ShouldBe(1);
        next.ContinuationToken.ShouldBe($"c{BulkDeleteOrchestration.BatchesPerExecution - 1}");
        next.PreviousFirstMatchKey.ShouldBe($"Patient/p{BulkDeleteOrchestration.BatchesPerExecution - 1}");
        next.CarriedCounts.ShouldBe(
            new Dictionary<string, long> { ["Observation"] = 7, ["Patient"] = BulkDeleteOrchestration.BatchesPerExecution },
            ignoreOrder: true);
        next.JobId.ShouldBe("job");
        next.ResourceTypes.ShouldBe(["Observation", "Patient"]);
        next.BatchSize.ShouldBe(2);
    }

    [Fact]
    public async Task GivenACarriedPosition_WhenTheNextExecutionRuns_ThenItResumesAtThatTypeCursorAndTotals()
    {
        Enqueue(Batch(new() { ["Patient"] = 2 }, hasMore: false, firstMatch: "Patient/p9"));
        Enqueue(Batch(new() { ["Group"] = 1 }, hasMore: false, firstMatch: "Group/g1"));

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.PurgeHistory, "Observation", "Patient", "Group") with
        {
            TypeIndex = 1,
            ContinuationToken = "c99",
            PreviousFirstMatchKey = "Patient/p8",
            CarriedCounts = new() { ["Observation"] = 3, ["Patient"] = 100 },
        });

        _batches.Select(batch => (batch.ResourceType, batch.ContinuationToken)).ShouldBe([("Patient", "c99"), ("Group", null)]);
        _batches[0].CumulativeCounts.ShouldBe(new Dictionary<string, long> { ["Observation"] = 3, ["Patient"] = 100 }, ignoreOrder: true);
        _completions.ShouldHaveSingleItem().ResourceDeletedCount.ShouldBe(
            new Dictionary<string, long> { ["Observation"] = 3, ["Patient"] = 102, ["Group"] = 1 }, ignoreOrder: true);
        _continued.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenACarriedFirstMatch_WhenTheNextExecutionsFirstBatchMakesNoProgress_ThenTheJobFails()
    {
        Enqueue(Batch([], hasMore: true, firstMatch: "Patient/p1"));

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient") with
        {
            PreviousFirstMatchKey = "Patient/p1",
            CarriedCounts = new() { ["Patient"] = 100 },
        });

        var completion = _completions.ShouldHaveSingleItem();
        completion.Success.ShouldBeFalse();
        completion.ErrorMessage.ShouldContain("no progress");
        completion.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 100 });
    }

    [Fact]
    public async Task GivenAnExecutionLimitReachedExactlyAtATypeBoundary_WhenContinuing_ThenTheNextTypeStartsFresh()
    {
        for (var index = 0; index < BulkDeleteOrchestration.BatchesPerExecution; index++)
        {
            var last = index == BulkDeleteOrchestration.BatchesPerExecution - 1;
            Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: !last, firstMatch: $"Patient/p{index}"));
        }

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient", "Group"));

        var next = _continued.ShouldHaveSingleItem().ShouldBeOfType<BulkDeleteOrchestrationInput>();
        next.TypeIndex.ShouldBe(1);
        next.ContinuationToken.ShouldBeNull();
        next.PreviousFirstMatchKey.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTheLastTypeFinishesOnTheLimit_WhenRunning_ThenTheJobCompletesWithoutContinuing()
    {
        for (var index = 0; index < BulkDeleteOrchestration.BatchesPerExecution; index++)
        {
            var last = index == BulkDeleteOrchestration.BatchesPerExecution - 1;
            Enqueue(Batch(new() { ["Patient"] = 1 }, hasMore: !last, firstMatch: $"Patient/p{index}"));
        }

        await new BulkDeleteOrchestration().RunTask(_context, Input(BulkDeleteMode.HardDelete, "Patient"));

        _continued.ShouldBeEmpty();
        _completions.ShouldHaveSingleItem().Success.ShouldBeTrue();
    }

    [Fact]
    public void GivenAKickoffInputWithoutCarriedState_WhenDurableTaskDeserializesIt_ThenItStartsAtTheBeginning()
    {
        // The input a kickoff produced before the carried fields existed.
        const string json = """
            {"JobId":"job","TenantId":1,"ResourceTypes":["Patient"],"SearchQuery":"","Mode":1,
             "ExcludedResourceTypes":[],"RemoveReferences":false,"BatchSize":5}
            """;

        var input = JsonDataConverter.Default.Deserialize<BulkDeleteOrchestrationInput>(json);

        input.TypeIndex.ShouldBe(0);
        input.ContinuationToken.ShouldBeNull();
        input.PreviousFirstMatchKey.ShouldBeNull();
        input.CarriedCounts.ShouldBeNull();
        input.BatchSize.ShouldBe(5);
    }

    [Fact]
    public void GivenACarriedInput_WhenDurableTaskRoundTripsIt_ThenThePositionAndCountsSurvive()
    {
        var carried = Input(BulkDeleteMode.PurgeHistory, "Patient", "Group") with
        {
            TypeIndex = 1, ContinuationToken = "c1", PreviousFirstMatchKey = "Group/g1", CarriedCounts = new() { ["Patient"] = 9 },
        };

        var input = JsonDataConverter.Default.Deserialize<BulkDeleteOrchestrationInput>(JsonDataConverter.Default.Serialize(carried));

        input.TypeIndex.ShouldBe(1);
        input.ContinuationToken.ShouldBe("c1");
        input.PreviousFirstMatchKey.ShouldBe("Group/g1");
        input.CarriedCounts.ShouldBe(new Dictionary<string, long> { ["Patient"] = 9 });
        input.ResourceTypes.ShouldBe(["Patient", "Group"]);
    }

    private void Enqueue(BulkDeleteBatchOutput output) => _outputs.Enqueue(() => output);

    private static BulkDeleteBatchOutput Batch(
        Dictionary<string, long> deleted, bool hasMore, string firstMatch, string? continuation = null) =>
        new(deleted, hasMore, continuation, firstMatch, Superseded: false);

    private static BulkDeleteOrchestrationInput Input(BulkDeleteMode mode, params string[] types) =>
        new("job", 1, types, "name=smith", mode, ["Group"], RemoveReferences: false, BatchSize: 2);
}
