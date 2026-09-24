using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Ignixa.Search.Sql.Symbols;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Search;

/// <summary>
/// <see cref="SqlServerSymbolResolver"/> stopped overriding <see cref="ISymbolResolver.GetSystemIdsAsync"/>
/// when it was ported from Ignixa.DataLayer.SqlEntityFramework -- the EF resolver overrides it, this one
/// didn't, so <c>Resolve.cs</c>'s search-compilation path silently fell back onto the interface's default
/// implementation: one <see cref="ISymbolResolver.GetSystemIdAsync"/> call (and, on a cold cache, one fresh
/// <c>SqlConnection</c>) per distinct system instead of one round trip for the whole set. This test drives
/// the lookup through the interface type, the way <c>Resolve.cs</c> actually calls it, so a regression back
/// to the default sequential implementation fails here.
/// </summary>
public class SqlServerSymbolResolverBatchTests
{
    [Fact]
    public async Task GivenNColdSystems_WhenGetSystemIdsAsyncThroughTheInterface_ThenOneRoundTripResolvesAll()
    {
        // Arrange
        var sql = new FixedRowsSqlExecutionService<(string Value, int Id)>(
            ("http://loinc.org", 1),
            ("http://snomed.info/sct", 2),
            ("http://unitsofmeasure.org", 3));
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        // Act: through the interface, exactly as Resolve.cs's search-compilation path calls it -- if
        // SqlServerSymbolResolver stops overriding GetSystemIdsAsync, this silently falls back to the
        // interface's sequential default and the round-trip assertion below fails.
        ISymbolResolver resolver = new SqlServerSymbolResolver(cache);
        var results = await resolver.GetSystemIdsAsync(
            ["http://loinc.org", "http://snomed.info/sct", "http://unitsofmeasure.org"], CancellationToken.None);

        // Assert
        sql.CallCount.ShouldBe(1);
        results.Count.ShouldBe(3);
        results["http://loinc.org"].ShouldBe(1);
        results["http://snomed.info/sct"].ShouldBe(2);
        results["http://unitsofmeasure.org"].ShouldBe(3);
    }
}
