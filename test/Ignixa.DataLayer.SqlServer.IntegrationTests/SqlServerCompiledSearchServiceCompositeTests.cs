using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Row-level proof that composite search parameters return the right resources against a real
/// database. The compiler's lowering rules for these shapes are unit tested against emitted SQL, but
/// emitted SQL cannot say whether the rows the write path actually produced satisfy the predicate --
/// the two halves have to meet on real data, which is what these do.
/// </summary>
// CA1001 (owns disposable fields but isn't itself IDisposable): mirrors SqlServerCompiledSearchServiceTests.cs's
// own suppression rationale -- xunit already drives this type's lifecycle through IAsyncLifetime, and
// DisposeAsync below disposes every disposable field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceCompositeTests : IAsyncLifetime
#pragma warning restore CA1001
{
    // Pure, I/O-free lookup structure over the pre-generated R4 catalog -- matches every
    // FhirVersion.R4-hardcoded fixture elsewhere in this project.
    private static readonly SearchParameterDefinitionManager ParameterManager = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);
        var resolver = new SqlServerSymbolResolver(_searchCache);

        var compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

        _service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService,
            _database.TenantId,
            resolver,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// The one composite type the legacy engine never supported at all: its query generator had no
    /// <c>CompositeType.TokenNumberNumber</c> case, so it returned nothing for every such query
    /// regardless of data. Nothing executed this shape against real rows on this engine either --
    /// only a SQL-shape unit test existed, and no end-to-end fixture uses MolecularSequence.
    /// </summary>
    [Fact]
    public async Task GivenATokenNumberNumberComposite_WhenSearchStreamAsyncCalled_ThenOnlyTheResourceWhoseRangeSatisfiesEveryComponentMatches()
    {
        // Arrange
        var compositeParam = ParameterManager.GetSearchParameter("MolecularSequence", "chromosome-window-coordinate");
        var chromosomeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var startParam = compositeParam.Component[1].ResolvedSearchParameter!;
        var endParam = compositeParam.Component[2].ResolvedSearchParameter!;
        await SeedSearchParametersAsync(compositeParam, chromosomeParam, startParam, endParam);

        // Genuine ranges (Low != High), not single-point values: TokenNumberNumberCompositeRowGenerator
        // stores a Low == High value in SingleValue2/SingleValue3 only, leaving LowValue2/HighValue2/
        // LowValue3/HighValue3 NULL, while TokenNumberNumberLoweringRule queries only the Low/High
        // pair -- a pre-existing write-path/lowering mismatch, out of scope here. A real range
        // exercises the columns both sides agree on, isolating this test to composite support.
        var matchId = "tnn-match";
        var otherId = "tnn-other";
        await CreateResourceAsync("MolecularSequence", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "1", text: null)],
                [new NumberSearchValue(low: 100m, high: 101m)],
                [new NumberSearchValue(low: 199m, high: 200m)],
            ]))]);
        await CreateResourceAsync("MolecularSequence", otherId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "1", text: null)],
                [new NumberSearchValue(low: 500m, high: 501m)],
                [new NumberSearchValue(low: 599m, high: 600m)],
            ]))]);

        var options = new SearchOptions
        {
            ResourceType = "MolecularSequence",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(chromosomeParam, 0,
                        new SearchParameterPredicateExpression(chromosomeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1", text: null))),
                    new CompositeComponentExpression(startParam, 1,
                        new SearchParameterPredicateExpression(startParam, SearchComparator.Ge, modifier: null, new NumberSearchValue(100m))),
                    new CompositeComponentExpression(endParam, 2,
                        new SearchParameterPredicateExpression(endParam, SearchComparator.Le, modifier: null, new NumberSearchValue(200m))),
                ])),
        };

        // Act
        var results = await CollectAsync(options);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    /// <summary>
    /// <c>Observation?code-value-date</c> -- the TokenDateTime composite. It had no coverage at any
    /// level before this test.
    /// </summary>
    [Fact]
    public async Task GivenATokenDateTimeComposite_WhenSearchStreamAsyncCalled_ThenOnlyTheResourceSatisfyingBothComponentsMatches()
    {
        // Arrange
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-date");
        var codeParam = compositeParam.Component[0].ResolvedSearchParameter!;
        var dateParam = compositeParam.Component[1].ResolvedSearchParameter!;
        await SeedSearchParametersAsync(compositeParam, codeParam, dateParam);

        var matchDate = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tooEarlyDate = new DateTimeSearchValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var matchId = "tdt-match";
        var wrongDateId = "tdt-wrong-date";
        var wrongCodeId = "tdt-wrong-code";
        await CreateResourceAsync("Observation", matchId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
                [[new TokenSearchValue(system: null, code: "8480-6", text: null)], [matchDate]]))]);
        await CreateResourceAsync("Observation", wrongDateId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
                [[new TokenSearchValue(system: null, code: "8480-6", text: null)], [tooEarlyDate]]))]);

        // Same date, different code -- proves the token half of the composite is really applied, not
        // just the date half.
        await CreateResourceAsync("Observation", wrongCodeId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
                [[new TokenSearchValue(system: null, code: "8462-4", text: null)], [matchDate]]))]);

        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                compositeParam,
                new MultiaryExpression(MultiaryOperator.And,
                [
                    new CompositeComponentExpression(codeParam, 0,
                        new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                    new CompositeComponentExpression(dateParam, 1,
                        new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, matchDate)),
                ])),
        };

        // Act
        var results = await CollectAsync(options);

        // Assert
        results.Select(r => r.ResourceId).ShouldBe([matchId]);
    }

    /// <summary>
    /// <c>:missing=true</c> on a composite parameter, returning real rows. The only prior coverage was
    /// a plan-shape test for <c>:missing=false</c> on a different composite type, which cannot say
    /// whether the anti-join finds the right resources. Both directions are asserted here: an
    /// implementation that returned every resource for <c>:missing=true</c> would satisfy a
    /// "contains the un-indexed one" assertion but not this one.
    /// </summary>
    [Fact]
    public async Task GivenACompositeParameterMissingModifier_WhenSearchStreamAsyncCalled_ThenOnlyResourcesWithoutThatCompositeIndexedAreReturned()
    {
        // Arrange
        var compositeParam = ParameterManager.GetSearchParameter("Observation", "code-value-concept");
        await SeedSearchParametersAsync(compositeParam);

        var indexedId = "missing-composite-present";
        var notIndexedId = "missing-composite-absent";
        await CreateResourceAsync("Observation", indexedId,
            [new SearchIndexEntry(compositeParam, new CompositeIndexSearchValue(
            [
                [new TokenSearchValue(system: null, code: "8480-6", text: null)],
                [new TokenSearchValue(system: null, code: "high", text: null)],
            ]))]);
        await CreateResourceAsync("Observation", notIndexedId, null);

        var missingTrueOptions = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new MissingSearchParameterExpression(compositeParam, isMissing: true),
        };
        var missingFalseOptions = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new MissingSearchParameterExpression(compositeParam, isMissing: false),
        };

        // Act
        var missingTrue = await CollectAsync(missingTrueOptions);
        var missingFalse = await CollectAsync(missingFalseOptions);

        // Assert
        missingTrue.Select(r => r.ResourceId).ShouldBe([notIndexedId]);
        missingFalse.Select(r => r.ResourceId).ShouldBe([indexedId]);
    }

    private async Task SeedSearchParametersAsync(params SearchParameterInfo[] parameters)
    {
        foreach (var url in parameters.Select(parameter => parameter.Url!.ToString()).Distinct(StringComparer.Ordinal))
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }
    }

    private async Task CreateResourceAsync(string resourceType, string resourceId, IReadOnlyList<object>? searchIndices)
    {
        var resource = new ResourceWrapper(
            resourceType,
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"{resourceType}/{resourceId}"))
        {
            SearchIndices = searchIndices,
        };

        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }

    private async Task<List<SearchEntryResult>> CollectAsync(SearchOptions options)
    {
        var results = new List<SearchEntryResult>();
        await foreach (var result in _service.SearchStreamAsync(options, CancellationToken.None))
        {
            results.Add(result);
        }

        return results;
    }
}
