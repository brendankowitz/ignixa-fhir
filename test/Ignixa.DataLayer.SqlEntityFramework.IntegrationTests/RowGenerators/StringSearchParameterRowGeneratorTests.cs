// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.RowGenerators;

/// <summary>
/// Unit tests for <see cref="StringSearchParameterRowGenerator"/>'s Text/TextOverflow write convention.
/// </summary>
/// <remarks>
/// Placed in this (SqlEntityFramework.IntegrationTests) project rather than the sibling
/// SqlEntityFramework.Tests project because the latter is not referenced by All.sln and does not
/// currently compile (pre-existing, unrelated to this fix) -- see task-1-report.md for details.
/// </remarks>
public class StringSearchParameterRowGeneratorTests
{
    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Patient"] = 1 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { ["http://hl7.org/fhir/SearchParameter/Patient-name"] = 1 };

    [Fact]
    public void GivenAStringLongerThan256Chars_WhenGeneratingRow_ThenTextOverflowHoldsTheWholeValue()
    {
        // Arrange
        var longValue = new string('A', 300);
        var generator = new StringSearchParameterRowGenerator();
        var resource = CreateResourceWithStringSearchValue(longValue);
        var resourceSurrogateIdMap = new Dictionary<ResourceWrapper, long> { [resource] = 1L };

        // Act
        var record = generator.GenerateSqlDataRecords(
            [resource], ResourceTypeIdMap, SearchParamIdMap, resourceSurrogateIdMap).Single();

        // Assert
        record.GetString(3).Length.ShouldBe(256);
        record.GetString(3).ShouldBe(longValue[..256]);
        record.GetString(4).ShouldBe(longValue);
    }

    [Fact]
    public void GivenAStringUnder256Chars_WhenGeneratingRow_ThenTextOverflowIsNull()
    {
        // Arrange
        var shortValue = "Smith";
        var generator = new StringSearchParameterRowGenerator();
        var resource = CreateResourceWithStringSearchValue(shortValue);
        var resourceSurrogateIdMap = new Dictionary<ResourceWrapper, long> { [resource] = 1L };

        // Act
        var record = generator.GenerateSqlDataRecords(
            [resource], ResourceTypeIdMap, SearchParamIdMap, resourceSurrogateIdMap).Single();

        // Assert
        record.GetString(3).ShouldBe(shortValue);
        record.IsDBNull(4).ShouldBeTrue();
    }

    private static ResourceWrapper CreateResourceWithStringSearchValue(string value)
    {
        var searchParameter = new SearchParameterInfo(
            "name", "name", SearchParamType.String,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var searchIndexEntry = new SearchIndexEntry(searchParameter, new StringSearchValue(value));

        return new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: "p1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Patient", Id = "p1" },
            Request: new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = new List<object> { searchIndexEntry },
        };
    }
}
