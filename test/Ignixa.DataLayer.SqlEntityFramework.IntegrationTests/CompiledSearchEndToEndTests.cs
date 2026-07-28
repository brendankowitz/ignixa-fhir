// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Proves Resolve -> Lower -> Emit compiles to SQL that actually returns the right resource when
/// executed against a live SQL Server -- not just that each stage's unit tests pass in isolation.
/// </summary>
public class CompiledSearchEndToEndTests
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
    public async Task GivenARealDatabase_WhenCompiledQueryIsExecuted_ThenReturnsTheSeededResource()
    {
        // Arrange -- seed one Patient row's StringSearchParam(name) via the same real seeding
        // mechanism Phase 3 task 5 used (SearchIndexReferenceDataCache + a real resource merge) --
        // find and reuse that project's established resource-seeding helper rather than hand-rolling
        // INSERTs, matching this project's existing integration-test convention.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>().UseSqlServer(connectionString).Options;
        await using var context = new FhirDbContext(options);
        var initializer = new DatabaseInitializer(context, NullLogger<DatabaseInitializer>.Instance, "Development");
        await initializer.InitializeAsync();

        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://ignixa.dev/fhir/task10/SearchParameter/patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase([parameter.Url!.ToString()], null!);
        var searchParamId = await context.SearchParams.AsNoTracking()
            .Where(sp => sp.Uri == parameter.Url.ToString()).Select(sp => sp.SearchParamId).SingleAsync();

        // Seed one StringSearchParam row directly -- proving the compiled query reads real rows,
        // not asserting on the row-generation path (that's task 1's job).
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax) VALUES (103, 999001, {searchParamId}, 'Smith', NULL, 0, 0)");

        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        // Act
        var symbolTable = (await Resolve.RunAsync(
            CompilationContext.Create(
                new SearchOptions { Expression = predicate },
                "Patient",
                new SearchPlanOptions(),
                DateTimeOffset.UtcNow),
            new SymbolResolution(resolver),
            CancellationToken.None)).Symbols;
        var plan = Lower.Run(predicate, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // CA2100 suppressed: emitted.Sql is SqlBuilder.Run's compiler-generated, fully parameterized T-SQL
        // text (no user value is ever inlined into it -- that is this whole test's point), not
        // ad hoc string concatenation of user input.
#pragma warning disable CA2100
        await using var command = new SqlCommand(emitted.Sql, (SqlConnection)context.Database.GetDbConnection());
#pragma warning restore CA2100
        await context.Database.OpenConnectionAsync();
        foreach (var p in emitted.Parameters) command.Parameters.AddWithValue(p.Name, p.Value);
        await using var reader = await command.ExecuteReaderAsync();

        // Assert
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt64(1).ShouldBe(999001L);
        (await reader.ReadAsync()).ShouldBeFalse();
    }
}
