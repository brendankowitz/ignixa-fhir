// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.RowGenerators;

/// <summary>
/// Regression coverage for two confirmed bugs in TokenStringCompositeRowGenerator's string component:
/// (1) Text2 was split at a hardcoded 128 chars instead of the actual NVARCHAR(256) column width, and
/// (2) the value was stored ToUpperInvariant(), permanently destroying original case and (per the design
/// spec) providing no matching benefit today, since Text2's collation is already case-insensitive.
/// </summary>
public class CompositeStringOverflowTests
{
    private static readonly Uri TestParamUrl = new("http://example.org/SearchParameter/test-composite");
    private static readonly SearchParameterInfo TestCompositeParam =
        new("test-composite", "test-composite", SearchParamType.Composite, TestParamUrl);

    private static readonly IReadOnlyDictionary<string, int> EmptySystemMappings =
        new Dictionary<string, int>();
    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 3 };
    private static readonly IReadOnlyDictionary<string, short> SearchParameterIdMap =
        new Dictionary<string, short> { [TestParamUrl.ToString()] = 1 };

    private static ResourceWrapper CreateResourceWithComposite(CompositeSearchValue compositeValue)
    {
        var entry = new SearchIndexEntry(TestCompositeParam, compositeValue);
        return new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "obs-1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "obs-1" },
            Request: new ResourceRequest("POST", "Observation"),
            IsDeleted: false)
        {
            SearchIndices = [entry],
        };
    }

    [Fact]
    public void GivenMixedCaseStringComponent_WhenGenerated_ThenStoresOriginalCaseNotUppercased()
    {
        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, "code1", null)],
                [new StringSearchValue("Smith")],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenStringCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        records[0].GetString(6).ShouldBe("Smith"); // Column 6 = Text2 - original case, not "SMITH"
    }

    [Fact]
    public void GivenStringComponentOver256Chars_WhenGenerated_ThenSplitsAt256NotAt128()
    {
        var longText = new string('a', 260);
        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, "code1", null)],
                [new StringSearchValue(longText)],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenStringCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        records[0].GetString(6).Length.ShouldBe(256); // Text2 inline
        records[0].IsDBNull(7).ShouldBeFalse(); // TextOverflow2
        records[0].GetString(7).ShouldBe(longText[256..]);
    }
}
