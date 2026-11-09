using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Tests.Features;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Generated;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.ImportActivityRunner;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ImportDefinitionsEventIdTests
{
    // §5.4: an import batch allocates with the event id of the handle that extracted it.
    [Fact]
    public async Task GivenAnImportBatch_WhenAllocating_ThenItUsesTheCurrentDefinitionsEventId()
    {
        var repository = WritingRepository();

        var output = await RunAsync(TestWritePath.Versions(definitionsEventId: 73), repository);

        output.SuccessCount.ShouldBe(1);
        await repository.Received(1).GetNextTransactionIdAsync(73, Arg.Any<CancellationToken>());
    }

    // Spec §5.4: a batch the barrier rejects is re-extracted with a fresh handle and retried once.
    [Fact]
    public async Task GivenTheBarrierRejectsTheFirstAllocation_WhenImporting_ThenTheBatchIsReextractedWithFreshDefinitions()
    {
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var versions = TestWritePath.Versions(72, indexer);
        versions.GetDefinitionsHandle(FhirVersion.R4, 1).Returns(
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 72),
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 73));
        var repository = WritingRepository();
        repository.GetNextTransactionIdAsync(72, Arg.Any<CancellationToken>())
            .Returns<ValueTask<TransactionId>>(_ => throw new StaleConformanceDefinitionsException(101, 72, 73));

        var output = await RunAsync(versions, repository);

        output.SuccessCount.ShouldBe(1);
        await repository.Received(1).GetNextTransactionIdAsync(73, Arg.Any<CancellationToken>());
        indexer.Received(2).Extract(Arg.Any<IElement>());
    }
}
