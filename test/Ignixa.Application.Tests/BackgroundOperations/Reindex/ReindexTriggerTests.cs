using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class ReindexTriggerTests
{
    [Fact]
    public async Task GivenAutoStartIsFalse_WhenActivationTriggers_ThenRequestIsNotSent()
    {
        var mediator = Substitute.For<IMediator>();
        var trigger = new ReindexTrigger(
            mediator,
            Options.Create(new ReindexOptions { AutoStart = false }),
            NullLogger<ReindexTrigger>.Instance);

        var result = await trigger.RequestReindexAsync(
            "activation",
            CancellationToken.None);

        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("disabled");
        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenProviderIsUnavailable_WhenActivationTriggers_ThenPendingStateIsExplicit()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReindexProviderUnavailableResult(3));
        var trigger = new ReindexTrigger(
            mediator,
            Options.Create(new ReindexOptions()),
            NullLogger<ReindexTrigger>.Instance);

        var result = await trigger.RequestReindexAsync(
            "activation",
            CancellationToken.None);

        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("tenant 3");
        result.Message.ShouldContain("Pending");
    }

    [Fact]
    public async Task GivenOperationalFailure_WhenActivationTriggers_ThenUnavailableExceptionIsThrown()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw new IOException("Database unavailable."));
        var trigger = new ReindexTrigger(
            mediator,
            Options.Create(new ReindexOptions()),
            NullLogger<ReindexTrigger>.Instance);

        var exception = await Should.ThrowAsync<ReindexTriggerUnavailableException>(() =>
            trigger.RequestReindexAsync("activation", CancellationToken.None));

        exception.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task GivenUnsupportedResult_WhenActivationTriggers_ThenItFailsFast()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new UnsupportedReindexResult());
        var trigger = new ReindexTrigger(
            mediator,
            Options.Create(new ReindexOptions()),
            NullLogger<ReindexTrigger>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            trigger.RequestReindexAsync("activation", CancellationToken.None));

        exception.Message.ShouldContain(nameof(UnsupportedReindexResult));
    }

    private sealed record UnsupportedReindexResult : CreateReindexJobResult;
}
