// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient.Server;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.RowGenerators;

/// <summary>
/// Pins the token code-overflow split point from the writers' side. The search compiler splits a long code
/// at <see cref="TokenColumnEquality.InlineCodeWidth"/> to build its <c>Code = @prefix AND CodeOverflow =
/// @remainder</c> predicate; these generators are what actually put the two halves in those columns. If the
/// two ever disagree, the compiler searches for a prefix no row stores and every over-long token code
/// silently matches nothing -- no error, just an empty result set.
/// </summary>
/// <remarks>
/// This lives here, driving the real generators, precisely because Ignixa.Search.Sql's own tests cannot
/// catch that class of divergence: the only split width available inside that assembly is whatever the rule
/// itself uses, so a test written there agrees with any value the rule picks. The generators are the
/// independent second source of truth, and they are deliberately left holding their own literal 128 --
/// pointing them at the constant would collapse the two sources back into one and re-open the hole.
/// </remarks>
public class TokenCodeOverflowSplitPointTests
{
    private const string CompositeUrl = "http://hl7.org/fhir/SearchParameter/Observation-code-value";

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1, ["Organization"] = 2 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { [CompositeUrl] = 1 };

    private static readonly IReadOnlyDictionary<string, int> NoSystemMappings = new Dictionary<string, int>();

    public static TheoryData<string> TokenSlots() => new()
    {
        "TokenSearchParam.Code",
        "TokenTokenCompositeSearchParam.Code1",
        "TokenTokenCompositeSearchParam.Code2",
        "TokenDateTimeCompositeSearchParam.Code1",
        "TokenQuantityCompositeSearchParam.Code1",
        "TokenStringCompositeSearchParam.Code1",
        "TokenNumberNumberCompositeSearchParam.Code1",
        "ReferenceTokenCompositeSearchParam.Code2",
    };

    [Theory]
    [MemberData(nameof(TokenSlots))]
    public void GivenACodeLongerThanTheCompilersSplitPoint_WhenTheRowGeneratorWritesIt_ThenItDividesAtExactlyThatPoint(string slot)
    {
        // Arrange — distinct fill either side of the split point, so a split at any other offset shows up
        var code = new string('a', TokenColumnEquality.InlineCodeWidth) + new string('b', 37);

        // Act
        var (record, codeOrdinal, overflowOrdinal) = Emit(slot, code);

        // Assert
        record.GetString(codeOrdinal).ShouldBe(
            code[..TokenColumnEquality.InlineCodeWidth],
            $"{slot}: the generator split the code somewhere other than TokenColumnEquality.InlineCodeWidth, " +
            "so the compiler's Code predicate compares against a prefix that is never stored");
        record.IsDBNull(overflowOrdinal).ShouldBeFalse($"{slot}: an over-long code must write a remainder");
        record.GetString(overflowOrdinal).ShouldBe(
            code[TokenColumnEquality.InlineCodeWidth..],
            $"{slot}: the remainder does not start at TokenColumnEquality.InlineCodeWidth");
    }

    [Theory]
    [MemberData(nameof(TokenSlots))]
    public void GivenACodeOfExactlyTheCompilersSplitWidth_WhenTheRowGeneratorWritesIt_ThenTheOverflowColumnIsNull(string slot)
    {
        // Arrange — the boundary the compiler's exact-width arm depends on
        var code = new string('a', TokenColumnEquality.InlineCodeWidth);

        // Act
        var (record, codeOrdinal, overflowOrdinal) = Emit(slot, code);

        // Assert
        record.GetString(codeOrdinal).ShouldBe(code, $"{slot}: a code of exactly the split width belongs inline, whole");
        record.IsDBNull(overflowOrdinal).ShouldBeTrue(
            $"{slot}: the compiler emits 'CodeOverflow IS NULL' for an exact-width code to stop it matching a " +
            "truncated longer one; that guard only works if this column really is NULL here");
    }

    private static (SqlDataRecord Record, int CodeOrdinal, int OverflowOrdinal) Emit(string slot, string code)
    {
        var token = new TokenSearchValue(system: null, code, text: null);
        var otherToken = new TokenSearchValue(system: null, "short", text: null);

        return slot switch
        {
            "TokenSearchParam.Code" => (
                Single(new TokenSearchParameterRowGenerator(NoSystemMappings), Leaf(token)), 4, 5),

            "TokenTokenCompositeSearchParam.Code1" => (
                Single(new TokenTokenCompositeRowGenerator(NoSystemMappings), Composite([token], [otherToken])), 4, 5),

            "TokenTokenCompositeSearchParam.Code2" => (
                Single(new TokenTokenCompositeRowGenerator(NoSystemMappings), Composite([otherToken], [token])), 7, 8),

            "TokenDateTimeCompositeSearchParam.Code1" => (
                Single(
                    new TokenDateTimeCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))])),
                4, 5),

            "TokenQuantityCompositeSearchParam.Code1" => (
                Single(
                    new TokenQuantityCompositeRowGenerator(NoSystemMappings, new Dictionary<string, int>()),
                    Composite([token], [new QuantitySearchValue(system: null, code: null, 5.4m)])),
                4, 5),

            "TokenStringCompositeSearchParam.Code1" => (
                Single(
                    new TokenStringCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new StringSearchValue("Smith")])),
                4, 5),

            "TokenNumberNumberCompositeSearchParam.Code1" => (
                Single(
                    new TokenNumberNumberCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new NumberSearchValue(1m)], [new NumberSearchValue(9m)])),
                4, 5),

            "ReferenceTokenCompositeSearchParam.Code2" => (
                Single(
                    new RefTokenCompositeRowGenerator(NoSystemMappings),
                    Composite(
                        [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: "o1")],
                        [token])),
                8, 9),

            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown token slot."),
        };
    }

    private static SqlDataRecord Single(ISearchParameterRowGenerator generator, ResourceWrapper resource)
    {
        var resourceSurrogateIdMap = new Dictionary<ResourceWrapper, long> { [resource] = 1L };

        return generator
            .GenerateSqlDataRecords([resource], ResourceTypeIdMap, SearchParamIdMap, resourceSurrogateIdMap)
            .Single();
    }

    private static ResourceWrapper Leaf(ISearchValue value) => Resource(value);

    private static ResourceWrapper Composite(params IReadOnlyList<ISearchValue>[] components)
        => Resource(new CompositeIndexSearchValue(components));

    private static ResourceWrapper Resource(ISearchValue value)
    {
        var searchParameter = new SearchParameterInfo(
            "code-value", "code-value", SearchParamType.Composite, url: new Uri(CompositeUrl));

        return new ResourceWrapper(
            ResourceType: "Observation",
            ResourceId: "o1",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = "Observation", Id = "o1" },
            Request: new ResourceRequest("POST", "Observation"))
        {
            SearchIndices = new List<object> { new SearchIndexEntry(searchParameter, value) },
        };
    }
}
