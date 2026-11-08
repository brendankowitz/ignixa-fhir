// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Microsoft.Extensions.Logging;
using static Ignixa.Search.Tests.InMemory.SearchCorpus;

namespace Ignixa.Search.Tests.InMemory;

public class LastUpdatedSearchIndexerTests
{
    [Theory]
    [InlineData("2020-01-01T23:30:00Z", "2020-01-01T23:30:00+00:00")]
    [InlineData("2020-01-02T01:30:00+02:00", "2020-01-01T23:30:00+00:00")]
    [InlineData("2020-01-01T23:30:00.123Z", "2020-01-01T23:30:00.123+00:00")]
    public void GivenAnInstant_WhenExtracted_ThenALastUpdatedEntryIsAddedAtThatInstant(string lastUpdated, string expectedStart)
    {
        var logger = new RecordingLogger();

        (_, IReadOnlyCollection<SearchIndexEntry> index) = Index(
            new LastUpdatedSearchIndexer(Indexer, Definitions, logger),
            $$$"""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"{{{lastUpdated}}}"},"gender":"female"}""");

        SearchIndexEntry entry = index.Where(e => e.SearchParameter.Code == "_lastUpdated").ShouldHaveSingleItem();
        ((DateTimeSearchValue)entry.Value).Start.ShouldBe(DateTimeOffset.Parse(expectedStart, System.Globalization.CultureInfo.InvariantCulture));
        index.ShouldContain(e => e.SearchParameter.Code == "gender");
        logger.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2020-01-01T23:30:00")]
    [InlineData("2020-01-01")]
    [InlineData("garbage")]
    public void GivenAValueThatIsNotAnInstant_WhenExtracted_ThenNoEntryIsAddedAndAWarningNamesTheResource(string lastUpdated)
    {
        var logger = new RecordingLogger();

        (_, IReadOnlyCollection<SearchIndexEntry> index) = Index(
            new LastUpdatedSearchIndexer(Indexer, Definitions, logger),
            $$$"""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"{{{lastUpdated}}}"}}""");

        index.ShouldNotContain(e => e.SearchParameter.Code == "_lastUpdated");
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("Patient/p1");
    }

    [Fact]
    public void GivenNoLastUpdated_WhenExtracted_ThenTheInnerEntriesAreReturnedUnchanged()
    {
        var logger = new RecordingLogger();

        (_, IReadOnlyCollection<SearchIndexEntry> index) = Index(
            new LastUpdatedSearchIndexer(Indexer, Definitions, logger),
            """{"resourceType":"Patient","id":"p1","gender":"female"}""");

        index.ShouldNotContain(e => e.SearchParameter.Code == "_lastUpdated");
        index.ShouldContain(e => e.SearchParameter.Code == "gender");
        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheStockIndexer_WhenExtracted_ThenItEmitsNoLastUpdatedEntry()
    {
        (_, IReadOnlyCollection<SearchIndexEntry> index) = Index("""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"2020-01-01T23:30:00Z"}}""");

        index.ShouldNotContain(e => e.SearchParameter.Code == "_lastUpdated");
    }

    [Fact]
    public void GivenLastUpdatedEntries_WhenSearchedByLastUpdated_ThenTheDefaultInterpreterMatchesThem()
    {
        var indexer = new LastUpdatedSearchIndexer(Indexer, Definitions, new RecordingLogger());

        string[] matched = Match(
            "Patient",
            "_lastUpdated=gt2020-01-01",
            [
                Index(indexer, """{"resourceType":"Patient","id":"old","meta":{"lastUpdated":"2019-05-01T00:00:00Z"}}"""),
                Index(indexer, """{"resourceType":"Patient","id":"new","meta":{"lastUpdated":"2022-05-01T00:00:00Z"}}"""),
                Index(indexer, """{"resourceType":"Patient","id":"none"}"""),
            ]);

        matched.ShouldBe(["new"]);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
