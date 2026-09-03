using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// <see cref="SqlServerSearchIndexReferenceDataCache.GetSystemIdsAsync"/> is the batched sibling of
/// <see cref="SqlServerSearchIndexReferenceDataCache.TryGetSystemIdAsync"/> that the EF-to-SqlServer port
/// dropped: without it, a search naming N distinct token systems cost N round trips (one
/// <c>SqlConnection</c> open per cache miss) instead of one. These tests pin the round-trip count directly
/// -- by counting <see cref="FixedRowsSqlExecutionService{TRow}.CallCount"/>, never by timing -- and the
/// "every requested system appears" contract the interface promises.
/// </summary>
public class SqlServerSearchIndexReferenceDataCacheBatchSystemLookupTests
{
    [Fact]
    public async Task GivenNColdSystems_WhenGetSystemIdsAsync_ThenOneRoundTripResolvesAll()
    {
        // Arrange: three distinct systems, none cached yet, all present in the fake row set the fixture
        // hands back on its single call.
        var sql = new FixedRowsSqlExecutionService<(string Value, int Id)>(
            ("http://loinc.org", 1),
            ("http://snomed.info/sct", 2),
            ("http://unitsofmeasure.org", 3));
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        // Act
        var results = await cache.GetSystemIdsAsync(
            ["http://loinc.org", "http://snomed.info/sct", "http://unitsofmeasure.org"], CancellationToken.None);

        // Assert: one round trip for three systems, not three.
        sql.CallCount.ShouldBe(1);
        results.Count.ShouldBe(3);
        results["http://loinc.org"].ShouldBe(1);
        results["http://snomed.info/sct"].ShouldBe(2);
        results["http://unitsofmeasure.org"].ShouldBe(3);
    }

    [Fact]
    public async Task GivenASystemWithNoRow_WhenGetSystemIdsAsync_ThenItAppearsInTheResultMappedToNull()
    {
        // Arrange: the fixture's fixed row set answers for "http://loinc.org" only -- "http://unknown.example"
        // has no matching row, matching a real "WHERE Value IN (...)" query that simply omits it.
        var sql = new FixedRowsSqlExecutionService<(string Value, int Id)>(("http://loinc.org", 1));
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        // Act
        var results = await cache.GetSystemIdsAsync(
            ["http://loinc.org", "http://unknown.example"], CancellationToken.None);

        // Assert: every requested system appears, the miss mapped to null rather than omitted.
        sql.CallCount.ShouldBe(1);
        results.Count.ShouldBe(2);
        results["http://loinc.org"].ShouldBe(1);
        results["http://unknown.example"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenEverySystemAlreadyWarmInTheCache_WhenGetSystemIdsAsync_ThenNoRoundTripIsIssued()
    {
        // Arrange: warm the positive cache via the single-lookup path first.
        var sql = new FixedRowsSqlExecutionService<int>(42);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.TryGetSystemIdAsync("http://loinc.org", CancellationToken.None);
        sql.CallCount.ShouldBe(1);

        // Act: the batch call for the same, now-warm system must not issue a second round trip.
        var results = await cache.GetSystemIdsAsync(["http://loinc.org"], CancellationToken.None);

        // Assert
        sql.CallCount.ShouldBe(1);
        results["http://loinc.org"].ShouldBe(42);
    }

    [Fact]
    public async Task GivenAnEmptyRequest_WhenGetSystemIdsAsync_ThenAnEmptyMapIsReturnedWithoutTouchingTheDatabase()
    {
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            new UnusableSqlExecutionService(), tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        var results = await cache.GetSystemIdsAsync([], CancellationToken.None);

        results.ShouldBeEmpty();
    }
}
