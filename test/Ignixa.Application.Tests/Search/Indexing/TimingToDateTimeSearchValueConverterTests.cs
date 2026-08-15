// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Covers the decision table <see cref="TimingToDateTimeSearchValueConverter"/> implements: which part of a
/// Timing supplies its outer limits, and when it has none.
/// </summary>
/// <remarks>
/// The specification reduces a Timing to one interval — "[Timing] the specified scheduling details are
/// ignored and only the outer limits matter" (https://hl7.org/fhir/R4/search.html#date) — but does not say
/// where to read those limits from when <c>bounds[x]</c> is a Duration or a Range, or when there is no
/// <c>bounds[x]</c> at all. Those are the choices asserted here, each with its reasoning on the test.
/// </remarks>
public class TimingToDateTimeSearchValueConverterTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly TimingToDateTimeSearchValueConverter _converter = new();

    [Fact]
    public void GivenABoundingPeriod_WhenConverted_ThenItIndexesExactlyAsThatPeriodWould()
    {
        // The bounding period is a Period, so it gets Period's semantics rather than a second
        // implementation of them -- asserted by comparison against the Period converter itself, so the two
        // cannot drift apart later.

        // Arrange
        var timing = Timing("""{"repeat":{"boundsPeriod":{"start":"2015-02-07","end":"2015-03-07"},"frequency":3,"period":1,"periodUnit":"d"}}""");
        var expected = new DateTimeSearchValue(
            PartialDateTime.Parse("2015-02-07"),
            PartialDateTime.Parse("2015-03-07"));

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(expected.Start);
        actual.End.ShouldBe(expected.End);
    }

    [Fact]
    public void GivenABoundingPeriodWithNoEnd_WhenConverted_ThenTheUpperBoundIsOpen()
    {
        // "Implicitly, a missing lower boundary is 'less than' any actual date" -- the same open-ended
        // treatment PeriodToDateTimeSearchValueConverter already applies, mirrored here.

        // Arrange
        var timing = Timing("""{"repeat":{"boundsPeriod":{"start":"2015-02-07"}}}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(new DateTimeSearchValue(PartialDateTime.Parse("2015-02-07")).Start);
        actual.End.ShouldBe(new DateTimeSearchValue(PartialDateTime.MaxValue, PartialDateTime.MaxValue).End);
    }

    [Fact]
    public void GivenABoundingPeriodWithNoStart_WhenConverted_ThenTheLowerBoundIsOpen()
    {
        // Arrange
        var timing = Timing("""{"repeat":{"boundsPeriod":{"end":"2015-03-07"}}}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(new DateTimeSearchValue(PartialDateTime.MinValue, PartialDateTime.MinValue).Start);
        actual.End.ShouldBe(new DateTimeSearchValue(PartialDateTime.Parse("2015-03-07")).End);
    }

    [Fact]
    public void GivenASingleEventAndNoBounds_WhenConverted_ThenItSpansThatEventsOwnPrecision()
    {
        // event is dateTime, and a dateTime denotes the range of the precision it was written at. A lone
        // day-precision event is therefore that whole day, not midnight -- the same literal in an
        // effectiveDateTime indexes identically, and the element carrying it must not change its meaning.

        // Arrange
        var timing = Timing("""{"event":["2015-03-09"]}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(DateTimeOffset.Parse("2015-03-09T00:00:00Z").ToUniversalTime());
        actual.End.ShouldBe(DateTimeOffset.Parse("2015-03-10T00:00:00Z").ToUniversalTime().AddTicks(-1));
    }

    [Fact]
    public void GivenSeveralUnorderedEvents_WhenConverted_ThenOneRowSpansTheirWholeExtent()
    {
        // "Only the outer limits matter" -- one row covering everything, not one row per occurrence, and
        // the extent is found by comparing bounds rather than by trusting document order.

        // Arrange
        var timing = Timing("""{"event":["2015-03-09","2015-02-07T13:28:17Z","2015-02-20"]}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(DateTimeOffset.Parse("2015-02-07T13:28:17Z").ToUniversalTime());
        actual.End.ShouldBe(DateTimeOffset.Parse("2015-03-10T00:00:00Z").ToUniversalTime().AddTicks(-1));
    }

    [Fact]
    public void GivenBothBoundsAndEvents_WhenConverted_ThenTheBoundingPeriodWins()
    {
        // bounds is the resource's own statement of its outer limits; the event list is a fallback for when
        // it has not made one. Widening the bounds to swallow a stray event would overrule the author.

        // Arrange
        var timing = Timing("""{"event":["2020-01-01"],"repeat":{"boundsPeriod":{"start":"2015-02-07","end":"2015-03-07"}}}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.End.ShouldBeLessThan(DateTimeOffset.Parse("2016-01-01T00:00:00Z"));
    }

    [Fact]
    public void GivenAnEmptyBoundingPeriodAndEvents_WhenConverted_ThenItFallsBackToTheEvents()
    {
        // A Period with neither bound would otherwise index as [MinValue, MaxValue] and match every date
        // query ever issued. Treating it as absent is strictly better than indexing "always".

        // Arrange
        var timing = Timing("""{"event":["2015-03-09"],"repeat":{"boundsPeriod":{},"frequency":2}}""");

        // Act
        var actual = Single(timing);

        // Assert
        actual.Start.ShouldBe(DateTimeOffset.Parse("2015-03-09T00:00:00Z").ToUniversalTime());
        actual.End.ShouldBe(DateTimeOffset.Parse("2015-03-10T00:00:00Z").ToUniversalTime().AddTicks(-1));
    }

    [Theory]
    [InlineData("""{"repeat":{"boundsDuration":{"value":10,"unit":"d"},"frequency":1}}""")]
    [InlineData("""{"repeat":{"boundsRange":{"low":{"value":1,"unit":"d"},"high":{"value":10,"unit":"d"}}}}""")]
    [InlineData("""{"repeat":{"frequency":3,"period":1,"periodUnit":"d"}}""")]
    [InlineData("""{"code":{"coding":[{"code":"BID"}]}}""")]
    public void GivenATimingWithNoResolvableExtent_WhenConverted_ThenNothingIsIndexed(string timingJson)
    {
        // A Duration or a Range bound states a length, not a position, so neither can be placed on the
        // calendar without inventing an origin. Indexing nothing leaves the resource out of date searches,
        // which is honest; indexing a guessed origin would put it in the wrong ones.

        // Arrange
        var timing = Timing(timingJson);

        // Act
        var converted = _converter.ConvertTo(timing).ToList();

        // Assert
        converted.ShouldBeEmpty();
    }

    private DateTimeSearchValue Single(IElement timing)
        => _converter.ConvertTo(timing).ShouldHaveSingleItem().ShouldBeOfType<DateTimeSearchValue>();

    private IElement Timing(string timingJson)
    {
        var json = $$"""
            {"resourceType":"ServiceRequest","id":"s1","status":"active","intent":"order",
             "subject":{"reference":"Patient/p1"},
             "occurrenceTiming":{{timingJson}}}
            """;

        var element = JsonSourceNodeFactory.Parse(json).ToElement(_schemaProvider);
        var timing = element.Select("occurrence").ShouldHaveSingleItem();

        timing.InstanceType.ShouldBe("Timing");

        return timing;
    }
}
