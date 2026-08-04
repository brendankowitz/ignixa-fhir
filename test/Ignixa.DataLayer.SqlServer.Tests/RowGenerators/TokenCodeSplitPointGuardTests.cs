// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

/// <summary>
/// Pins the compiler's token code-overflow split point to what the row generators actually write.
/// <para>
/// <b>Why this exists.</b> The two live in different assemblies with no shared symbol:
/// <c>TokenColumnEquality.InlineCodeWidth</c> decides where a searched-for code is cut in two, and every
/// token row generator cuts the stored code with its own literal <c>128</c>. When the compiler's split
/// point was changed to the <c>Code</c> column's declared <c>MaxLength</c> (256), every overflowing code
/// silently stopped matching and nine E2E tests failed; the compiler's own tests agreed with the wrong
/// value because they derived their expectation from the same lookup the rule read.
/// </para>
/// <para>
/// <b>Why behavioural, and why the generators are deliberately NOT refactored onto the constant.</b> A
/// shared constant would make divergence impossible, which sounds strictly better — but it would also make
/// every assertion here tautological, reading the number back from the place that produced it. That is the
/// exact failure mode of the tests this replaces. Driving the real generators and comparing their output
/// against the compiler's constant keeps two independent sources of truth and fails if <i>either</i> moves
/// alone.
/// </para>
/// </summary>
public class TokenCodeSplitPointGuardTests
{
    private const int SplitPoint = TokenColumnEquality.InlineCodeWidth;
    private const string SearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-code";

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1, ["Patient"] = 2 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { [SearchParameterUrl] = 1 };

    private static readonly IReadOnlyDictionary<string, int> SystemMappings = new Dictionary<string, int>();

    private static readonly IReadOnlyDictionary<string, int> QuantityCodeMappings = new Dictionary<string, int>();

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(new TokenSearchParameterRowGenerator(SystemMappings), Token, codeOrdinal: 4, overflowOrdinal: 5);

    [Fact]
    public void GivenACodeOfExactlyTheSplitWidth_WhenTokenRowsAreGenerated_ThenNoOverflowIsWritten()
    {
        // Arrange
        var code = new string('A', SplitPoint);

        // Act
        var record = GenerateSingleRecord(new TokenSearchParameterRowGenerator(SystemMappings), Token(code));

        // Assert -- the compiler's exact-width arm adds a "CodeOverflow IS NULL" guard, which only matches
        // rows written this way.
        record.GetString(4).ShouldBe(code);
        record.IsDBNull(5).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenTokenCompositeRowsAreGenerated_ThenBothSlotsSplitAtTheCompilersInlineCodeWidth()
    {
        AssertSplitMatchesCompiler(
            new TokenTokenCompositeRowGenerator(SystemMappings),
            code => Composite([Token(code)], [Token(code)]),
            codeOrdinal: 4,
            overflowOrdinal: 5);

        AssertSplitMatchesCompiler(
            new TokenTokenCompositeRowGenerator(SystemMappings),
            code => Composite([Token(code)], [Token(code)]),
            codeOrdinal: 7,
            overflowOrdinal: 8);
    }

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenStringCompositeRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(
            new TokenStringCompositeRowGenerator(SystemMappings),
            code => Composite([Token(code)], [new StringSearchValue("text")]),
            codeOrdinal: 4,
            overflowOrdinal: 5);

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenDateTimeCompositeRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(
            new TokenDateTimeCompositeRowGenerator(SystemMappings),
            code => Composite([Token(code)], [new DateTimeSearchValue(DateTimeOffset.UtcNow)]),
            codeOrdinal: 4,
            overflowOrdinal: 5);

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenQuantityCompositeRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(
            new TokenQuantityCompositeRowGenerator(SystemMappings, QuantityCodeMappings),
            code => Composite([Token(code)], [new QuantitySearchValue(null, null, 1m)]),
            codeOrdinal: 4,
            overflowOrdinal: 5);

    [Fact]
    public void GivenAnOverflowingCode_WhenTokenNumberNumberCompositeRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(
            new TokenNumberNumberCompositeRowGenerator(SystemMappings),
            code => Composite([Token(code)], [new NumberSearchValue(1m)], [new NumberSearchValue(2m)]),
            codeOrdinal: 4,
            overflowOrdinal: 5);

    [Fact]
    public void GivenAnOverflowingCode_WhenRefTokenCompositeRowsAreGenerated_ThenTheSplitMatchesTheCompilersInlineCodeWidth()
        => AssertSplitMatchesCompiler(
            new RefTokenCompositeRowGenerator(SystemMappings),
            code => Composite(
                [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "p1")],
                [Token(code)]),
            codeOrdinal: 8,
            overflowOrdinal: 9);

    private static void AssertSplitMatchesCompiler(
        ISearchParameterRowGenerator generator,
        Func<string, ISearchValue> valueFactory,
        int codeOrdinal,
        int overflowOrdinal)
    {
        // Arrange
        var code = new string('A', SplitPoint) + new string('B', 40);

        // Act
        var record = GenerateSingleRecord(generator, valueFactory(code));

        // Assert
        record.GetString(codeOrdinal).ShouldBe(code[..SplitPoint]);
        record.GetString(overflowOrdinal).ShouldBe(code[SplitPoint..]);
    }

    private static SqlDataRecord GenerateSingleRecord(ISearchParameterRowGenerator generator, ISearchValue value)
    {
        var resource = CreateResource(value);

        return generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIdMap,
            SearchParamIdMap,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            NullLogger.Instance).Single();
    }

    private static TokenSearchValue Token(string code) => new(system: null, code, text: null);

    private static CompositeIndexSearchValue Composite(params IReadOnlyList<ISearchValue>[] components)
        => new(components);

    private static ResourceWrapper CreateResource(ISearchValue value)
    {
        var searchParameter = new SearchParameterInfo(
            "code", "code", SearchParamType.Token, url: new Uri(SearchParameterUrl));

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
