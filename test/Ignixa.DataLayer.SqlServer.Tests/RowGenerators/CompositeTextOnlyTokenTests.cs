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
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

/// <summary>
/// Pins the composite row generators against a CodeableConcept that carries only <c>.text</c> and no
/// <c>.coding</c> -- an ordinary, spec-valid FHIR shape that indexes to a <see cref="TokenSearchValue"/>
/// with a null <c>Code</c>.
/// </summary>
/// <remarks>
/// Every composite TVP declares its token slots (<c>Code1</c>, <c>Code2</c>) NOT NULL, so writing DBNull
/// there is not a degraded index entry -- it is a hard SQL error that aborts the entire MergeResources
/// call for the resource. The only safe behaviour is to drop the composite row, which is what
/// <see cref="TokenSearchParameterRowGenerator"/> already does for the leaf TokenSearchParam table.
/// </remarks>
public class CompositeTextOnlyTokenTests
{
    private const string SearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-code-value";

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIdMap =
        new Dictionary<string, short> { ["Observation"] = 1, ["Patient"] = 2 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIdMap =
        new Dictionary<string, short> { [SearchParameterUrl] = 1 };

    private static readonly IReadOnlyDictionary<string, int> SystemMappings = new Dictionary<string, int>();

    private static readonly IReadOnlyDictionary<string, int> QuantityCodeMappings = new Dictionary<string, int>();

    [Fact]
    public void GivenATextOnlyTokenInTheFirstSlot_WhenTokenTokenCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(new TokenTokenCompositeRowGenerator(SystemMappings), Composite([TextOnlyToken()], [CodedToken()]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenATextOnlyTokenInTheSecondSlot_WhenTokenTokenCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(new TokenTokenCompositeRowGenerator(SystemMappings), Composite([CodedToken()], [TextOnlyToken()]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenATextOnlyToken_WhenRefTokenCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(new RefTokenCompositeRowGenerator(SystemMappings), Composite([Reference()], [TextOnlyToken()]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenAReferenceWithAResourceId_WhenRefTokenCompositeRowsAreGenerated_ThenTheRowIsStillWritten()
    {
        // Act
        var records = Generate(
            new RefTokenCompositeRowGenerator(SystemMappings),
            Composite([Reference()], [CodedToken()]));

        // Assert -- the ReferenceResourceId1 null-guard must not swallow ordinary references
        records.Count.ShouldBe(1);
        records[0].GetString(5).ShouldBe("p1");
        records[0].GetString(8).ShouldBe("1234-5");
    }

    [Fact]
    public void GivenATextOnlyToken_WhenTokenDateTimeCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(
                new TokenDateTimeCompositeRowGenerator(SystemMappings),
                Composite([TextOnlyToken()], [new DateTimeSearchValue(DateTimeOffset.UtcNow)]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenATextOnlyToken_WhenTokenNumberNumberCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(
                new TokenNumberNumberCompositeRowGenerator(SystemMappings),
                Composite([TextOnlyToken()], [new NumberSearchValue(1m)], [new NumberSearchValue(2m)]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenATextOnlyToken_WhenTokenQuantityCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(
                new TokenQuantityCompositeRowGenerator(SystemMappings, QuantityCodeMappings),
                Composite([TextOnlyToken()], [new QuantitySearchValue(null, null, 1m)]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenATextOnlyToken_WhenTokenStringCompositeRowsAreGenerated_ThenNoRowIsWritten()
        => Generate(
                new TokenStringCompositeRowGenerator(SystemMappings),
                Composite([TextOnlyToken()], [new StringSearchValue("Left kidney")]))
            .ShouldBeEmpty();

    [Fact]
    public void GivenACodeableConceptWithBothTextOnlyAndCodedTokens_WhenTokenTokenCompositeRowsAreGenerated_ThenOnlyTheCodedTokenIsIndexed()
    {
        // Arrange -- the shape a CodeableConcept with one text-only entry alongside a real coding produces
        var value = Composite([TextOnlyToken(), CodedToken()], [CodedToken()]);

        // Act
        var records = Generate(new TokenTokenCompositeRowGenerator(SystemMappings), value);

        // Assert -- the text-only pairing is dropped, the coded pairing survives
        records.Count.ShouldBe(1);
        records[0].GetString(4).ShouldBe("1234-5");
        records[0].GetString(7).ShouldBe("1234-5");
    }

    [Fact]
    public void GivenACodedToken_WhenTokenStringCompositeRowsAreGenerated_ThenTheRowIsStillWritten()
    {
        // Act
        var records = Generate(
            new TokenStringCompositeRowGenerator(SystemMappings),
            Composite([CodedToken()], [new StringSearchValue("Left kidney")]));

        // Assert -- the null-code guard must not swallow ordinary coded tokens
        records.Count.ShouldBe(1);
        records[0].GetString(4).ShouldBe("1234-5");
        records[0].IsDBNull(5).ShouldBeTrue();
    }

    private static TokenSearchValue TextOnlyToken() => new(system: null, code: null, text: "Left kidney");

    private static TokenSearchValue CodedToken() => new(system: null, "1234-5", text: null);

    private static ReferenceSearchValue Reference() =>
        new(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "p1");

    private static CompositeIndexSearchValue Composite(params IReadOnlyList<ISearchValue>[] components) => new(components);

    private static IReadOnlyList<SqlDataRecord> Generate(ISearchParameterRowGenerator generator, ISearchValue value)
    {
        var searchParameter = new SearchParameterInfo(
            "code-value", "code-value", SearchParamType.Composite, url: new Uri(SearchParameterUrl));

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
            NullLogger.Instance).ToList();
    }
}
