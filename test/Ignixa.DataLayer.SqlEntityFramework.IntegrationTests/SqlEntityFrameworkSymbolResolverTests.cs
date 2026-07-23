// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Proves <see cref="SqlEntityFrameworkSymbolResolver"/> works end to end against a real, live SQL
/// Server: seeds one real <c>dbo.SearchParam</c> row via the existing
/// <see cref="SearchIndexReferenceDataCache.SyncSearchParametersToDatabase"/> mechanism (the same
/// one <c>CompartmentDataSeeder.cs</c> and production's search-parameter sync path already use --
/// no hand-rolled catalog-seeding SQL), then resolves it through the real
/// <see cref="Resolve.RunAsync"/> pipeline (Phase 3 Task 4) and this real resolver, asserting the
/// returned <c>SearchParamId</c> matches the row that was actually seeded.
/// <para>
/// THIS IS A MANUAL HARNESS, NOT CI COVERAGE. Every test that touches the database is
/// <c>[Fact(Skip = ...)]</c> and runs only when a developer sets <c>TEST_SQL_CONNECTION_STRING</c> and
/// removes the skip. A green CI run says nothing about this file, so do not count it when judging
/// whether <see cref="SqlEntityFrameworkSymbolResolver"/> is covered.
/// </para>
/// </summary>
public class SqlEntityFrameworkSymbolResolverTests
{
    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
        }

        return connectionString;
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenResolvingAKnownParameter_ThenReturnsItsRealSearchParamId()
    {
        // Arrange: initialize schema via SchemaDeployer, the same DacFx-based mechanism production
        // uses (see SqlEntityFrameworkRepositoryFactory.cs), then seed one real search parameter row.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new FhirDbContext(options);
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);

        var parameter = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://ignixa.dev/fhir/task5/SearchParameter/patient-name"));

        // CA2000 suppressed: SearchIndexReferenceDataCache.Dispose() disposes the FhirDbContext it
        // was constructed with, which this test still owns and needs afterward (via `context` above)
        // -- same justification CompartmentDataSeeder.cs uses for the identical pattern.
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase([parameter.Url!.ToString()], null!);

        var seededSearchParamId = await context.SearchParams
            .AsNoTracking()
            .Where(sp => sp.Uri == parameter.Url.ToString())
            .Select(sp => sp.SearchParamId)
            .SingleAsync();

        var resolver = new SqlEntityFrameworkSymbolResolver(cache);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));

        // Act
        var symbolTable = (await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

        // Assert
        symbolTable.SearchParamId(parameter).ShouldBe(seededSearchParamId);
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenGetSystemIdAsyncCalledForKnownSystem_ThenReturnsSeedRowId()
    {
        // Arrange: seed via a dedicated context+cache pair (Phase 1) so the row persists to the
        // database, then resolve through a completely fresh context+cache (Phase 2, cold) to prove
        // the EF database-query path fires rather than a warm in-memory cache hit.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        const string system = "http://loinc.org/resolver-test";

        // Phase 1: initialize schema and persist the System row through a seed context.
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);
        int seededId;
        await using (var seedContext = new FhirDbContext(options))
        {
            // CA2000: seedCache.Dispose() disposes seedContext; seedContext is also disposed
            // by the enclosing await-using block -- double-dispose is benign here.
#pragma warning disable CA2000
            var seedCache = new SearchIndexReferenceDataCache(seedContext, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
            var id = await seedCache.GetOrCreateSystemIdAsync(system);
            id.ShouldNotBeNull();
            seededId = id!.Value;
        }

        // Phase 2: open a fresh context and build a cold cache (no entries in _systemCache).
        // GetSystemIdAsync must issue a real EF query to find the persisted row.
        await using var resolveContext = new FhirDbContext(options);
#pragma warning disable CA2000
        var resolveCache = new SearchIndexReferenceDataCache(resolveContext, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        var resolver = new SqlEntityFrameworkSymbolResolver(resolveCache);

        // Act
        var result = await resolver.GetSystemIdAsync(system, CancellationToken.None);

        // Assert
        result.ShouldBe(seededId);
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenGetSystemIdAsyncCalledForUnknownSystem_ThenReturnsNull()
    {
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);
        await using var context = new FhirDbContext(options);

#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000

        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        // Act
        var result = await resolver.GetSystemIdAsync("http://does-not-exist.example/system", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenGetQuantityCodeIdAsyncCalledForKnownCode_ThenReturnsSeedRowId()
    {
        // Arrange: seed via a dedicated context+cache pair (Phase 1) so the row persists to the
        // database, then resolve through a completely fresh context+cache (Phase 2, cold) to prove
        // the EF database-query path fires rather than a warm in-memory cache hit.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        const string code = "mg-resolver-test";

        // Phase 1: initialize schema and persist the QuantityCode row through a seed context.
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);
        int seededId;
        await using (var seedContext = new FhirDbContext(options))
        {
#pragma warning disable CA2000
            var seedCache = new SearchIndexReferenceDataCache(seedContext, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
            var id = await seedCache.GetOrCreateQuantityCodeIdAsync(code);
            id.ShouldNotBeNull();
            seededId = id!.Value;
        }

        // Phase 2: open a fresh context and build a cold cache (no entries in _quantityCodeCache).
        // GetQuantityCodeIdAsync must issue a real EF query to find the persisted row.
        await using var resolveContext = new FhirDbContext(options);
#pragma warning disable CA2000
        var resolveCache = new SearchIndexReferenceDataCache(resolveContext, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        var resolver = new SqlEntityFrameworkSymbolResolver(resolveCache);

        // Act
        var result = await resolver.GetQuantityCodeIdAsync(code, CancellationToken.None);

        // Assert
        result.ShouldBe(seededId);
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenGetQuantityCodeIdAsyncCalledForUnknownCode_ThenReturnsNull()
    {
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);
        await using var context = new FhirDbContext(options);

#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000

        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        // Act
        var result = await resolver.GetQuantityCodeIdAsync("does-not-exist-unit", CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetSystemIdAsyncCalled_ThenThrowsOperationCanceledException()
    {
        // Arrange: no database access happens on this path, so any valid DbContextOptions works here --
        // the connection is never opened. What stops it on a cold cache is the semaphore wait, not a
        // cancellation check; the check that short-circuits a *warm* cache hit is pinned separately by
        // SearchIndexReferenceDataCacheRegressionTests, which this test cannot distinguish.
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer("Server=.;Database=FakeCancelCheck;Trusted_Connection=True;")
            .Options;
        await using var context = new FhirDbContext(options);
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => resolver.GetSystemIdAsync("http://loinc.org", cts.Token));
    }

    [Fact]
    public async Task GivenACancelledToken_WhenGetQuantityCodeIdAsyncCalled_ThenThrowsOperationCanceledException()
    {
        // Arrange: as above -- no database access on this path, and the semaphore wait rather than a
        // cancellation check is what this particular test observes.
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer("Server=.;Database=FakeCancelCheck;Trusted_Connection=True;")
            .Options;
        await using var context = new FhirDbContext(options);
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => resolver.GetQuantityCodeIdAsync("mg", cts.Token));
    }
}
