using Ignixa.Abstractions;
using Ignixa.Application.Tests.Features;
using Ignixa.Search.Indexing;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.ImportActivityRunner;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkImportIndexingFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenIndexExtractionFailure_WhenImportRuns_ThenResourceIsReportedAsAnError(bool streaming)
    {
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(_ => throw new InvalidOperationException("Index extraction failed"));

        var result = await RunAsync(streaming, TestWritePath.Versions(0, indexer), WritingRepository());

        result.SuccessCount.ShouldBe(0);
        result.ErrorCount.ShouldBe(1);
        result.Errors.Single().ErrorMessage.ShouldContain("Index extraction failed");
    }
}
