using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
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
        var reconciler = new ReindexStartupReconciler(
            mediator,
            Options.Create(new ReindexOptions { AutoStart = false }),
            NullLogger<ReindexStartupReconciler>.Instance);

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
        var reconciler = new ReindexStartupReconciler(
            mediator,
            Options.Create(new ReindexOptions()),
            NullLogger<ReindexStartupReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command =>
                command.Trigger == "Reconciliation" &&
                !command.QueueRequest),
            CancellationToken.None);
    }
}
