using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class DeferredWriteCoordinatorBarrierTests
{
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
                    ? ValueTask.FromException<UpdateResult>(new StaleConformanceDefinitionsException(101, 11, 29))
                    : new ValueTask<UpdateResult>(Result(wrapper));
            });
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var coordinator = await CreateCoordinatorAsync(repository, indexer);
        var wrapper = Patient("retry") with { DefinitionsEventId = 11 };

        var queued = coordinator.QueueWriteAsync(wrapper, entryIndex: 7);
        await coordinator.WaitToReadAsync();
        var errors = await coordinator.ProcessBatchAsync(batchSize: 1, CancellationToken.None);

        errors.ShouldBeEmpty();
        (await queued).Id.ShouldBe("retry");
        attempts.Select(attempt => attempt.DefinitionsEventId).ShouldBe([11, 29]);
        indexer.Received(1).Extract(Arg.Any<IElement>());
    }

    [Fact]
    public async Task GivenMultipleQueuedEntries_WhenTheFirstIsRejectedTwice_ThenEveryEntryCompletesWithThe503()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<UpdateResult>(new StaleConformanceDefinitionsException(101, 11, 29)));
        var coordinator = await CreateCoordinatorAsync(repository, channelCapacity: 2);

        var first = coordinator.QueueWriteAsync(Patient("first") with { DefinitionsEventId = 11 });
        var second = coordinator.QueueWriteAsync(Patient("second") with { DefinitionsEventId = 11 });
        await coordinator.WaitToReadAsync();

        await Should.ThrowAsync<ConformanceStaleException>(() =>
            coordinator.ProcessBatchAsync(batchSize: 2, CancellationToken.None));
        await Should.ThrowAsync<ConformanceStaleException>(() => first);
        await Should.ThrowAsync<ConformanceStaleException>(() => second);
    }

    private static Task<DeferredWriteCoordinator> CreateCoordinatorAsync(
        IFhirRepository repository,
        ISearchIndexer? indexer = null,
        int channelCapacity = 1) =>
        DeferredWriteCoordinator.CreateAsync(
            channelCapacity,
            TestWritePath.Repositories(repository),
            TestWritePath.IsolatedPartition(),
            TestWritePath.RequestContext(),
            TestWritePath.Versions(definitionsEventId: 29, indexer),
            TestConformanceBarrierRetryPolicy.Create(),
            NullLogger<DeferredWriteCoordinator>.Instance);

    private static ResourceWrapper Patient(string id) => new(
        "Patient",
        id,
        "1",
        DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));

    private static UpdateResult Result(ResourceWrapper wrapper) => new(
        new ResourceKey(wrapper.ResourceType, wrapper.ResourceId, "1", 1),
        ReadOnlyMemory<byte>.Empty,
        DateTimeOffset.UtcNow);
}
