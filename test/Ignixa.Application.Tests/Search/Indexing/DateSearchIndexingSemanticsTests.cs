// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.FhirPath.Evaluation;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins the implicit range a temporal element occupies once indexed, which the FHIR search specification
/// defines PER FHIR DATATYPE rather than uniformly across the <c>date</c> search parameter type.
/// </summary>
/// <remarks>
/// <para>
/// From the <c>date</c> search parameter type section of the FHIR search specification
/// (https://hl7.org/fhir/R4/search.html#date), on the datatypes a date parameter may be used with:
/// </para>
/// <para>
/// <c>date</c> — "The range of the value is the day, month, or year as specified".
/// <c>dateTime</c> — "The range of the value as defined above; e.g. the date 2013-01-10 specifies all the
/// time from 00:00 on 10-Jan 2013 to immediately before 00:00 on 11-Jan 2013".
/// <c>instant</c> — "An instant is considered a fixed point in time with an interval smaller than the
/// precision of the system, i.e. an interval with an effective width of 0".
/// </para>
/// <para>
/// So a second-precision <c>dateTime</c> spans its whole second while a second-precision <c>instant</c> is
/// a zero-width point, even though the two share a wire format and can carry the identical literal. The
/// converters look inconsistent for that reason and are not: aligning them would break whichever one was
/// aligned onto the other. These tests exist so that the next reader finds the divergence asserted and
/// attributed rather than inferred from two implementations that happen to differ.
/// </para>
/// </remarks>
public class DateSearchIndexingSemanticsTests
{
    private const string SecondPrecisionLiteral = "2015-02-07T13:28:17Z";

    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly ISearchIndexer _indexer;

    public DateSearchIndexingSemanticsTests()
    {
        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            NullLoggerFactory.Instance,
            new SearchParameterDefinitionManager(_schemaProvider, new NullLogger<SearchParameterDefinitionManager>()),
            NullFhirBaseUriProvider.Instance);
    }

    [Fact]
    public void GivenAnInstantAtSecondPrecision_WhenIndexed_ThenItOccupiesAZeroWidthPoint()
    {
        // Arrange -- Provenance.recorded is typed instant, which the spec gives "an effective width of 0".
        var provenance = $$$"""
            {"resourceType":"Provenance","id":"pr1",
             "target":[{"reference":"Patient/p1"}],
             "recorded":"{{{SecondPrecisionLiteral}}}",
             "agent":[{"who":{"reference":"Practitioner/pra1"}}]}
            """;

        // Act
        var recorded = IndexedDate(provenance, "recorded");

        // Assert
        recorded.Start.ShouldBe(DateTimeOffset.Parse(SecondPrecisionLiteral).ToUniversalTime());
        recorded.End.ShouldBe(recorded.Start);
    }

    [Fact]
    public void GivenADateTimeAtSecondPrecision_WhenIndexed_ThenItSpansThatWholeSecond()
    {
        // Arrange -- Observation.effective[x] as dateTime carries the SAME literal as the instant above.
        var observation = $$"""
            {"resourceType":"Observation","id":"o1","status":"final",
             "code":{"coding":[{"system":"http://loinc.org","code":"8302-2"}]},
             "effectiveDateTime":"{{SecondPrecisionLiteral}}"}
            """;

        // Act
        var effective = IndexedDate(observation, "date");

        // Assert
        var second = DateTimeOffset.Parse(SecondPrecisionLiteral).ToUniversalTime();
        effective.Start.ShouldBe(second);
        effective.End.ShouldBe(second.AddSeconds(1).AddTicks(-1));
    }

    [Theory]
    [InlineData("2013")]
    [InlineData("2013-04")]
    [InlineData("2013-04-02")]
    public void GivenAReducedPrecisionDateTime_WhenIndexed_ThenItSpansTheUnitItWasWrittenAt(string literal)
    {
        // Arrange
        var observation = $$"""
            {"resourceType":"Observation","id":"o1","status":"final",
             "code":{"coding":[{"system":"http://loinc.org","code":"8302-2"}]},
             "effectiveDateTime":"{{literal}}"}
            """;

        // Act
        var effective = IndexedDate(observation, "date");

        // Assert -- lower bound is the first instant of the unit, upper the last tick of it.
        var start = DateTimeOffset.Parse(
            literal.Length switch { 4 => literal + "-01-01", 7 => literal + "-01", _ => literal },
            styles: System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        var end = literal.Length switch
        {
            4 => start.AddYears(1),
            7 => start.AddMonths(1),
            _ => start.AddDays(1),
        };

        effective.Start.ShouldBe(start);
        effective.End.ShouldBe(end.AddTicks(-1));
    }

    [Fact]
    public void GivenAnInstantAndADateTimeCarryingTheSameLiteral_WhenIndexed_ThenOnlyTheUpperBoundDiffers()
    {
        // The whole of the divergence, stated in one assertion: both lower bounds agree, and the instant's
        // upper bound collapses onto its lower bound while the dateTime's runs to the end of the second.
        // Anything that changes this is changing search semantics, not tidying duplicate code.

        // Arrange
        var provenance = $$$"""
            {"resourceType":"Provenance","id":"pr1",
             "target":[{"reference":"Patient/p1"}],
             "recorded":"{{{SecondPrecisionLiteral}}}",
             "agent":[{"who":{"reference":"Practitioner/pra1"}}]}
            """;
        var observation = $$"""
            {"resourceType":"Observation","id":"o1","status":"final",
             "code":{"coding":[{"system":"http://loinc.org","code":"8302-2"}]},
             "effectiveDateTime":"{{SecondPrecisionLiteral}}"}
            """;

        // Act
        var instant = IndexedDate(provenance, "recorded");
        var dateTime = IndexedDate(observation, "date");

        // Assert
        instant.Start.ShouldBe(dateTime.Start);
        instant.End.ShouldBe(instant.Start);
        dateTime.End.ShouldBeGreaterThan(instant.End);
    }

    [Fact]
    public void GivenAnInstantWithSubSecondPrecision_WhenIndexed_ThenItIsStillAZeroWidthPoint()
    {
        // Arrange -- the spec's "effective width of 0" makes no exception for how finely the instant was
        // written, so a millisecond-precision instant must not span its millisecond either.
        const string Literal = "2015-02-07T13:28:17.239+02:00";
        var provenance = $$$"""
            {"resourceType":"Provenance","id":"pr1",
             "target":[{"reference":"Patient/p1"}],
             "recorded":"{{{Literal}}}",
             "agent":[{"who":{"reference":"Practitioner/pra1"}}]}
            """;

        // Act
        var recorded = IndexedDate(provenance, "recorded");

        // Assert
        recorded.Start.ShouldBe(DateTimeOffset.Parse(Literal).ToUniversalTime());
        recorded.End.ShouldBe(recorded.Start);
    }

    private DateTimeSearchValue IndexedDate(string json, string parameterCode)
    {
        var element = JsonSourceNodeFactory.Parse(json).ToElement(_schemaProvider);

        return _indexer.Extract(element)
            .Where(entry => entry.SearchParameter.Code == parameterCode)
            .Select(entry => entry.Value)
            .OfType<DateTimeSearchValue>()
            .ShouldHaveSingleItem();
    }
}
