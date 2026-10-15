// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

/// <summary>
/// Pins the rows the row generators write for values the shared converters index (R4 resources indexed by the production
/// indexer), and that a reference too long for its columns is skipped and logged rather than truncated.
/// </summary>
/// <remarks>
/// The ContactPoint, quantity-unit, Range and <c>urn:</c> rows pin 4b's converter changes: they fail at 206143a and are
/// guards against b096951 (mutation-checked against the converters). The over-wide reference rows fail at b096951,
/// where <c>SetString</c> truncated them. The row whose base URI and id fit is a guard.
/// </remarks>
public class IndexedValueRowTests
{
    private const string Ucum = "http://unitsofmeasure.org";

    private static readonly R4CoreSchemaProvider Schema = new();

    private static readonly SearchParameterDefinitionManager Definitions = new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    private static readonly ISearchIndexer Indexer = SearchIndexerFactory.CreateInstance(Schema, NullLoggerFactory.Instance, Definitions, NullFhirBaseUriProvider.Instance);

    private static readonly IReadOnlyDictionary<string, short> ResourceTypeIds = new Dictionary<string, short> { ["Patient"] = 1, ["Observation"] = 2, ["RiskAssessment"] = 3 };

    private static readonly IReadOnlyDictionary<string, short> SearchParamIds = Definitions.AllSearchParameters
        .Select(parameter => parameter.Url.ToString())
        .Distinct()
        .Select((url, index) => (url, (short)(index + 1)))
        .ToDictionary(pair => pair.url, pair => pair.Item2);

    private static readonly IReadOnlyDictionary<string, int> Systems = new Dictionary<string, int> { [Ucum] = 7, ["http://loinc.org"] = 8, ["home"] = 9, ["phone"] = 10 };

    private static readonly IReadOnlyDictionary<string, int> QuantityCodes = new Dictionary<string, int> { ["[lb_av]"] = 11, ["lbs"] = 12, ["mg"] = 13, ["milligram"] = 14 };

    private const string WeightInPounds =
        """{"resourceType":"Observation","id":"w1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"29463-7"}]},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":185,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"}}""";

    [Fact]
    public void GivenAContactPoint_WhenTokenRowsAreGenerated_ThenTheRowHasTheValueAsCodeAndNoSystem()
    {
        SqlDataRecord row = Rows(new TokenSearchParameterRowGenerator(Systems), """{"resourceType":"Patient","id":"p1","telecom":[{"system":"phone","value":"555-1234","use":"home"}]}""", "individual-telecom")
            .ShouldHaveSingleItem();

        row.IsDBNull(3).ShouldBeTrue();
        row.GetString(4).ShouldBe("555-1234");
    }

    [Fact]
    public void GivenAQuantityWhoseUnitDiffersFromItsCode_WhenQuantityRowsAreGenerated_ThenAUnitRowWithNoSystemIsAdded()
    {
        IReadOnlyList<SqlDataRecord> rows = Rows(new QuantitySearchParameterRowGenerator(Systems, QuantityCodes), WeightInPounds, "Observation-value-quantity");

        rows.Select(row => (row.IsDBNull(3) ? (int?)null : row.GetInt32(3), row.GetInt32(4), row.GetDecimal(6), row.GetDecimal(7)))
            .ShouldBe([(7, 11, 185m, 185m), (null, 12, 185m, 185m)], ignoreOrder: true);
    }

    [Fact]
    public void GivenAQuantityWhoseUnitDiffersFromItsCode_WhenTokenQuantityCompositeRowsAreGenerated_ThenAUnitRowWithNoSystemIsAdded()
    {
        IReadOnlyList<SqlDataRecord> rows = Rows(new TokenQuantityCompositeRowGenerator(Systems, QuantityCodes), WeightInPounds, "Observation-code-value-quantity");

        rows.Select(row => (row.GetString(4), row.IsDBNull(6) ? (int?)null : row.GetInt32(6), row.GetInt32(7)))
            .ShouldBe([("29463-7", 7, 11), ("29463-7", null, 12)], ignoreOrder: true);
    }

    [Fact]
    public void GivenARiskAssessmentWithAProbabilityRange_WhenNumberRowsAreGenerated_ThenTheRangeBoundsAreWritten()
    {
        SqlDataRecord row = Rows(
                new NumberSearchParameterRowGenerator(),
                """{"resourceType":"RiskAssessment","id":"r1","status":"final","subject":{"reference":"Patient/p1"},"prediction":[{"probabilityRange":{"low":{"value":0.2},"high":{"value":0.5}}}]}""",
                "RiskAssessment-probability")
            .ShouldHaveSingleItem();

        (row.IsDBNull(3), row.GetDecimal(4), row.GetDecimal(5)).ShouldBe((true, 0.2m, 0.5m));
    }

    [Fact]
    public void GivenARangeWithUnits_WhenQuantityRowsAreGenerated_ThenTheBoundsAndAUnitRowAreWritten()
    {
        IElement range = ResourceJsonNode
            .Parse("""{"resourceType":"RiskAssessment","id":"r1","status":"final","subject":{"reference":"Patient/p1"},"prediction":[{"probabilityRange":{"low":{"value":2,"unit":"milligram","system":"http://unitsofmeasure.org","code":"mg"},"high":{"value":5,"unit":"milligram","system":"http://unitsofmeasure.org","code":"mg"}}}]}""")
            .ToElement(Schema)
            .Select("RiskAssessment.prediction.probability")
            .ShouldHaveSingleItem();
        var parameter = new SearchParameterInfo("range", "range", SearchParamType.Quantity, new Uri("http://example.org/SearchParameter/RiskAssessment-range"));
        List<SearchIndexEntry> entries = [.. new RangeToQuantitySearchValueConverter().ConvertTo(range).Select(value => new SearchIndexEntry(parameter, value))];

        IReadOnlyList<SqlDataRecord> rows = Generate(
            new QuantitySearchParameterRowGenerator(Systems, QuantityCodes),
            "RiskAssessment",
            entries,
            new Dictionary<string, short> { [parameter.Url.ToString()] = 99 });

        rows.Select(row => (row.IsDBNull(3) ? (int?)null : row.GetInt32(3), row.GetInt32(4), row.IsDBNull(5), row.GetDecimal(6), row.GetDecimal(7)))
            .ShouldBe([(7, 13, true, 2m, 5m), (null, 14, true, 2m, 5m)], ignoreOrder: true);
    }

    [Fact]
    public void GivenAUrnReference_WhenReferenceRowsAreGenerated_ThenTheWholeUrnIsTheIdWithNoBaseOrType()
    {
        SqlDataRecord row = Rows(
                new ReferenceSearchParameterRowGenerator(),
                """{"resourceType":"Observation","id":"u1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"}}""",
                "Observation-subject")
            .ShouldHaveSingleItem();

        (row.IsDBNull(3), row.IsDBNull(4), row.GetString(5)).ShouldBe((true, true, "urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"));
    }

    [Fact]
    public void GivenReferenceIdsLongerThanTheColumnSharingAPrefix_WhenReferenceRowsAreGenerated_ThenBothAreSkippedAndLoggedAndTheFittingOneIsWritten()
    {
        string prefix = "urn:oid:1.2.840.113619." + new string('1', 50);
        string fitting = "urn:oid:1.2.3";
        ILogger logger = Substitute.For<ILogger>();

        IReadOnlyList<SqlDataRecord> rows = Rows(
            new ReferenceSearchParameterRowGenerator(),
            $$$"""{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"{{{prefix}}}.1"},"performer":[{"reference":"{{{prefix}}}.2"},{"reference":"{{{fitting}}}"}]}""",
            parameterSuffix: null,
            logger);

        rows.Select(row => row.GetString(5)).ShouldBe([fitting]);
        logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning).ShouldBe(2);
    }

    [Fact]
    public void GivenABaseUriLongerThanTheColumn_WhenReferenceRowsAreGenerated_ThenTheRowIsSkippedAndLogged()
    {
        string longBase = "https://example.org/" + new string('b', 120) + "/fhir/";
        ILogger logger = Substitute.For<ILogger>();

        IReadOnlyList<SqlDataRecord> rows = Rows(
            new ReferenceSearchParameterRowGenerator(),
            $$$"""{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"{{{longBase}}}Patient/p1"}}""",
            "Observation-subject",
            logger);

        rows.ShouldBeEmpty();
        logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    public void GivenAReferenceWhoseBaseAndIdFit_WhenReferenceRowsAreGenerated_ThenTheRowIsWrittenUnchanged()
    {
        SqlDataRecord row = Rows(
                new ReferenceSearchParameterRowGenerator(),
                """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"http://other.example/fhir/Patient/p1"}}""",
                "Observation-subject")
            .ShouldHaveSingleItem();

        (row.GetString(3), row.GetInt16(4), row.GetString(5)).ShouldBe(("http://other.example/fhir/", (short)1, "p1"));
    }

    /// <summary>The rows <paramref name="generator"/> writes for <paramref name="json"/>, for the entries of the parameter whose URL ends with <paramref name="parameterSuffix"/> (all when null).</summary>
    private static IReadOnlyList<SqlDataRecord> Rows(ISearchParameterRowGenerator generator, string json, string? parameterSuffix, ILogger? logger = null)
    {
        var node = ResourceJsonNode.Parse(json);
        List<SearchIndexEntry> entries = [.. Indexer.Extract(node.ToElement(Schema))
            .Where(entry => parameterSuffix is null || entry.SearchParameter.Url.ToString().EndsWith("/" + parameterSuffix, StringComparison.Ordinal))];
        return Generate(generator, node.ResourceType, entries, SearchParamIds, logger);
    }

    private static IReadOnlyList<SqlDataRecord> Generate(
        ISearchParameterRowGenerator generator,
        string resourceType,
        List<SearchIndexEntry> entries,
        IReadOnlyDictionary<string, short> searchParamIds,
        ILogger? logger = null)
    {
        var resource = new ResourceWrapper(
            ResourceType: resourceType,
            ResourceId: "r",
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            Resource: new ResourceJsonNode { ResourceType = resourceType, Id = "r" },
            Request: new ResourceRequest("POST", resourceType))
        {
            SearchIndices = [.. entries],
        };

        return [.. generator.GenerateSqlDataRecords(
            [resource],
            ResourceTypeIds,
            searchParamIds,
            new Dictionary<ResourceWrapper, long> { [resource] = 1L },
            logger ?? NullLogger.Instance)];
    }
}
