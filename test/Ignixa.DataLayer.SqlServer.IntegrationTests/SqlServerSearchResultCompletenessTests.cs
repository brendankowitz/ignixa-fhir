using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using SearchIndexEntry = Ignixa.Search.Indexing.SearchIndexEntry;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

#pragma warning disable CA1001 // xUnit owns disposal through IAsyncLifetime.
public class SqlServerSearchResultCompletenessTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private readonly SearchParameterDefinitionManager _definitions = new(
        FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        foreach (var parameter in new[]
        {
            _definitions.GetSearchParameter("Patient", "family"),
            _definitions.GetSearchParameter("Observation", "subject"),
        })
        {
            await _database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{parameter.Url}', 'active', SYSDATETIMEOFFSET(), 0)");
        }

        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId,
            NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData("unsorted", false)]
    [InlineData("valued", false)]
    [InlineData("missing", false)]
    [InlineData("cross-phase", false)]
    [InlineData("unsorted", true)]
    [InlineData("valued", true)]
    [InlineData("missing", true)]
    [InlineData("cross-phase", true)]
    public async Task GivenUnreadablePageMember_WhenHealthyProbeExists_ThenNextPagePreservesRemainingMatches(
        string phase, bool fetchMiss)
    {
        for (int i = 1; i <= 4; i++)
        {
            bool hasValue = phase == "valued" || (phase == "cross-phase" && i <= 2);
            await CreateAsync("Patient", $"patient-{i}", hasValue
                ? [new SearchIndexEntry(_definitions.GetSearchParameter("Patient", "family"),
                    new StringSearchValue($"Family{i}") { IsMin = true, IsMax = true })]
                : null);
        }

        ISqlExecutionService execution = _database.SqlExecutionService;
        if (fetchMiss)
        {
            execution = new MissingFetchExecution(execution,
                await _database.ExecuteScalarAsync<long>(
                    "SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = 'patient-1'"));
        }
        else
        {
            await _database.ExecuteNonQueryAsync(
                "UPDATE dbo.Resource SET RawResource = 0x01020304 WHERE ResourceId = 'patient-1'");
        }

        var options = new SearchOptions
        {
            ResourceType = "Patient",
            MaxItemCount = 2,
            ProbeExtraRow = true,
            Sort = phase == "unsorted" ? [] :
                [new SortExpression(_definitions.GetSearchParameter("Patient", "family"), SortOrder.Ascending)],
        };
        var service = CreateService(execution);

        using var first = await SerializeAsync(service, options);
        MatchIds(first).ShouldBe(["patient-2"]);
        first.RootElement.GetProperty("entry").EnumerateArray()
            .Count(e => e.GetProperty("search").GetProperty("mode").GetString() == "outcome")
            .ShouldBe(fetchMiss ? 0 : 1);
        if (!fetchMiss)
        {
            var issue = first.RootElement.GetProperty("entry").EnumerateArray()
                .Single(e => e.GetProperty("search").GetProperty("mode").GetString() == "outcome")
                .GetProperty("resource").GetProperty("issue")[0];
            issue.GetProperty("severity").GetString().ShouldBe("warning");
            issue.GetProperty("code").GetString().ShouldBe("incomplete");
            issue.GetProperty("diagnostics").GetString()!.ShouldContain("Patient/patient-1");
        }
        var next = first.RootElement.GetProperty("link").EnumerateArray()
            .Single(l => l.GetProperty("relation").GetString() == "next").GetProperty("url").GetString()!;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(next).Query);
        options.ContinuationToken = query["after"].ToString();

        using var second = await SerializeAsync(service, options);
        MatchIds(second).ShouldBe(["patient-3", "patient-4"]);
        second.RootElement.GetProperty("link").EnumerateArray()
            .ShouldNotContain(l => l.GetProperty("relation").GetString() == "next");
    }

    [Theory]
    [InlineData("unsorted")]
    [InlineData("_id")]
    [InlineData("valued")]
    [InlineData("missing")]
    public async Task GivenLargeRevincludeFanout_WhenNoIncludesCapRequested_ThenAllIncludesReturnWithoutProbeSeeds(string sort)
    {
        await CreateAsync("Patient", "patient-page", sort == "valued"
            ? [new SearchIndexEntry(_definitions.GetSearchParameter("Patient", "family"),
                new StringSearchValue("Family") { IsMin = true, IsMax = true })]
            : null);
        await CreateAsync("Patient", "patient-probe", null);
        for (int i = 1; i <= 25; i++)
        {
            await CreateObservationAsync($"observation-{i:D2}", "patient-page");
        }
        await CreateObservationAsync("observation-probe", "patient-probe");
        var subject = _definitions.GetSearchParameter("Observation", "subject");
        var options = new SearchOptions
        {
            ResourceType = "Patient",
            MaxItemCount = 1,
            ProbeExtraRow = true,
            Sort = sort != "unsorted"
                ? [new SortExpression(_definitions.GetSearchParameter("Patient", sort == "_id" ? "_id" : "family"),
                    SortOrder.Ascending)]
                : [],
            RevInclude = [new IncludeExpression(
                ["Observation"], subject, "Observation", "Patient", null, false, true, false)],
        };

        using var bundle = await SerializeAsync(CreateService(_database.SqlExecutionService), options);
        MatchIds(bundle).ShouldBe(["patient-page"]);
        bundle.RootElement.GetProperty("entry").EnumerateArray()
            .Where(e => e.GetProperty("search").GetProperty("mode").GetString() == "include")
            .Select(e => e.GetProperty("resource").GetProperty("id").GetString()).Order()
            .ShouldBe(
            [
                "observation-01", "observation-02", "observation-03", "observation-04", "observation-05",
                "observation-06", "observation-07", "observation-08", "observation-09", "observation-10",
                "observation-11", "observation-12", "observation-13", "observation-14", "observation-15",
                "observation-16", "observation-17", "observation-18", "observation-19", "observation-20",
                "observation-21", "observation-22", "observation-23", "observation-24", "observation-25",
            ]);
    }

    private Task CreateObservationAsync(string id, string patientId) => CreateAsync("Observation", id,
        [new SearchIndexEntry(_definitions.GetSearchParameter("Observation", "subject"),
            new ReferenceSearchValue(ReferenceKind.Internal, null!, "Patient", patientId))]);

    private async Task CreateAsync(string type, string id, IReadOnlyList<object>? indices)
    {
        var wrapper = new ResourceWrapper(
            type, id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{type}}","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"{type}/{id}"))
        {
            SearchIndices = indices,
        };
        await _database.Repository.CreateOrUpdateAsync(wrapper, CancellationToken.None);
    }

    private SqlServerCompiledSearchService CreateService(ISqlExecutionService execution) => new(
        execution, _database.TenantId, new SqlServerSymbolResolver(_cache),
        new CompartmentDefinitionManager(FhirVersion.R4), _definitions,
        new GzipResourceCompressor(new RecyclableMemoryStreamManager()), NullLogger.Instance);

    private static async Task<JsonDocument> SerializeAsync(SqlServerCompiledSearchService service, SearchOptions options)
    {
        using var output = new MemoryStream();
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            output, "searchset", null, service.SearchStreamAsync(options, CancellationToken.None),
            options, "http://localhost/Patient", "?_count=2", cancellationToken: CancellationToken.None);
        return JsonDocument.Parse(output.ToArray());
    }

    private static string?[] MatchIds(JsonDocument document) => document.RootElement.GetProperty("entry")
        .EnumerateArray().Where(e => e.GetProperty("search").GetProperty("mode").GetString() == "match")
        .Select(e => e.GetProperty("resource").GetProperty("id").GetString()).ToArray();

    // Real SQL does selection, sorting, and materialization. Only the batch-fetch boundary is faulted,
    // deterministically reproducing deletion after matching without relying on timing.
    private sealed class MissingFetchExecution(ISqlExecutionService inner, long missingId) : ISqlExecutionService
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            if (command.CommandText.Contains("FROM (VALUES", StringComparison.Ordinal))
            {
                foreach (SqlParameter parameter in command.Parameters)
                {
                    if (parameter.ParameterName.StartsWith("@Sid", StringComparison.Ordinal) &&
                        parameter.Value is long id && id == missingId)
                    {
                        parameter.Value = -1L;
                    }
                }
            }
            return inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
