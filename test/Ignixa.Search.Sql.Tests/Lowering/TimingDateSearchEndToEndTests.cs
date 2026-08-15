using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Proves that a resource scheduled with a <c>Timing</c> is actually found by a date search, across the
/// whole path: the production indexer extracts the row, the production search-value parser reads the query
/// literal, and the production lowering rule decides the verdict.
/// </summary>
/// <remarks>
/// Before <c>TimingToDateTimeSearchValueConverter</c> existed there was no converter registered for the
/// Timing type at all, so <c>ServiceRequest.occurrenceTiming</c> produced zero <c>occurrence</c> rows and
/// every query below missed — silently, because a resource that indexes nothing is simply absent from the
/// result set rather than erroring. <see cref="GivenAServiceRequestScheduledWithATiming_WhenIndexed_ThenTheOccurrenceParameterHasARow"/>
/// is the regression guard for that specific "indexes nothing" failure; the rest show the row behaves.
/// </remarks>
public class TimingDateSearchEndToEndTests
{
    private const string BoundedTiming = """
        {"resourceType":"ServiceRequest","id":"s1","status":"active","intent":"order",
         "subject":{"reference":"Patient/p1"},
         "occurrenceTiming":{
           "repeat":{"boundsPeriod":{"start":"2015-02-07","end":"2015-03-07"},
                     "frequency":3,"period":1,"periodUnit":"d"}}}
        """;

    private static readonly R4CoreSchemaProvider SchemaProvider = new();

    private readonly ISearchIndexer _indexer = SearchIndexerFactory.CreateInstance(
        SchemaProvider,
        NullLoggerFactory.Instance,
        new SearchParameterDefinitionManager(SchemaProvider, new NullLogger<SearchParameterDefinitionManager>()),
        NullFhirBaseUriProvider.Instance);

    [Fact]
    public void GivenAServiceRequestScheduledWithATiming_WhenIndexed_ThenTheOccurrenceParameterHasARow()
    {
        // Act
        var row = OccurrenceRow(BoundedTiming);

        // Assert -- the bounding period's outer limits, floor to ceiling.
        row.Start.ShouldBe(DateTimeOffset.Parse("2015-02-07T00:00:00Z").ToUniversalTime());
        row.End.ShouldBe(DateTimeOffset.Parse("2015-03-08T00:00:00Z").ToUniversalTime().AddTicks(-1));
    }

    [Theory]
    [InlineData(SearchComparator.Eq, "2015", true)]
    [InlineData(SearchComparator.Eq, "2016", false)]
    [InlineData(SearchComparator.Ge, "2015-02-20", true)]
    [InlineData(SearchComparator.Le, "2015-02-20", true)]
    [InlineData(SearchComparator.Ge, "2015-04-01", false)]
    [InlineData(SearchComparator.Le, "2015-01-01", false)]
    [InlineData(SearchComparator.Sa, "2015-01-01", true)]
    [InlineData(SearchComparator.Eb, "2015-04-01", true)]
    public void GivenATimingBoundedResource_WhenSearchedByDate_ThenTheVerdictFollowsTheBoundingPeriod(
        SearchComparator comparator,
        string searchLiteral,
        bool expected)
    {
        // Arrange
        var row = OccurrenceRow(BoundedTiming);
        var searchValue = DateTimeSearchValue.Parse(searchLiteral);

        // Act
        var matched = DateRowMatcher.Matches(comparator, searchValue, row);

        // Assert
        matched.ShouldBe(expected);
    }

    [Fact]
    public void GivenATimingWithOnlyEvents_WhenSearchedByDate_ThenItIsFoundAcrossTheEventExtent()
    {
        // Arrange -- no bounds at all, so the outer limits are the extent of the event list.
        const string EventOnly = """
            {"resourceType":"ServiceRequest","id":"s2","status":"active","intent":"order",
             "subject":{"reference":"Patient/p1"},
             "occurrenceTiming":{"event":["2015-02-07T13:28:17Z","2015-03-09"]}}
            """;

        // Act
        var row = OccurrenceRow(EventOnly);

        // Assert -- earliest lower bound to latest upper bound, each event spanning its own precision.
        row.Start.ShouldBe(DateTimeOffset.Parse("2015-02-07T13:28:17Z").ToUniversalTime());
        row.End.ShouldBe(DateTimeOffset.Parse("2015-03-10T00:00:00Z").ToUniversalTime().AddTicks(-1));

        DateRowMatcher.Matches(SearchComparator.Eq, DateTimeSearchValue.Parse("2015"), row).ShouldBeTrue();
        DateRowMatcher.Matches(SearchComparator.Ge, DateTimeSearchValue.Parse("2015-03-01"), row).ShouldBeTrue();
        DateRowMatcher.Matches(SearchComparator.Eb, DateTimeSearchValue.Parse("2015-01-01"), row).ShouldBeFalse();
    }

    [Fact]
    public void GivenATimingWithNoResolvableExtent_WhenIndexed_ThenNoOccurrenceRowIsWritten()
    {
        // A duration with no anchor cannot be placed on the calendar, so there is nothing honest to index.
        const string DurationBounded = """
            {"resourceType":"ServiceRequest","id":"s3","status":"active","intent":"order",
             "subject":{"reference":"Patient/p1"},
             "occurrenceTiming":{"repeat":{"boundsDuration":{"value":10,"unit":"d"},"frequency":1}}}
            """;

        // Act
        var rows = OccurrenceRows(DurationBounded);

        // Assert
        rows.ShouldBeEmpty();
    }

    private DateTimeSearchValue OccurrenceRow(string json) => OccurrenceRows(json).ShouldHaveSingleItem();

    private IReadOnlyList<DateTimeSearchValue> OccurrenceRows(string json)
    {
        var element = JsonSourceNodeFactory.Parse(json).ToElement(SchemaProvider);

        return _indexer.Extract(element)
            .Where(entry => entry.SearchParameter.Code == "occurrence")
            .Select(entry => entry.Value)
            .OfType<DateTimeSearchValue>()
            .ToList();
    }
}
