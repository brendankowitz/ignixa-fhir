using System.Text.Json.Nodes;
using Ignixa.Api.Services;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Api.Tests.Services;

public sealed class ReindexCompletionHookTests
{
    [Fact]
    public async Task GivenLocalRefreshFails_WhenCompletionRuns_ThenTerminalDecisionDoesNotDependOnRefresh()
    {
        using var state = new ConformanceState();
        var refresher = Substitute.For<IConformanceCacheRefresher>();
        refresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IConformanceConsumerSnapshot>(
                new InvalidOperationException("local refresh failed")));
        using var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);
        var hook = new ReindexCompletionHook(
            new ReindexAutomationStateStore(
                Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>()),
            Substitute.For<IMediator>(),
            Options.Create(new ReindexOptions { AutoStart = false }),
            NullLogger<ReindexCompletionHook>.Instance);

        await hook.OnCompletedAsync(
            new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Completed",
                Definition = ReindexTestHelper.CreateJobDefinition(),
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData("Failed", 1L, 1L, true)]
    [InlineData("Completed", 2L, 1L, false)]
    public async Task GivenFollowUpIsNotEligible_WhenCompletionRuns_ThenNoFollowUpIsStarted(
        string status,
        long requestedGeneration,
        long consumedGeneration,
        bool autoStart)
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(new SourceEvent(
            1,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/Patient-custom",
                "custom",
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom@1.0.0",
                null,
                2,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow));
        var refresher = Substitute.For<IConformanceCacheRefresher>();
        refresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IConformanceConsumerSnapshot>(
                new TestSnapshot(call.ArgAt<long>(1))));
        using var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync(
                ReindexAutomationStateStore.StateJobId,
                1,
                Arg.Any<CancellationToken>())
            .Returns(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = ReindexAutomationStateStore.StateJobId,
                JobType = (int)BackgroundJobType.ReindexAutomation,
                Status = "Active",
                Definition = ReindexTestHelper.CreateJobDefinition(),
                Progress = new JsonObject { ["requestedGeneration"] = requestedGeneration },
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            });
        var mediator = Substitute.For<IMediator>();
        var hook = new ReindexCompletionHook(
            new ReindexAutomationStateStore(repository),
            mediator,
            Options.Create(new ReindexOptions { AutoStart = autoStart }),
            NullLogger<ReindexCompletionHook>.Instance);
        var definition = ReindexTestHelper.CreateJobDefinition();
        definition = new ReindexJobDefinition
        {
            TargetEventId = definition.TargetEventId,
            TenantIds = definition.TenantIds,
            ResourceTypes = definition.ResourceTypes,
            SearchParameters = definition.SearchParameters,
            MaximumNumberOfResourcesPerQuery = definition.MaximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = definition.MaximumNumberOfResourcesPerWrite,
            MaximumConcurrency = definition.MaximumConcurrency,
            QueryDelayIntervalInMilliseconds = definition.QueryDelayIntervalInMilliseconds,
            Trigger = definition.Trigger,
            ConsumedGeneration = consumedGeneration
        };

        await hook.OnCompletedAsync(
            new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "failed-job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = status,
                Definition = definition,
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    private sealed record TestSnapshot(long Generation) : IConformanceConsumerSnapshot;
}
