using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.Features.TestWritePath;

namespace Ignixa.Application.Tests.Features.Bundle;

/// <summary>Spec §5.3–§5.4: bundle entries extracted at event 11 meet a barrier that has moved on to 29.</summary>
public class DeferredWriteCoordinatorBarrierTests
{
    private readonly ISearchIndexer _indexer = Indexer();

    [Fact]
    public async Task GivenANonAtomicEntryRejectedByTheBarrier_WhenDefinitionsRefresh_ThenItIsReextractedAndRetried()
    {
        var repository = Substitute.For<IFhirRepository>();
        var attempts = new List<ResourceWrapper>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var wrapper = call.Arg<ResourceWrapper>();
                attempts.Add(wrapper);
                return attempts.Count == 1
                    ? ValueTask.FromException<UpdateResult>(Stale())
                    : new(new UpdateResult(new ResourceKey("Patient", wrapper.ResourceId, "1", 1), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow));
            });
        var coordinator = await CreateCoordinatorAsync(repository);

        var queued = coordinator.QueueWriteAsync(Patient("retry", 11), entryIndex: 7);
        await coordinator.WaitToReadAsync();
        var errors = await coordinator.ProcessBatchAsync(batchSize: 1, CancellationToken.None);

        errors.ShouldBeEmpty();
        (await queued).Id.ShouldBe("retry");
        attempts.Select(attempt => attempt.DefinitionsEventId).ShouldBe([11, 29]);
        _indexer.Received(1).Extract(Arg.Any<IElement>());
    }

    [Fact]
    public async Task GivenMultipleQueuedEntries_WhenTheFirstIsRejectedTwice_ThenEveryEntryCompletesWithThe503()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<UpdateResult>(Stale()));
        var coordinator = await CreateCoordinatorAsync(repository, channelCapacity: 2);

        var first = coordinator.QueueWriteAsync(Patient("first", 11));
        var second = coordinator.QueueWriteAsync(Patient("second", 11));
        await coordinator.WaitToReadAsync();

        await Should.ThrowAsync<ConformanceStaleException>(() => coordinator.ProcessBatchAsync(batchSize: 2, CancellationToken.None));
        await Should.ThrowAsync<ConformanceStaleException>(() => first);
        await Should.ThrowAsync<ConformanceStaleException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    // Spec 5.4: a transaction bundle rejected by the barrier re-extracts every staged entry with the fresh handle.
    [Fact]
    public async Task GivenAnAtomicCommitRejectedByTheBarrier_WhenDefinitionsRefresh_ThenEveryStagedEntryIsReextractedAndRetried()
    {
        var repository = Substitute.For<IFhirRepository, IAtomicFhirRepository>();
        var attempts = new List<IReadOnlyList<ResourceWrapper>>();
        ((IAtomicFhirRepository)repository).WriteTransactionAsync(Arg.Any<IReadOnlyList<ResourceWrapper>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                attempts.Add(call.Arg<IReadOnlyList<ResourceWrapper>>());
                return attempts.Count == 1 ? Task.FromException(Stale()) : Task.CompletedTask;
            });
        var coordinator = await CreateCoordinatorAsync(repository, atomic: true);
        await coordinator.QueueWriteAsync(Patient("first", 11), entryIndex: 0);
        await coordinator.QueueWriteAsync(Patient("second", 11), entryIndex: 1);

        await coordinator.CommitAsync(CancellationToken.None);

        attempts.Select(attempt => attempt.Select(write => write.DefinitionsEventId).ToArray()).ShouldBe([[11L, 11L], [29L, 29L]]);
        _indexer.Received(2).Extract(Arg.Any<IElement>());
    }

    private static StaleConformanceDefinitionsException Stale() => new(101, 11, 29);

    private Task<DeferredWriteCoordinator> CreateCoordinatorAsync(IFhirRepository repository, int channelCapacity = 1, bool atomic = false) =>
        DeferredWriteCoordinator.CreateAsync(
            channelCapacity,
            Repositories(repository),
            IsolatedPartition(),
            RequestContext(),
            Versions(definitionsEventId: 29, _indexer),
            TestConformanceBarrierRetryPolicy.Create(),
            NullLogger<DeferredWriteCoordinator>.Instance,
            atomic);
}
