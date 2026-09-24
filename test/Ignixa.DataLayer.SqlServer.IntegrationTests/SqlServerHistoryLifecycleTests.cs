using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.History;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerHistoryLifecycleTests : IAsyncLifetime
{
    private const string ResourceId = "history-lifecycle";
    private static readonly DateTimeOffset Epoch = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(0, 2005)]
    [InlineData(1, 2007)]
    [InlineData(2, 2008)]
    public async Task GivenMoreThanTwoThousandVersions_WhenRequestingAccurateHistory_ThenCountsAllStoredVersionsAndPagesNormally(
        int scope, int expected)
    {
        await SeedHistoryAsync(2005);
        var parameters = new HistoryQueryParameters { Count = 17, Offset = 1000, Total = TotalMode.Accurate };

        HistoryResult result = await HandleAsync(scope, parameters);

        result.TotalCount.ShouldBe(expected);
        using JsonDocument bundle = await SerializeAsync(result, parameters.Count);
        bundle.RootElement.GetProperty("total").GetInt32().ShouldBe(expected);
        bundle.RootElement.GetProperty("entry").GetArrayLength().ShouldBe(17);
        var links = bundle.RootElement.GetProperty("link").EnumerateArray().ToList();
        links.ShouldContain(link => link.GetProperty("relation").GetString() == "next");
        links.Single(link => link.GetProperty("relation").GetString() == "last")
            .GetProperty("url").GetString()!.ShouldContain($"_offset={(expected - 1) / 17 * 17}");

        var capped = await ReadHistory(scope, parameters with { Count = int.MaxValue, Offset = 0 }).ToListAsync();
        capped.Count.ShouldBe(HistoryQueryParameters.MaxCount + 1);
        capped.Count(entry => !entry.IsPagingProbe).ShouldBe(HistoryQueryParameters.MaxCount);
        capped.Last().IsPagingProbe.ShouldBeTrue();

        parameters = parameters with { Count = 1000, Offset = 2000 };
        result = await HandleAsync(scope, parameters);
        using JsonDocument lastPage = await SerializeAsync(result, parameters.Count);
        lastPage.RootElement.GetProperty("entry").GetArrayLength().ShouldBe(expected - 2000);
        lastPage.RootElement.GetProperty("link").EnumerateArray()
            .ShouldNotContain(link => link.GetProperty("relation").GetString() == "next");
    }

    [Theory]
    [InlineData(0, 2005)]
    [InlineData(1, 2007)]
    [InlineData(2, 2008)]
    public async Task GivenUnreadableBodies_WhenCountingHistory_ThenCountsMetadataWithoutLoadingBodies(int scope, int expected)
    {
        await SeedHistoryAsync(2005);
        await _database.ExecuteNonQueryAsync("UPDATE dbo.Resource SET RawResource = 0xDEADBEEF");

        int count = await CountAsync(scope, new HistoryQueryParameters { Count = 1, Offset = 1999 });

        count.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenPutThenCutoffThenStandaloneDelete_WhenReadingIncrementalHistory_ThenReturnsTheDeletion(int scope)
    {
        await PutAsync();
        var key = new ResourceKey("Patient", ResourceId);
        DateTimeOffset beforeDelete = DateTimeOffset.UtcNow;
        // Keep the cutoff strictly after the PUT and before the tombstone's millisecond timestamp.
        await Task.Delay(20);
        await _database.Repository.DeleteAsync(key, new ResourceRequest("DELETE", $"Patient/{ResourceId}"),
            transactionId: null, cancellationToken: CancellationToken.None);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsDeleted = 1 AND TransactionId IS NULL"))
            .ShouldBe(1);
        var parameters = new HistoryQueryParameters { Since = beforeDelete, Count = 1, Total = TotalMode.Accurate };

        var incremental = await ReadHistory(scope, parameters).ToListAsync();

        incremental.Count.ShouldBe(1);
        incremental.Single().IsDeleted.ShouldBeTrue();
        incremental.Single().Request!.Method.ShouldBe("DELETE");
        incremental.Single().LastModified.ShouldBeGreaterThanOrEqualTo(beforeDelete);
        (await CountAsync(scope, parameters)).ShouldBe(1);
        using JsonDocument bundle = await SerializeAsync(await HandleAsync(scope, parameters), 1);
        bundle.RootElement.GetProperty("entry")[0].GetProperty("response").GetProperty("status").GetString().ShouldBe("204");

        var before = await ReadHistory(scope, new HistoryQueryParameters { Until = beforeDelete }).ToListAsync();
        before.Select(entry => entry.VersionId).ShouldBe(["1"]);
        var descending = await ReadHistory(scope, new HistoryQueryParameters { Count = 1 }).ToListAsync();
        descending[0].VersionId.ShouldBe("2");
        descending.Last().IsPagingProbe.ShouldBeTrue();
        var next = await ReadHistory(scope, new HistoryQueryParameters { Count = 1, Offset = 1 }).ToListAsync();
        next.Select(entry => entry.VersionId).ShouldBe(["1"]);
        var ascending = await ReadHistory(scope, new HistoryQueryParameters { Sort = HistorySortOrder.Ascending }).ToListAsync();
        ascending.Select(entry => entry.VersionId).ShouldBe(["1", "2"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenSurrogateUniquifiers_WhenFilteringExactInstants_ThenBoundsMatchLastModifiedWithoutRounding(int scope)
    {
        await SeedHistoryAsync(4, includeOtherResources: false);
        // Two entries share the same decoded millisecond, including the highest possible low bits.
        await _database.ExecuteNonQueryAsync(
            $"""
             UPDATE dbo.Resource SET ResourceSurrogateId = CASE Version
               WHEN 1 THEN {Epoch.ToId()}
               WHEN 2 THEN {Epoch.ToId() + 79999}
               WHEN 3 THEN {Epoch.AddMilliseconds(1).ToId() + 7}
               ELSE {Epoch.AddMilliseconds(2).ToId()} END
             WHERE ResourceId = '{ResourceId}';
             """);

        await AssertWindowAsync(scope, Epoch, Epoch, ["2", "1"]);
        await AssertWindowAsync(scope, Epoch.AddTicks(1), Epoch.AddMilliseconds(1).AddTicks(-1), []);
        await AssertWindowAsync(scope, Epoch.AddTicks(1), Epoch.AddMilliseconds(1), ["3"]);
        await AssertWindowAsync(scope, null, Epoch.AddTicks(-1), []);
        await AssertWindowAsync(scope, Epoch.ToOffset(TimeSpan.FromHours(5)), Epoch.ToOffset(TimeSpan.FromHours(-7)), ["2", "1"]);
        await AssertWindowAsync(scope, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, ["4", "3", "2", "1"]);
        await AssertWindowAsync(scope, DateTimeOffset.MaxValue, null, []);

        var window = new HistoryQueryParameters { Since = Epoch, Until = Epoch, Count = 1, Sort = HistorySortOrder.Ascending };
        var first = await ReadHistory(scope, window).ToListAsync();
        first[0].VersionId.ShouldBe("1");
        first.Last().IsPagingProbe.ShouldBeTrue();
        var next = await ReadHistory(scope, window with { Offset = 1 }).ToListAsync();
        next.Select(entry => entry.VersionId).ShouldBe(["2"]);
        next.Single().LastModified.ShouldBe(Epoch);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task GivenCorruptPageBody_WhenStreamingHistory_ThenFailsVisiblyInsteadOfClaimingCompleteness(int scope, bool emptyBody)
    {
        await SeedHistoryAsync(3, includeOtherResources: false);
        string body = emptyBody ? "0x1F8B080000000000000A03000000000000000000" : "0xDEADBEEF";
        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.Resource SET RawResource = {body} WHERE ResourceId = '{ResourceId}' AND Version = 2");
        var parameters = new HistoryQueryParameters { Count = 10 };
        var result = await HandleAsync(scope, parameters);
        using var stream = new MemoryStream();

        await Should.ThrowAsync<InvalidDataException>(() => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", result.TotalCount, result.Entries, result.Links, pageSize: 10));

        using JsonDocument bundle = JsonDocument.Parse(stream.ToArray());
        bundle.RootElement.TryGetProperty("link", out _).ShouldBeFalse();
        var entries = bundle.RootElement.GetProperty("entry");
        entries.GetArrayLength().ShouldBe(2);
        entries[1].GetProperty("response").GetProperty("status").GetString().ShouldBe("500");
        entries[1].GetProperty("response").GetProperty("outcome")
            .GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
    }

    private async Task AssertWindowAsync(int scope, DateTimeOffset? since, DateTimeOffset? until, string[] versions)
    {
        var parameters = new HistoryQueryParameters { Since = since, Until = until, Count = 10, Offset = 0 };
        var entries = await ReadHistory(scope, parameters).ToListAsync();
        entries.Select(entry => entry.VersionId).ShouldBe(versions);
        (await CountAsync(scope, parameters with { Count = 1, Offset = 5000 })).ShouldBe(versions.Length);
    }

    private Task<int> CountAsync(int scope, HistoryQueryParameters parameters) => scope switch
    {
        0 => HistoryCountHelper.CountResourceHistoryAsync(_database.Repository, new ResourceKey("Patient", ResourceId), parameters),
        1 => HistoryCountHelper.CountTypeHistoryAsync(_database.Repository, "Patient", _database.TenantId, parameters),
        _ => HistoryCountHelper.CountSystemHistoryAsync(_database.Repository, _database.TenantId, parameters),
    };

    private IAsyncEnumerable<SearchEntryResult> ReadHistory(int scope, HistoryQueryParameters parameters) => scope switch
    {
        0 => _database.Repository.GetResourceHistoryAsync(new ResourceKey("Patient", ResourceId), parameters),
        1 => _database.Repository.GetTypeHistoryAsync("Patient", _database.TenantId, parameters),
        _ => _database.Repository.GetSystemHistoryAsync(_database.TenantId, parameters),
    };

    private Task<HistoryResult> HandleAsync(int scope, HistoryQueryParameters parameters)
    {
        var factory = new RepositoryFactory(_database.Repository);
        return scope switch
        {
            0 => new GetResourceHistoryHandler(factory, NullLogger<GetResourceHistoryHandler>.Instance).HandleAsync(
                new GetResourceHistoryQuery("Patient", ResourceId, _database.TenantId, parameters, "https://history.example", $"/Patient/{ResourceId}/_history")),
            1 => new GetTypeHistoryHandler(factory, NullLogger<GetTypeHistoryHandler>.Instance).HandleAsync(
                new GetTypeHistoryQuery("Patient", _database.TenantId, parameters, "https://history.example", "/Patient/_history")),
            _ => new GetSystemHistoryHandler(factory, NullLogger<GetSystemHistoryHandler>.Instance).HandleAsync(
                new GetSystemHistoryQuery(_database.TenantId, parameters, "https://history.example", "/_history")),
        };
    }

    private static async Task<JsonDocument> SerializeAsync(HistoryResult result, int pageSize)
    {
        using var stream = new MemoryStream();
        await StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", result.TotalCount, result.Entries, result.Links, pageSize: pageSize);
        return JsonDocument.Parse(stream.ToArray());
    }

    private async Task PutAsync()
    {
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"));
        await _database.Repository.CreateOrUpdateAsync(resource);
    }

    private async Task SeedHistoryAsync(int count, bool includeOtherResources = true)
    {
        // Real resource rows, valid gzip bodies, current/history versions, and one tombstone. Set-based
        // seeding keeps the >2000-version test focused on reads rather than thousands of write calls.
        await _database.ExecuteNonQueryAsync(
            $$"""
             DECLARE @TypeId smallint = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient');
             WITH Versions AS (
               SELECT TOP ({{count}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Version
               FROM sys.all_objects a CROSS JOIN sys.all_objects b
             )
             INSERT dbo.Resource
               (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet)
             SELECT @TypeId, '{{ResourceId}}', Version, IIF(Version = {{count}}, 0, 1),
               {{Epoch.ToId()}} + Version * 80000, IIF(Version = {{count}}, 1, 0),
               IIF(Version = {{count}}, 'DELETE', 'PUT'),
               COMPRESS(CONVERT(varchar(max), '{"resourceType":"Patient","id":"{{ResourceId}}"}')), 1
             FROM Versions;
             """);
        if (includeOtherResources)
        {
            await _database.ExecuteNonQueryAsync(
                $$"""
                 INSERT dbo.ResourceType (Name) VALUES ('Observation');
                 INSERT dbo.Resource
                   (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet)
                 SELECT rt.ResourceTypeId, 'other-history', v.Version, IIF(v.Version = 1 AND rt.Name = 'Patient', 1, 0),
                   {{Epoch.AddMinutes(1).ToId()}} + v.Version, 0, 'PUT',
                   COMPRESS(CONVERT(varchar(max), '{"resourceType":"' + rt.Name + '","id":"other-history"}')), 1
                 FROM dbo.ResourceType rt CROSS JOIN (VALUES (1), (2)) v(Version)
                 WHERE rt.Name = 'Patient' OR (rt.Name = 'Observation' AND v.Version = 1);
                 """);
        }
    }

    private sealed class RepositoryFactory(IFhirRepository repository) : IFhirRepositoryFactory
    {
        public Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(repository);
    }
}
