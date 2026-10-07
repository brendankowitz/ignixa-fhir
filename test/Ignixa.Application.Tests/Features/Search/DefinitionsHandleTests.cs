using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Search;

public class DefinitionsHandleTests
{
    [Fact]
    public async Task GivenConcurrentHandlePublication_WhenReadersAcquireHandles_ThenIndexerAndEventIdStayAtomic()
    {
        var firstIndexer = Substitute.For<ISearchIndexer>();
        var secondIndexer = Substitute.For<ISearchIndexer>();
        var slot = new DefinitionsHandleSlot(new DefinitionsHandle(firstIndexer, 11));
        var mismatches = 0;

        var publisher = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 10_000; iteration++)
            {
                slot.Publish(new DefinitionsHandle(firstIndexer, 11));
                slot.Publish(new DefinitionsHandle(secondIndexer, 29));
            }
        });
        var reader = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 10_000; iteration++)
            {
                var handle = slot.Current;
                if ((ReferenceEquals(handle.Indexer, firstIndexer) && handle.DefinitionsEventId != 11) ||
                    (ReferenceEquals(handle.Indexer, secondIndexer) && handle.DefinitionsEventId != 29))
                {
                    Interlocked.Increment(ref mismatches);
                }
            }
        });

        await Task.WhenAll(publisher, reader);

        mismatches.ShouldBe(0);
    }

    [Fact]
    public void GivenASuccessfulDefinitionsRefresh_WhenHandleIsPublished_ThenWritersAcquireTheRefreshedPosition()
    {
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);

        context.PublishDefinitionsHandle(FhirVersion.R4, tenantId: null, definitionsEventId: 47);

        var handle = context.GetDefinitionsHandle(FhirVersion.R4, tenantId: null);
        handle.DefinitionsEventId.ShouldBe(47);
        handle.Indexer.ShouldBeSameAs(context.GetDefinitionsHandle(FhirVersion.R4, tenantId: null).Indexer);
    }

    [Fact]
    public void GivenAnOlderHandleCompletesAfterANewerHandle_WhenPublished_ThenItCannotReplaceTheNewerHandle()
    {
        var olderIndexer = Substitute.For<ISearchIndexer>();
        var newerIndexer = Substitute.For<ISearchIndexer>();
        var slot = new DefinitionsHandleSlot(new DefinitionsHandle(olderIndexer, 11));

        slot.Publish(new DefinitionsHandle(newerIndexer, 29));
        slot.Publish(new DefinitionsHandle(olderIndexer, 17));

        slot.Current.DefinitionsEventId.ShouldBe(29);
        slot.Current.Indexer.ShouldBeSameAs(newerIndexer);
    }

    [Fact]
    public void GivenAnOlderConsumerSnapshotCompletesAfterANewerSnapshot_WhenPublished_ThenAllConsumersRemainOnTheNewerGeneration()
    {
        var olderDefinitions = Substitute.For<ISearchParameterDefinitionManager>();
        var newerDefinitions = Substitute.For<ISearchParameterDefinitionManager>();
        var older = new ConformanceDefinitionsSnapshot(
            olderDefinitions,
            olderDefinitions,
            new DefinitionsHandle(Substitute.For<ISearchIndexer>(), 11));
        var newer = new ConformanceDefinitionsSnapshot(
            newerDefinitions,
            newerDefinitions,
            new DefinitionsHandle(Substitute.For<ISearchIndexer>(), 29));
        var slot = new ConformanceDefinitionsSnapshotSlot(older);

        slot.Publish(newer);
        slot.Publish(new ConformanceDefinitionsSnapshot(
            olderDefinitions,
            olderDefinitions,
            new DefinitionsHandle(Substitute.For<ISearchIndexer>(), 17)));

        slot.Current.Generation.ShouldBe(29);
        slot.Current.ExtractionDefinitions.ShouldBeSameAs(newerDefinitions);
        slot.Current.SearchableDefinitions.ShouldBeSameAs(newerDefinitions);
        slot.ExtractionDefinitions.ShouldNotBeSameAs(olderDefinitions);
        slot.SearchableDefinitions.ShouldNotBeSameAs(olderDefinitions);
    }
}
