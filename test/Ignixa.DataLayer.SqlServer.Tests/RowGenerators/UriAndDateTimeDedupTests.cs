// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

/// <summary>
/// UriSearchParamList and DateTimeSearchParamList both carry a PRIMARY KEY / UNIQUE constraint across
/// their key columns. Duplicate search index entries for the same resource (e.g. a repeated
/// meta.profile URI, or two identical DateTime values under one search parameter) must be
/// de-duplicated before being handed to the TVP, otherwise the SqlException thrown by the constraint
/// violation aborts the entire merge batch, not just the offending row.
/// </summary>
public class UriAndDateTimeDedupTests
{
    private const string UriSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Resource-profile";
    private const string DateTimeSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1 };

    [Fact]
    public void GivenTwoIdenticalUriValues_WhenUriRowsAreGenerated_ThenOnlyOneRowIsYielded()
    {
        // Arrange
        var searchParamIdMap = new Dictionary<string, short> { [UriSearchParameterUrl] = 1 };
        var searchParameter = new SearchParameterInfo("profile", "profile", SearchParamType.Uri, url: new Uri(UriSearchParameterUrl));
        var uriValue = new UriSearchValue("http://example.org/StructureDefinition/foo", separateCanonicalComponents: false);

        var resource = CreateResource(
            new SearchIndexEntry(searchParameter, uriValue),
            new SearchIndexEntry(searchParameter, uriValue));

        var generator = new UriSearchParameterRowGenerator();

        // Act
        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            searchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).ToList();

        // Assert
        records.Count.ShouldBe(1);
        records[0].GetString(3).ShouldBe(uriValue.Uri);
    }

    [Fact]
    public void GivenTwoDistinctUriValues_WhenUriRowsAreGenerated_ThenBothRowsAreYielded()
    {
        // Arrange
        var searchParamIdMap = new Dictionary<string, short> { [UriSearchParameterUrl] = 1 };
        var searchParameter = new SearchParameterInfo("profile", "profile", SearchParamType.Uri, url: new Uri(UriSearchParameterUrl));

        var resource = CreateResource(
            new SearchIndexEntry(searchParameter, new UriSearchValue("http://example.org/StructureDefinition/foo", separateCanonicalComponents: false)),
            new SearchIndexEntry(searchParameter, new UriSearchValue("http://example.org/StructureDefinition/bar", separateCanonicalComponents: false)));

        var generator = new UriSearchParameterRowGenerator();

        // Act
        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            searchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).ToList();

        // Assert
        records.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenTwoIdenticalDateTimeValues_WhenDateTimeRowsAreGenerated_ThenOnlyOneRowIsYielded()
    {
        // Arrange
        var searchParamIdMap = new Dictionary<string, short> { [DateTimeSearchParameterUrl] = 1 };
        var searchParameter = new SearchParameterInfo("date", "date", SearchParamType.Date, url: new Uri(DateTimeSearchParameterUrl));
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var dateTimeValue = new DateTimeSearchValue(instant);

        var resource = CreateResource(
            new SearchIndexEntry(searchParameter, dateTimeValue),
            new SearchIndexEntry(searchParameter, dateTimeValue));

        var generator = new DateTimeSearchParameterRowGenerator();

        // Act
        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            searchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).ToList();

        // Assert
        records.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenTwoDistinctDateTimeValues_WhenDateTimeRowsAreGenerated_ThenBothRowsAreYielded()
    {
        // Arrange
        var searchParamIdMap = new Dictionary<string, short> { [DateTimeSearchParameterUrl] = 1 };
        var searchParameter = new SearchParameterInfo("date", "date", SearchParamType.Date, url: new Uri(DateTimeSearchParameterUrl));

        var resource = CreateResource(
            new SearchIndexEntry(searchParameter, new DateTimeSearchValue(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))),
            new SearchIndexEntry(searchParameter, new DateTimeSearchValue(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))));

        var generator = new DateTimeSearchParameterRowGenerator();

        // Act
        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            searchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).ToList();

        // Assert
        records.Count.ShouldBe(2);
    }

    private static ResourceWrapper CreateResource(params SearchIndexEntry[] entries)
    {
        return new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "o1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "o1" },
            Request: new ResourceRequest("POST", "Observation"))
        {
            SearchIndices = entries.Cast<object>().ToList(),
        };
    }
}
