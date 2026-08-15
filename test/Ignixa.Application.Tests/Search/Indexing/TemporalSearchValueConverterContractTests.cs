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
using Ignixa.Serialization.Utilities;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Pins the contract the date search value converters depend on but never state: they read a temporal
/// element as <c>value.Value.ToString()</c>, and <see cref="IElement.Value"/> now hands them a
/// <see cref="FhirTemporal"/> rather than the raw wire string. That only keeps working because
/// <see cref="FhirTemporal.ToString"/> returns <see cref="FhirTemporal.Literal"/> verbatim.
/// </summary>
/// <remarks>
/// Switching any converter to <see cref="FhirTemporal.Value"/> would look like a type-safety improvement
/// and would silently break two things at once: partial-precision literals resolve to <see langword="null"/>
/// and would stop being indexed entirely, and timezone-less literals would be coerced to UTC, moving every
/// index row by the local offset. Neither shows up as an exception.
/// </remarks>
public class TemporalSearchValueConverterContractTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Theory]
    [InlineData("1974-12-25")]
    [InlineData("2013")]
    [InlineData("2013-04")]
    public void GivenAFhirTemporalBackedDateElement_WhenConverted_ThenItIndexesAsTheRawWireStringDid(string literal)
    {
        // Arrange
        var birthDate = ParseSingle($$"""{"resourceType":"Patient","id":"p1","birthDate":"{{literal}}"}""", "birthDate");
        birthDate.Value.ShouldBeOfType<FhirTemporal>();

        // Act
        var converted = new DateToDateTimeSearchValueConverter().ConvertTo(birthDate).ToList();

        // Assert
        var actual = converted.ShouldHaveSingleItem().ShouldBeOfType<DateTimeSearchValue>();
        var expected = new DateTimeSearchValue(PartialDateTime.Parse(literal));
        actual.Start.ShouldBe(expected.Start);
        actual.End.ShouldBe(expected.End);
    }

    [Theory]
    [InlineData("2013-04-02T09:30:10+01:00")]
    [InlineData("2013-04-02T09:30:10Z")]
    [InlineData("2013-04-02T09:30:10")]
    [InlineData("2013")]
    public void GivenAFhirTemporalBackedDateTimeElement_WhenConverted_ThenItIndexesAsTheRawWireStringDid(string literal)
    {
        // The timezone-less case is the one with teeth: FhirTemporal.Value resolves it to a UTC instant,
        // so a converter reading Value instead of the literal would move the indexed row by the offset
        // and no test that only covers offset-bearing literals would notice.

        // Arrange
        var effective = ParseSingle(
            $$"""{"resourceType":"Observation","id":"o1","status":"final","effectiveDateTime":"{{literal}}"}""",
            "effectiveDateTime");
        effective.Value.ShouldBeOfType<FhirTemporal>();

        // Act
        var converted = new DateToDateTimeSearchValueConverter().ConvertTo(effective).ToList();

        // Assert
        var actual = converted.ShouldHaveSingleItem().ShouldBeOfType<DateTimeSearchValue>();
        var expected = new DateTimeSearchValue(PartialDateTime.Parse(literal));
        actual.Start.ShouldBe(expected.Start);
        actual.End.ShouldBe(expected.End);
    }

    [Theory]
    [InlineData("2015-02-07T13:28:17.239+02:00")]
    [InlineData("2015-02-07T13:28:17Z")]
    public void GivenAFhirTemporalBackedInstantElement_WhenConverted_ThenItIndexesAsTheRawWireStringDid(string literal)
    {
        // The baseline here is deliberately not PartialDateTime.Parse. InstantToDateTimeSearchValueConverter
        // narrows through PrimitiveTypeConverter instead, which produces a point rather than a range, so a
        // second-precision instant and a second-precision dateTime index differently even when they carry
        // the identical literal. That is required, not incidental: the date search parameter section of the
        // spec (https://hl7.org/fhir/R4/search.html#date) gives dateTime "the range of the value as defined
        // above" but gives instant "an interval with an effective width of 0". Aligning the two converters
        // would therefore break whichever one was moved. DateSearchIndexingSemanticsTests asserts the two
        // shapes directly and cites the text; this test only pins the instant converter against its own
        // raw-string behaviour, so that the FhirTemporal-backed IElement.Value keeps feeding it what the
        // wire string used to.

        // Arrange
        var recorded = ParseSingle(
            $$"""{"resourceType":"Provenance","id":"pr1","recorded":"{{literal}}"}""",
            "recorded");
        recorded.Value.ShouldBeOfType<FhirTemporal>();

        // Act
        var converted = new InstantToDateTimeSearchValueConverter().ConvertTo(recorded).ToList();

        // Assert
        var actual = converted.ShouldHaveSingleItem().ShouldBeOfType<DateTimeSearchValue>();
        var expected = new DateTimeSearchValue(PrimitiveTypeConverter.ConvertTo<DateTimeOffset>(literal));
        actual.Start.ShouldBe(expected.Start);
        actual.End.ShouldBe(expected.End);
    }

    [Theory]
    [InlineData("2013-04-02T09:30:10+01:00", "2013-04-03T09:30:10+01:00")]
    [InlineData("2013-04-02T09:30:10", "2013-04-03T09:30:10")]
    [InlineData("2013", "2014")]
    public void GivenAFhirTemporalBackedPeriod_WhenConverted_ThenItIndexesAsTheRawWireStringsDid(
        string start,
        string end)
    {
        // Period reaches its bounds through IElement.Scalar, which also yields FhirTemporal now, so it
        // depends on the same ToString() contract one level down.

        // Arrange
        var period = ParseSingle(
            $$"""{"resourceType":"Encounter","period":{"start":"{{start}}","end":"{{end}}"},"status":"finished","id":"e1"}""",
            "period");

        // Act
        var converted = new PeriodToDateTimeSearchValueConverter().ConvertTo(period).ToList();

        // Assert
        var actual = converted.ShouldHaveSingleItem().ShouldBeOfType<DateTimeSearchValue>();
        var expected = new DateTimeSearchValue(PartialDateTime.Parse(start), PartialDateTime.Parse(end));
        actual.Start.ShouldBe(expected.Start);
        actual.End.ShouldBe(expected.End);
    }

    [Fact]
    public void GivenAFhirTemporalBackedElement_WhenStringified_ThenItReproducesTheWireLiteralExactly()
    {
        // The single assumption every converter above rests on, stated once and directly.

        // Arrange
        const string Literal = "2013-04-02T09:30:10+01:00";
        var effective = ParseSingle(
            $$"""{"resourceType":"Observation","id":"o1","status":"final","effectiveDateTime":"{{Literal}}"}""",
            "effectiveDateTime");

        // Assert
        effective.Value.ShouldBeOfType<FhirTemporal>();
        effective.Value!.ToString().ShouldBe(Literal);
    }

    private IElement ParseSingle(string json, string path)
    {
        var resource = JsonSourceNodeFactory.Parse(json).ToElement(_schemaProvider);

        return resource.Select(path).ShouldHaveSingleItem();
    }
}
