// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

/// <summary>
/// Pins the date slot of TokenDateTimeCompositeSearchParam to the column type it is actually declared as.
/// </summary>
/// <remarks>
/// <c>StartDateTime2</c>/<c>EndDateTime2</c> are DATETIMEOFFSET(7). Describing them to the TVP as
/// <see cref="SqlDbType.DateTime"/> costs two things that both fail quietly or late: DATETIME cannot
/// represent anything before 1753-01-01 (historical <c>birthDate</c>, <c>Provenance.occurred</c>), and it
/// quantizes to ~3.33ms, so a boundary such as <c>...T23:59:59.9999999</c> lands on a different instant
/// than the leaf DateTimeSearchParam table stores for the same value -- range queries then disagree
/// between the composite index and the leaf index with no error anywhere.
/// </remarks>
public class TokenDateTimeCompositeInstantFidelityTests
{
    private const string CompositeUrl = "http://hl7.org/fhir/SearchParameter/Observation-code-value-date";
    private const string LeafUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";

    private const int StartOrdinal = 6;
    private const int EndOrdinal = 7;

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { [CompositeUrl] = 1, [LeafUrl] = 2 };

    private static readonly IReadOnlyDictionary<string, int> SystemMappings = new Dictionary<string, int>();

    [Fact]
    public void GivenTheCompositeMetadata_WhenGeneratingRows_ThenTheDateSlotsAreDeclaredAsDateTimeOffset()
    {
        // Act
        var record = EmitComposite(new DateTimeSearchValue(DateTimeOffset.UtcNow));

        // Assert -- SqlDbType.DateTime here would silently narrow every value written below
        record.GetSqlMetaData(StartOrdinal).SqlDbType.ShouldBe(SqlDbType.DateTimeOffset);
        record.GetSqlMetaData(EndOrdinal).SqlDbType.ShouldBe(SqlDbType.DateTimeOffset);
    }

    [Fact]
    public void GivenADateBefore1753_WhenGeneratingCompositeRow_ThenTheInstantIsWrittenIntact()
    {
        // Arrange -- outside DATETIME's representable range entirely
        var value = new DateTimeSearchValue(new DateTimeOffset(1601, 3, 4, 6, 7, 8, TimeSpan.Zero));

        // Act
        var record = EmitComposite(value);

        // Assert
        record.GetDateTimeOffset(StartOrdinal).ShouldBe(value.Start);
        record.GetDateTimeOffset(EndOrdinal).ShouldBe(value.End);
    }

    [Fact]
    public void GivenAnInstantFinerThanDateTimesTickResolution_WhenGeneratingCompositeRow_ThenNoPrecisionIsLost()
    {
        // Arrange -- .1234567 is not on DATETIME's ~3.33ms grid, so a DATETIME slot would round it away
        var instant = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1234567);
        var value = new DateTimeSearchValue(instant);

        // Act
        var record = EmitComposite(value);

        // Assert
        record.GetDateTimeOffset(StartOrdinal).ShouldBe(value.Start);
        record.GetDateTimeOffset(StartOrdinal).Ticks.ShouldBe(value.Start.Ticks);
    }

    [Fact]
    public void GivenTheSameDateTimeValue_WhenGeneratingCompositeAndLeafRows_ThenBothIndexTheSameInstants()
    {
        // Arrange -- an end-of-period boundary, which is where rounding differences change query results
        var value = new DateTimeSearchValue(
            new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999));

        // Act
        var composite = EmitComposite(value);
        var leaf = EmitLeaf(value);

        // Assert -- the composite index must agree with the leaf index it is range-compared against
        composite.GetDateTimeOffset(StartOrdinal).ShouldBe(leaf.GetDateTimeOffset(3));
        composite.GetDateTimeOffset(EndOrdinal).ShouldBe(leaf.GetDateTimeOffset(4));
    }

    private static SqlDataRecord EmitComposite(DateTimeSearchValue dateTimeValue)
    {
        var value = new CompositeIndexSearchValue(
        [
            [new TokenSearchValue(system: null, "1234-5", text: null)],
            [dateTimeValue],
        ]);

        return Emit(new TokenDateTimeCompositeRowGenerator(SystemMappings), CompositeUrl, SearchParamType.Composite, value);
    }

    private static SqlDataRecord EmitLeaf(DateTimeSearchValue dateTimeValue)
        => Emit(new DateTimeSearchParameterRowGenerator(), LeafUrl, SearchParamType.Date, dateTimeValue);

    private static SqlDataRecord Emit(
        ISearchParameterRowGenerator generator,
        string url,
        SearchParamType searchParamType,
        ISearchValue value)
    {
        var searchParameter = new SearchParameterInfo("p", "p", searchParamType, url: new Uri(url));

        var resource = new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "o1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "o1" },
            Request: new ResourceRequest("POST", "Observation"))
        {
            SearchIndices = new List<object> { new SearchIndexEntry(searchParameter, value) },
        };

        return generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).Single();
    }
}
