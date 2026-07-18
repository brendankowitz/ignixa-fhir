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
        // Arrange: initialize schema via the same DatabaseInitializer production uses (see
        // DatabaseSchemaInitializationTests.cs), then seed one real search parameter row.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new FhirDbContext(options);
        var initializer = new DatabaseInitializer(context, NullLogger<DatabaseInitializer>.Instance, "Development");
        await initializer.InitializeAsync();

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
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(parameter).ShouldBe(seededSearchParamId);
    }
}
