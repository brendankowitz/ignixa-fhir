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
/// Pins the string slot of TokenStringCompositeSearchParam from the writer's side: Text2 keeps a redundant
/// prefix so the index can still seek, and TextOverflow2 holds the WHOLE value rather than the remainder.
/// </summary>
/// <remarks>
/// TokenStringLoweringRule switches to TextOverflow2 once the search value exceeds Text2's declared width and
/// compares it with LIKE @value%, so a writer that stored only the remainder there would make every string
/// component longer than that width match nothing -- silently, with an empty result set rather than an error.
/// This mirrors <see cref="StringSearchParameterRowGeneratorTests"/> for the leaf StringSearchParam table.
/// </remarks>
public class TokenStringCompositeTextOverflowTests
{
    private static readonly int Text2Width =
        SqlCatalog.Default.Table("TokenStringCompositeSearchParam").Column("Text2").MaxLength!.Value;

    private const string CompositeUrl = "http://hl7.org/fhir/SearchParameter/Observation-code-value";

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { [CompositeUrl] = 1 };

    [Fact]
    public void GivenAStringComponentLongerThanTheInlineWidth_WhenGeneratingRow_ThenTextOverflow2HoldsTheWholeValue()
    {
        // Arrange — distinct fill either side of the width, so a remainder-style split is visible
        var value = new string('a', Text2Width) + new string('b', 44);

        // Act
        var record = Emit(value);

        // Assert
        record.GetString(record.GetOrdinal("Text2")).ShouldBe(
            value.ToUpperInvariant()[..Text2Width],
            "Text2 keeps the leading prefix so a StartsWith search can still seek the index");
        record.GetString(record.GetOrdinal("TextOverflow2")).ShouldBe(
            value.ToUpperInvariant(),
            "TextOverflow2 must hold the WHOLE value, not the remainder: TokenStringLoweringRule compares it " +
            "with LIKE @value% once the search value overflows, and a remainder never starts with the value");
    }

    [Fact]
    public void GivenAStringComponentOfExactlyTheInlineWidth_WhenGeneratingRow_ThenTextOverflow2IsNull()
    {
        // Arrange — the boundary the lowering rule switches columns at
        var value = new string('a', Text2Width);

        // Act
        var record = Emit(value);

        // Assert
        record.GetString(record.GetOrdinal("Text2")).ShouldBe(value.ToUpperInvariant());
        record.IsDBNull(record.GetOrdinal("TextOverflow2")).ShouldBeTrue(
            "a value of exactly the inline width belongs in Text2 whole, which is the column the lowering " +
            "rule still reads at this length");
    }

    [Fact]
    public void GivenAShortStringComponent_WhenGeneratingRow_ThenTextOverflow2IsNull()
    {
        // Act
        var record = Emit("Smith");

        // Assert
        record.GetString(record.GetOrdinal("Text2")).ShouldBe("SMITH");
        record.IsDBNull(record.GetOrdinal("TextOverflow2")).ShouldBeTrue();
    }

    private static SqlDataRecord Emit(string stringComponent)
    {
        var value = new CompositeIndexSearchValue(
        [
            [new TokenSearchValue(system: null, "short-code", text: null)],
            [new StringSearchValue(stringComponent)],
        ]);

        var searchParameter = new SearchParameterInfo(
            "code-value", "code-value", SearchParamType.Composite, url: new Uri(CompositeUrl));

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

        return new TokenStringCompositeRowGenerator(new Dictionary<string, int>())
            .GenerateSqlDataRecords(
                [resource],
                ResourceTypeIdMap,
                SearchParamIdMap,
                new Dictionary<ResourceWrapper, long> { [resource] = 1L })
            .Single();
    }
}
