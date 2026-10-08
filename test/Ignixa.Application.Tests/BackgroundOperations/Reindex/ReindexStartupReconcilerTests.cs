using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class ReindexStartupReconcilerTests
{
    [Fact]
    public async Task GivenAutoStartIsFalse_WhenStartupReconciles_ThenNoJobIsRequested()
    {
        var mediator = Substitute.For<IMediator>();
        var reconciler = CreateReconciler(mediator, autoStart: false);

        await reconciler.ReconcileAsync(CancellationToken.None);

        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAutoStartIsTrue_WhenStartupReconciles_ThenReconciliationJobIsRequested()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new NoReindexWorkResult("No resources need reindexing."));
        var reconciler = CreateReconciler(mediator, autoStart: true);

        await reconciler.ReconcileAsync(CancellationToken.None);

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command =>
                command.Trigger == "Reconciliation" &&
                !command.QueueRequest),
            CancellationToken.None);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenLatestGenerationEndedUnsuccessfully_WhenReconciles_ThenNoJobIsRequested(
        string status)
    {
        var mediator = Substitute.For<IMediator>();
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
                Definition = ReindexJobDefinition.CreateForTest(),
                Progress = new System.Text.Json.Nodes.JsonObject { ["requestedGeneration"] = 1L },
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            });
        var definition = ReindexJobDefinition.CreateForTest();
        repository.ListAsync((int)BackgroundJobType.Reindex, Arg.Any<CancellationToken>())
            .Returns([
                new BackgroundJob<ReindexJobDefinition>
                {
                    JobId = "terminal",
                    JobType = (int)BackgroundJobType.Reindex,
                    Status = status,
                    Definition = new ReindexJobDefinition
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
                        ConsumedGeneration = 1
                    },
                    CreateDate = DateTimeOffset.UtcNow,
                    HeartbeatDate = DateTimeOffset.UtcNow,
                    EndDate = DateTimeOffset.UtcNow
                }
            ]);
        var reconciler = CreateReconciler(mediator, autoStart: true, repository);

        await reconciler.ReconcileAsync(CancellationToken.None);

        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    private static ReindexStartupReconciler CreateReconciler(
        IMediator mediator,
        bool autoStart,
        IBackgroundJobRepository<ReindexJobDefinition>? repository = null)
    {
        if (repository is null)
        {
            repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
            repository.ListAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        return new ReindexStartupReconciler(
            mediator,
            repository,
            new ReindexAutomationStateStore(repository),
            Options.Create(new ReindexOptions { AutoStart = autoStart }),
            NullLogger<ReindexStartupReconciler>.Instance);
    }
}
