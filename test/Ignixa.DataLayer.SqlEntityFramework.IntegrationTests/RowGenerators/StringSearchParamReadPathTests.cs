// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.RowGenerators;

/// <summary>
/// Proves the read path (<see cref="SearchParameterQueryGenerator"/>) can find a StringSearchParam row
/// whose &gt;256-char value only matches past character 256, once seeded under the corrected write
/// convention (task 1: TextOverflow holds the whole value, not the remainder). Seeds the row directly
/// via EF (rather than through the full merge pipeline) since the row generator's write-path convention
/// is already covered by <see cref="StringSearchParameterRowGeneratorTests"/>; this test only proves the
/// read path's Text+TextOverflow -> TextOverflow fix works against a real SQL Server, matching the
/// live-DB convention <c>SqlEntityFrameworkSymbolResolverTests</c> established.
/// </summary>
public class StringSearchParamReadPathTests
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
    public async Task GivenAStringLongerThan256CharsSeededUnderTheCorrectedConvention_WhenSearchingPastChar256_ThenTheResourceIsFound()
    {
        // Arrange: initialize schema, then seed a real dbo.SearchParam row for "name" plus one
        // StringSearchParam row whose value is 300 chars long, written under task 1's corrected
        // convention: Text holds the first 256 chars, TextOverflow holds the WHOLE 300-char value.
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
            new Uri("http://ignixa.dev/fhir/task1/SearchParameter/patient-name"));

        // CA2000 suppressed: SearchIndexReferenceDataCache.Dispose() disposes the FhirDbContext it
        // was constructed with, which this test still owns and needs afterward (via `context` above)
        // -- same justification SqlEntityFrameworkSymbolResolverTests.cs uses for the identical pattern.
#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase([parameter.Url!.ToString()], null!);

        var seededSearchParamId = await context.SearchParams
            .AsNoTracking()
            .Where(sp => sp.Uri == parameter.Url.ToString())
            .Select(sp => sp.SearchParamId)
            .SingleAsync();

        var resourceType = await context.ResourceTypes.AsNoTracking().FirstOrDefaultAsync(rt => rt.Name == "Patient")
            ?? await CreateResourceTypeAsync(context, "Patient");

        var longValue = new string('A', 256) + "needle-past-char-256";
        var resourceSurrogateId = DateTimeOffset.UtcNow.Ticks;
        context.StringSearchParams.Add(new StringSearchParamEntity
        {
            ResourceTypeId = resourceType.ResourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = seededSearchParamId,
            Text = longValue[..256],
            TextOverflow = longValue,
            IsMin = false,
            IsMax = false,
        });
        await context.SaveChangesAsync();

        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            context, cache, NullLogger<CompositeSearchParameterQueryGenerator>.Instance);
        var generator = new SearchParameterQueryGenerator(
            context, cache, NullLogger<SearchParameterQueryGenerator>.Instance, compositeGenerator);

        // Act: search for a substring that only exists past character 256 -- only findable if the
        // read path correctly consults TextOverflow (the whole value) rather than Text + TextOverflow.
        var expression = new SearchParameterExpression(
            parameter,
            new StringExpression(StringOperator.Contains, FieldName.String, componentIndex: null, "needle-past-char-256", ignoreCase: true));
        var query = await generator.GenerateQueryAsync(resourceType.ResourceTypeId, expression, CancellationToken.None);
        var matches = await query.ToListAsync();

        // Assert
        matches.ShouldContain(resourceSurrogateId);
    }

    private static async Task<ResourceTypeEntity> CreateResourceTypeAsync(FhirDbContext context, string name)
    {
        var entity = new ResourceTypeEntity { Name = name };
        context.ResourceTypes.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }
}
