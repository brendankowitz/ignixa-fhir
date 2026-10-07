using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexProgressReporterTests
{
    public static TheoryData<string, string> ClosedJobWriters =>
        new()
        {
            { "Completing", "BarrierDelay" },
            { "Completing", "Barrier" },
            { "Completing", "Drain" },
            { "Completing", "Plan" },
            { "Completing", "Range" },
            { "Completing", "Completing" },
            { "Completed", "BarrierDelay" },
            { "Completed", "Barrier" },
            { "Completed", "Drain" },
            { "Completed", "Plan" },
            { "Completed", "Range" },
            { "Completed", "Completing" },
            { "Failed", "BarrierDelay" },
            { "Failed", "Barrier" },
            { "Failed", "Drain" },
            { "Failed", "Plan" },
            { "Failed", "Range" },
            { "Failed", "Completing" },
            { "Cancelled", "BarrierDelay" },
            { "Cancelled", "Barrier" },
            { "Cancelled", "Drain" },
            { "Cancelled", "Plan" },
            { "Cancelled", "Range" },
            { "Cancelled", "Completing" }
        };

    [Theory]
    [MemberData(nameof(ClosedJobWriters))]
    public async Task GivenClosedJob_WhenLateProgressIsReported_ThenJobAndHeartbeatAreUnchanged(
        string status,
        string writer)
    {
        var (reporter, job) = CreateReporter();
        job.Status = status;
        job.HeartbeatDate = DateTimeOffset.UtcNow.AddMinutes(-1);
        job.Progress = new JsonObject
        {
            ["phase"] = "Completing",
            ["terminalDecision"] = "Cancelled",
            ["terminalOutcomes"] = new JsonArray()
        };
        var before = JsonSerializer.Serialize(job);

        await (writer switch
        {
            "BarrierDelay" => reporter.ReportBarrierDelayAsync("job", [1], [], CancellationToken.None),
            "Barrier" => reporter.ReportBarrierAsync("job", new RaiseBarrierOutput(1, 10, 20), CancellationToken.None),
            "Drain" => reporter.ReportDrainAsync("job", new AwaitDrainOutput(1, true, 10), CancellationToken.None),
            "Plan" => reporter.ReportPlanAsync(
                new PlanReindexInput("job", 1, "Patient", -1, 100, 10, 2),
                new PlanReindexOutput([new ReindexRange(1, 10, 10)], null),
                CancellationToken.None),
            "Range" => reporter.ReportRangeAsync(
                new ReindexRangeInput("job", 1, "Patient", 1, 10, 42, 10, 0),
                new ReindexRangeOutput(10, 10, 0, []),
                CancellationToken.None),
            "Completing" => reporter.ReportCompletingAsync("job", CancellationToken.None),
            _ => throw new InvalidOperationException($"Unknown writer: {writer}")
        });

        JsonSerializer.Serialize(job).ShouldBe(before);
    }

    [Fact]
    public async Task GivenJobStart_WhenBarrierDelayIsReported_ThenEveryTenantHasPhaseProgress()
    {
        var (reporter, job) = CreateReporter();
        job.Progress = new JsonObject
        {
            ["notCovered"] = new JsonArray("http://example.org/not-covered")
        };

        await reporter.ReportBarrierDelayAsync(
            "job",
            [1, 2],
            ["http://example.org/ignored"],
            CancellationToken.None);

        job.Progress!["phase"]!.GetValue<string>().ShouldBe("BarrierDelay");
        job.Progress["ignoredLifecycleEvents"]!.AsArray()
            .Single()!.GetValue<string>().ShouldBe("http://example.org/ignored");
        var tenants = job.Progress["tenants"]!.AsArray();
        tenants.Count.ShouldBe(2);
        tenants.Select(tenant => tenant!["tenantId"]!.GetValue<int>()).ShouldBe([1, 2]);
        tenants.ShouldAllBe(tenant =>
            tenant!["status"]!.GetValue<string>() == "BarrierDelay");
        job.Progress["notCovered"]!.AsArray()
            .Single()!.GetValue<string>().ShouldBe("http://example.org/not-covered");
    }

    [Fact]
    public async Task GivenExactPlansAndCompletedRanges_WhenProgressIsReported_ThenTotalIsExactAndRunningProgressIsCapped()
    {
        var (reporter, job) = CreateReporter();
        await reporter.ReportBarrierDelayAsync("job", [1], [], CancellationToken.None);

        await reporter.ReportPlanAsync(
            new PlanReindexInput("job", 1, "Patient", -1, 100, 10, 2),
            new PlanReindexOutput(
                [new ReindexRange(1, 10, 10), new ReindexRange(11, 15, 5)],
                null),
            CancellationToken.None);
        await reporter.ReportRangeAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 15, 42, 15, 0),
            new ReindexRangeOutput(15, 15, 0, []),
            CancellationToken.None);

        job.Progress!["totalResourcesToReindex"]!.GetValue<long>().ShouldBe(15);
        job.Progress["resourcesSuccessfullyReindexed"]!.GetValue<long>().ShouldBe(15);
        job.Progress["progress"]!.GetValue<double>().ShouldBe(99.9);
    }

    private static (ReindexProgressReporter Reporter, BackgroundJob<ReindexJobDefinition> Job) CreateReporter()
    {
        var job = new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexJobDefinition.CreateForTest(),
            Progress = new JsonObject()
        };
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(job);
        var updater = new ReindexJobUpdater(
            repository,
            new ImmediateJobLock(),
            Substitute.For<IReindexCompletionHook>());
        return (new ReindexProgressReporter(updater), job);
    }

    private sealed class ImmediateJobLock : IReindexJobLock
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
