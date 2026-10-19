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
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Proves that <c>_include</c>/<c>_revinclude</c> rows are bounded in SQL at the include window
/// (offset + <c>_includesCount</c>, plus the match page as slack) while paging through that window -- the way
/// the search bundle and <c>$includes</c> do -- still yields every included resource exactly once.
/// </summary>
// CA1001: xunit drives this type's lifecycle through IAsyncLifetime; DisposeAsync disposes every field.
#pragma warning disable CA1001
public class SqlServerCompiledSearchServiceIncludeLimitTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private static readonly SearchParameterInfo IdParameter = new(
        "_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static readonly SearchParameterInfo FamilyParameter = new(
        "family", "family", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/individual-family"));

    private readonly SearchParameterDefinitionManager _parameters = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _searchCache = null!;
    private IncludeRowCountingSqlExecutionService _sql = null!;
    private SqlServerCompiledSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        // The write path indexes a value only when its SearchParamId is already in the catalog.
        var urls = new[]
        {
            FamilyParameter.Url!.ToString(),
            SubjectParameter.Url!.ToString(),
            HasMemberParameter.Url!.ToString(),
            GeneralPractitionerParameter.Url!.ToString(),
        };
        foreach (var url in urls)
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }

        _sql = new IncludeRowCountingSqlExecutionService(_database.SqlExecutionService);
        _searchCache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _searchCache.PreloadResourceTypesAsync(CancellationToken.None);

        _service = new SqlServerCompiledSearchService(
            _sql,
            _database.TenantId,
            new SqlServerSymbolResolver(_searchCache),
            new CompartmentDefinitionManager(FhirVersion.R4),
            _parameters,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger.Instance);
    }

    public async Task DisposeAsync()
    {
        _searchCache.Dispose();
        await _database.DisposeAsync();
    }

    private SearchParameterInfo SubjectParameter => _parameters.GetSearchParameter("Observation", "subject");

    private SearchParameterInfo HasMemberParameter => _parameters.GetSearchParameter("Observation", "has-member");

    private SearchParameterInfo GeneralPractitionerParameter => _parameters.GetSearchParameter("Patient", "general-practitioner");

    [Fact]
    public async Task GivenMoreRevIncludedResourcesThanTheIncludesCount_WhenSearching_ThenSqlReadsOnlyTheIncludeWindowAndPagingReturnsEachResourceOnce()
    {
        // Arrange
        var patientId = $"cap-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId);

        var observationIds = new List<string>();
        for (var i = 0; i < 25; i++)
        {
            var observationId = $"cap-obs-{i:D2}-{Guid.NewGuid():N}";
            observationIds.Add(observationId);
            await CreateResourceAsync("Observation", observationId, [ReferenceTo(SubjectParameter, "Patient", patientId)]);
        }

        var search = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = IdEquals(patientId),
            RevInclude = [new IncludeExpression(["Observation"], SubjectParameter, "Observation", "Patient", null, wildCard: false, reversed: true, iterate: false)],
            MaxItemCount = 1,
            ProbeExtraRow = true,
        };

        // Act
        var pages = await ReadIncludePagesAsync(search, includesCount: 10, stageCount: 1);

        // Assert
        pages.Select(page => page.Count).ShouldBe([10, 10, 5]);
        pages.SelectMany(page => page).ShouldBe(observationIds, ignoreOrder: true);
    }

    [Fact]
    public async Task GivenAnIterateChildOfAParentRankedPastTheWindow_WhenSearching_ThenTheChildIsReturnedAtItsOwnRank()
    {
        // Arrange -- the child is written first, so it has the lowest surrogate id and ranks first among the
        // includes, but only the LAST parent references it. Seeding the iterate stage from the parent stage's
        // capped rows would never see that parent while the window still covers the child's rank.
        var patientId = $"iter-patient-{Guid.NewGuid():N}";
        await CreateResourceAsync("Patient", patientId);
        var childId = $"iter-child-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", childId);

        var parentIds = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var parentId = $"iter-parent-{i}-{Guid.NewGuid():N}";
            parentIds.Add(parentId);
            List<object> indices = [ReferenceTo(SubjectParameter, "Patient", patientId)];
            if (i == 7)
            {
                indices.Add(ReferenceTo(HasMemberParameter, "Observation", childId));
            }

            await CreateResourceAsync("Observation", parentId, indices);
        }

        var search = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = IdEquals(patientId),
            RevInclude = [new IncludeExpression(["Observation"], SubjectParameter, "Observation", "Patient", null, wildCard: false, reversed: true, iterate: false)],
            Include = [new IncludeExpression(["Observation"], HasMemberParameter, "Observation", "Observation", null, wildCard: false, reversed: false, iterate: true)],
            MaxItemCount = 1,
            ProbeExtraRow = true,
        };

        // Act
        var pages = await ReadIncludePagesAsync(search, includesCount: 2, stageCount: 2);

        // Assert
        pages[0].ShouldBe([childId, parentIds[0]]);
        pages.SelectMany(page => page).ShouldBe([childId, .. parentIds]);
    }

    [Fact]
    public async Task GivenAMatchThatIsAlsoTheFirstIncludeTarget_WhenPagingIncludes_ThenEveryOtherTargetIsStillReturned()
    {
        // Arrange -- the first match is also the include stage's lowest-ranked target. It is dropped from the
        // stage only after the stage's cap, so without slack for match rows it would consume the cap and hide
        // the lookahead row that proves another include page exists.
        var memberMatchId = $"slack-member-{Guid.NewGuid():N}";
        await CreateResourceAsync("Observation", memberMatchId);

        var otherMemberIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var memberId = $"slack-other-{i}-{Guid.NewGuid():N}";
            otherMemberIds.Add(memberId);
            await CreateResourceAsync("Observation", memberId);
        }

        var panelId = $"slack-panel-{Guid.NewGuid():N}";
        await CreateResourceAsync(
            "Observation",
            panelId,
            [.. new[] { memberMatchId }.Concat(otherMemberIds).Select(id => ReferenceTo(HasMemberParameter, "Observation", id))]);

        var search = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = IdIn(memberMatchId, panelId),
            Include = [new IncludeExpression(["Observation"], HasMemberParameter, "Observation", "Observation", null, wildCard: false, reversed: false, iterate: false)],
            MaxItemCount = 2,
            ProbeExtraRow = true,
        };

        // Act
        var pages = await ReadIncludePagesAsync(search, includesCount: 1, stageCount: 1);

        // Assert
        pages.SelectMany(page => page).ShouldBe(otherMemberIds);
    }

    [Fact]
    public async Task GivenASortedSearchWithAMultiTypeIncludeAcrossBothSortPhases_WhenPagingIncludes_ThenEachResourceIsReturnedOnce()
    {
        // Arrange -- general-practitioner targets several types. The first Organization is written before any
        // Practitioner, so Organization has the lower ResourceTypeId; later Organizations then rank AHEAD of
        // earlier Practitioners by (T1, Sid1) but BEHIND them by Sid1 alone, which is the include order under a
        // custom _sort. A per-stage TOP ranked by (T1, Sid1) would keep the wrong rows.
        var tag = Guid.NewGuid().ToString("N");
        var practitioners = new List<string>();
        var practitionersOfValuedPatients = new List<(string Type, string Id)>
        {
            ("Organization", $"gp-org-0-{tag}"),
            ("Practitioner", $"gp-prac-1-{tag}"),
        };
        for (var i = 1; i <= 10; i++)
        {
            practitionersOfValuedPatients.Add(("Organization", $"gp-org-{i}-{tag}"));
        }

        var practitionersOfMissingPatients = new List<(string Type, string Id)>();
        for (var i = 2; i <= 5; i++)
        {
            practitionersOfMissingPatients.Add(("Practitioner", $"gp-prac-{i}-{tag}"));
        }

        for (var i = 11; i <= 14; i++)
        {
            practitionersOfMissingPatients.Add(("Organization", $"gp-org-{i}-{tag}"));
        }

        foreach (var (type, id) in practitionersOfValuedPatients.Concat(practitionersOfMissingPatients))
        {
            await CreateResourceAsync(type, id);
            practitioners.Add(id);
        }

        for (var i = 0; i < 3; i++)
        {
            await CreatePatientWithPractitionersAsync($"gp-valued-{i}-{tag}", $"family-{i}", practitionersOfValuedPatients.Skip(i * 4).Take(4));
        }

        for (var i = 0; i < 2; i++)
        {
            await CreatePatientWithPractitionersAsync($"gp-missing-{i}-{tag}", family: null, practitionersOfMissingPatients.Skip(i * 4).Take(4));
        }

        var search = new SearchOptions
        {
            ResourceType = "Patient",
            Sort = [new SortExpression(FamilyParameter, SortOrder.Ascending)],
            Include = [new IncludeExpression(["Patient"], GeneralPractitionerParameter, "Patient", null, null, wildCard: false, reversed: false, iterate: false)],
            MaxItemCount = 5,
            ProbeExtraRow = true,
        };

        // Act
        var pages = await ReadIncludePagesAsync(search, includesCount: 3, stageCount: 1);

        // Assert -- the valued phase's includes in surrogate order, then the missing phase's.
        pages.SelectMany(page => page).ShouldBe(practitioners);
    }

    /// <summary>
    /// Pages through a search's includes as the search bundle and <c>$includes</c> do: the same match page,
    /// an include window of <paramref name="includesCount"/> starting at each offset, and one lookahead row to
    /// decide whether another page follows. Asserts every statement read no more include rows than the window
    /// allows: offset + count + lookahead, plus the match page each stage may also reach, per stage.
    /// </summary>
    private async Task<List<List<string>>> ReadIncludePagesAsync(SearchOptions search, int includesCount, int stageCount)
    {
        var matchFetch = search.MaxItemCount + (search.ProbeExtraRow ? 1 : 0);
        var pages = new List<List<string>>();

        for (var offset = 0; ; offset += includesCount)
        {
            offset.ShouldBeLessThan(1000, "include paging did not terminate");

            var options = new SearchOptions(search)
            {
                IncludesMaxItemCount = includesCount,
                IncludesContinuationToken = offset == 0 ? null! : IncludesContinuationToken.Encode(offset, includesCount),
            };

            _sql.Reset();
            var includes = new List<string>();
            await foreach (var entry in _service.SearchStreamAsync(options, CancellationToken.None))
            {
                if (entry.SearchMode == SearchEntryMode.Include)
                {
                    includes.Add(entry.ResourceId);
                }
            }

            var rowBound = stageCount * (offset + includesCount + 1 + matchFetch);
            _sql.IncludeRowsPerStatement.ShouldNotBeEmpty();
            _sql.IncludeRowsPerStatement.ShouldAllBe(rows => rows <= rowBound);

            var window = includes.Skip(offset).Take(includesCount + 1).ToList();
            pages.Add(window.Take(includesCount).ToList());
            if (window.Count <= includesCount)
            {
                return pages;
            }
        }
    }

    private static Expression IdEquals(string resourceId) => new SearchParameterExpression(IdParameter, IdPredicate(resourceId));

    private static Expression IdIn(params string[] resourceIds) => new SearchParameterExpression(
        IdParameter,
        Expression.Or([.. resourceIds.Select(IdPredicate)]));

    private static Expression IdPredicate(string resourceId)
        => new SearchParameterPredicateExpression(IdParameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: resourceId, text: null));

    private static SearchIndexEntry ReferenceTo(SearchParameterInfo parameter, string resourceType, string resourceId)
        => new(parameter, new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: resourceType, resourceId: resourceId));

    private async Task CreatePatientWithPractitionersAsync(string patientId, string? family, IEnumerable<(string Type, string Id)> practitioners)
    {
        List<object> indices = [.. practitioners.Select(p => ReferenceTo(GeneralPractitionerParameter, p.Type, p.Id))];
        if (family is not null)
        {
            indices.Add(new SearchIndexEntry(FamilyParameter, new StringSearchValue(family) { IsMin = true, IsMax = true }));
        }

        await CreateResourceAsync("Patient", patientId, indices);
    }

    private async Task CreateResourceAsync(string resourceType, string resourceId, IReadOnlyList<object>? searchIndices = null)
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
}
