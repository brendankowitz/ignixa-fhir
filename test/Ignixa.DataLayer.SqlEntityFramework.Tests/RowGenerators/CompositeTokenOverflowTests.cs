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
/// Regression coverage for the confirmed bug: all 6 composite row generators split token codes at a
/// hardcoded 128 characters instead of TokenCodeStorage.MaxInlineCodeLength (256), which is the actual
/// width of the Code1/Code2 TVP and table columns - codes between 129 and 256 characters were being
/// truncated and overflowed unnecessarily, and (before the read-side fix in Task 5) never matched at all.
/// This test file only proves the WRITE side now splits at 256 - read-side matching is proven end-to-end
/// by the E2E tests added in Task 7.
/// </summary>
public class CompositeTokenOverflowTests
{
    // 24 x "z0123456789" (11 chars each) = 264 chars total, comfortably over the 256 inline threshold.
    private const string LongCode1 = "z0123456789z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789z0123456789" +
        "z0123456789z0123456789z0123456789z0123456789z0123456789z0123456789";

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
    public void GivenTokenTokenCompositeWithLongCode_WhenGenerated_ThenSplitsAt256NotAt128()
    {
        LongCode1.Length.ShouldBe(264);

        var compositeValue = new CompositeSearchValue(
            [
                [new TokenSearchValue(null, LongCode1, null)],
                [new TokenSearchValue(null, "short", null)],
            ]);
        var resource = CreateResourceWithComposite(compositeValue);
        var generator = new TokenTokenCompositeRowGenerator(EmptySystemMappings);

        var records = generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParameterIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 100L }).ToList();

        records.ShouldHaveSingleItem();
        // Column 4 = Code1 (inline), Column 5 = CodeOverflow1
        records[0].GetString(4).Length.ShouldBe(256);
        records[0].IsDBNull(5).ShouldBeFalse();
        records[0].GetString(5).ShouldBe(LongCode1[256..]);
    }
}
