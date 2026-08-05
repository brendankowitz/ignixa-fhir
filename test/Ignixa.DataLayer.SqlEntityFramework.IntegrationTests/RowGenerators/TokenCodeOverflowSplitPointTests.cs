// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient.Server;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.RowGenerators;

/// <summary>
/// Pins the token code-overflow split point from the writers' side. Both the search compiler
/// (TokenColumnEquality) and microsoft/fhir-server split a long code at the Code column's declared width;
/// these generators are what actually put the two halves in those columns. If a generator ever splits
/// somewhere else, the compiler searches for a prefix no row stores and every over-long token code
/// silently matches nothing -- no error, just an empty result set.
/// </summary>
/// <remarks>
/// The expected width comes from <see cref="SqlCatalog"/>, i.e. the DDL, which is the single source of
/// truth all three parties derive from. What this test adds is that the generators really do derive it
/// rather than carrying a literal that happens to agree today: they are driven for real, and a
/// reintroduced hard-coded width shows up here as a failure.
/// </remarks>
public class TokenCodeOverflowSplitPointTests
{
    private static readonly int InlineCodeWidth =
        SqlCatalog.Default.Table("TokenSearchParam").Column("Code").MaxLength!.Value;

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
        var code = new string('a', InlineCodeWidth) + new string('b', 37);

        // Act
        var record = Emit(slot, code);

        // Assert
        var (codeOrdinal, overflowOrdinal) = Ordinals(record, slot);
        record.GetString(codeOrdinal).ShouldBe(
            code[..InlineCodeWidth],
            $"{slot}: the generator split the code somewhere other than the Code column's declared width, " +
            "so the compiler's Code predicate compares against a prefix that is never stored");
        record.IsDBNull(overflowOrdinal).ShouldBeFalse($"{slot}: an over-long code must write a remainder");
        record.GetString(overflowOrdinal).ShouldBe(
            code[InlineCodeWidth..],
            $"{slot}: the remainder does not start at the Code column's declared width");
    }

    [Theory]
    [MemberData(nameof(TokenSlots))]
    public void GivenACodeOfExactlyTheCompilersSplitWidth_WhenTheRowGeneratorWritesIt_ThenTheOverflowColumnIsNull(string slot)
    {
        // Arrange — the boundary the compiler's exact-width arm depends on
        var code = new string('a', InlineCodeWidth);

        // Act
        var record = Emit(slot, code);

        // Assert
        var (codeOrdinal, overflowOrdinal) = Ordinals(record, slot);
        record.GetString(codeOrdinal).ShouldBe(code, $"{slot}: a code of exactly the split width belongs inline, whole");
        record.IsDBNull(overflowOrdinal).ShouldBeTrue(
            $"{slot}: the compiler emits 'CodeOverflow IS NULL' for an exact-width code to stop it matching a " +
            "truncated longer one; that guard only works if this column really is NULL here");
    }

    [Fact]
    public void GivenAnOverLongCode_WhenExtractingExtensionData_ThenTheJoinKeyMatchesTheStoredCode()
    {
        // Arrange — PostMergeExtensionUpdater locates the row it just merged by (…, SystemId, Code), so this
        // key has to be truncated exactly as GenerateSqlDataRecords truncated the Code column
        var code = new string('a', InlineCodeWidth) + new string('b', 37);
        var token = new TokenSearchValue(
            system: null,
            code,
            text: null,
            identifierTypeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
            identifierTypeCode: "MR");
        var resource = Leaf(token);

        // Act
        var extension = new TokenSearchParameterRowGenerator(NoSystemMappings)
            .ExtractExtensionData(
                [resource],
                ResourceTypeIdMap,
                SearchParamIdMap,
                new Dictionary<ResourceWrapper, long> { [resource] = 1L })
            .Single();

        // Assert
        extension.Code.ShouldBe(
            code[..InlineCodeWidth],
            "the extension-update join key must equal the Code value written to TokenSearchParam, or the " +
            "post-merge update silently stamps nothing and :of-type stops matching this identifier");
    }

    private static (int CodeOrdinal, int OverflowOrdinal) Ordinals(SqlDataRecord record, string slot)
    {
        // "TokenTokenCompositeSearchParam.Code2" -> Code2 / CodeOverflow2. Resolved by name so a column
        // added to a TVP moves the assertion with it instead of silently repointing it.
        var codeColumn = slot.Split('.')[1];
        var suffix = codeColumn["Code".Length..];
        return (record.GetOrdinal(codeColumn), record.GetOrdinal($"CodeOverflow{suffix}"));
    }

    private static SqlDataRecord Emit(string slot, string code)
    {
        var token = new TokenSearchValue(system: null, code, text: null);
        var otherToken = new TokenSearchValue(system: null, "short", text: null);

        return slot switch
        {
            "TokenSearchParam.Code" =>
                Single(new TokenSearchParameterRowGenerator(NoSystemMappings), Leaf(token)),

            "TokenTokenCompositeSearchParam.Code1" =>
                Single(new TokenTokenCompositeRowGenerator(NoSystemMappings), Composite([token], [otherToken])),

            "TokenTokenCompositeSearchParam.Code2" =>
                Single(new TokenTokenCompositeRowGenerator(NoSystemMappings), Composite([otherToken], [token])),

            "TokenDateTimeCompositeSearchParam.Code1" =>
                Single(
                    new TokenDateTimeCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))])),

            "TokenQuantityCompositeSearchParam.Code1" =>
                Single(
                    new TokenQuantityCompositeRowGenerator(NoSystemMappings, new Dictionary<string, int>()),
                    Composite([token], [new QuantitySearchValue(system: null, code: null, 5.4m)])),

            "TokenStringCompositeSearchParam.Code1" =>
                Single(
                    new TokenStringCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new StringSearchValue("Smith")])),

            "TokenNumberNumberCompositeSearchParam.Code1" =>
                Single(
                    new TokenNumberNumberCompositeRowGenerator(NoSystemMappings),
                    Composite([token], [new NumberSearchValue(1m)], [new NumberSearchValue(9m)])),

            "ReferenceTokenCompositeSearchParam.Code2" =>
                Single(
                    new RefTokenCompositeRowGenerator(NoSystemMappings),
                    Composite(
                        [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Organization", resourceId: "o1")],
                        [token])),

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

    private static ResourceWrapper Leaf(ISearchValue value) => Resource(value, SearchParamType.Token);

    private static ResourceWrapper Composite(params IReadOnlyList<ISearchValue>[] components)
        => Resource(new CompositeIndexSearchValue(components), SearchParamType.Composite);

    private static ResourceWrapper Resource(ISearchValue value, SearchParamType type)
    {
        var searchParameter = new SearchParameterInfo(
            "code-value", "code-value", type, url: new Uri(CompositeUrl));

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
