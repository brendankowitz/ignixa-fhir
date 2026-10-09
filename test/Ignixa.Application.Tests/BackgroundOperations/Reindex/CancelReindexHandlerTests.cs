using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class CancelReindexHandlerTests
{
    [Fact]
    public async Task GivenTerminalDecisionIsPersisted_WhenCancellationIsRequested_ThenOrchestrationIsNotTerminated()
    {
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                OrchestrationInstanceId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Completing",
                Definition = ReindexTestHelper.CreateJobDefinition(),
                Progress = new JsonObject { ["terminalDecision"] = "Completed" }
            });
        var jobLock = Substitute.For<IReindexJobLock>();
        var updater = new ReindexJobUpdater(
            repository,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var handler = new CancelReindexHandler(
            new TaskHubClient(runtime),
            repository,
            new ReindexLifecycleEventWriter(
                Substitute.For<ISourceEventStore>(),
                new ConformanceState()),
            updater);

        var result = await handler.HandleAsync(
            new CancelReindexCommand("job", "operator request"),
            CancellationToken.None);

        result.ShouldBeOfType<ReindexJobAlreadyTerminalResult>().Status.ShouldBe("Completed");
        await runtime.DidNotReceive().ForceTerminateTaskOrchestrationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
    }
}
