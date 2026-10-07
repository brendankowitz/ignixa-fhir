using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
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
        var coordinator = await CreateCoordinatorAsync(
            repository,
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 29));
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
    public async Task GivenANonAtomicEntryRejectedTwice_WhenDefinitionsRefresh_ThenThe503EscapesTheBatch()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<UpdateResult>(
                new StaleConformanceDefinitionsException(101, 11, 29)));
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var coordinator = await CreateCoordinatorAsync(
            repository,
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 29));

        var queued = coordinator.QueueWriteAsync(Patient("stale") with { DefinitionsEventId = 11 });
        await coordinator.WaitToReadAsync();

        await Should.ThrowAsync<ConformanceDefinitionsUnavailableException>(() =>
            coordinator.ProcessBatchAsync(batchSize: 1, CancellationToken.None));
        await Should.ThrowAsync<ConformanceDefinitionsUnavailableException>(() => queued);
    }

    [Fact]
    public async Task GivenMultipleQueuedEntries_WhenTheFirstIsRejectedTwice_ThenEveryEntryCompletesWithThe503()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<UpdateResult>(
                new StaleConformanceDefinitionsException(101, 11, 29)));
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var coordinator = await CreateCoordinatorAsync(
            repository,
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 29),
            channelCapacity: 2);

        var first = coordinator.QueueWriteAsync(Patient("first") with { DefinitionsEventId = 11 });
        var second = coordinator.QueueWriteAsync(Patient("second") with { DefinitionsEventId = 11 });
        await coordinator.WaitToReadAsync();

        await Should.ThrowAsync<ConformanceDefinitionsUnavailableException>(() =>
            coordinator.ProcessBatchAsync(batchSize: 2, CancellationToken.None));
        await Should.ThrowAsync<ConformanceDefinitionsUnavailableException>(() => first);
        await Should.ThrowAsync<ConformanceDefinitionsUnavailableException>(() => second);
    }

    private static async Task<DeferredWriteCoordinator> CreateCoordinatorAsync(
        IFhirRepository repository,
        DefinitionsHandle definitionsHandle,
        int channelCapacity = 1)
    {
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        var partitions = Substitute.For<IPartitionStrategy>();
        partitions.DetermineWritePartition(
                Arg.Any<PartitionResolutionContext>(),
                Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.FhirVersion.Returns(FhirVersion.R4);
        context.TenantConfiguration.Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Barrier test", FhirVersion = "4.0" });
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(new R4CoreSchemaProvider());
        versions.GetDefinitionsHandle(FhirVersion.R4, 1).Returns(definitionsHandle);
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var retryPolicy = new ConformanceBarrierRetryPolicy(
            synchronizer,
            TimeSpan.FromSeconds(17),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);

        return await DeferredWriteCoordinator.CreateAsync(
            channelCapacity,
            repositories,
            partitions,
            accessor,
            versions,
            retryPolicy,
            NullLogger<DeferredWriteCoordinator>.Instance);
    }

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
