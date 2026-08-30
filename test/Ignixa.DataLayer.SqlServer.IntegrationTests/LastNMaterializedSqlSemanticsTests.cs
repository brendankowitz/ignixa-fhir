using System.Data;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class LastNMaterializedSqlSemanticsTests
{
    private const short ObservationResourceTypeId = 104;
    private const short CodeSearchParamId = 210;
    private const short EffectiveSearchParamId = 211;
    private const short FilterSearchParamId = 212;
    private const short AuthorizationSearchParamId = 213;
    private const string CodeSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-code";
    private const string EffectiveSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";
    private const string AuthorizationSearchParameterUrl = "http://example.org/fhir/SearchParameter/Observation-authorization";

    private static readonly SearchParameterInfo CodeSearchParameter = new(
        "code",
        "code",
        SearchParamType.Token,
        new Uri(CodeSearchParameterUrl));

    private static readonly SearchParameterInfo EffectiveSearchParameter = new(
        "date",
        "date",
        SearchParamType.Date,
        new Uri(EffectiveSearchParameterUrl));

    private static readonly SearchParameterInfo AuthorizationSearchParameter = new(
        "authorization",
        "authorization",
        SearchParamType.Token,
        new Uri(AuthorizationSearchParameterUrl));

    [SkippableFact]
    public async Task GivenAnOrdinaryFilter_WhenLastNExecutes_ThenFilteringOccursBeforeGrouping()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(1), ["a"], FilterCode: "included"),
                new(2, Effective(2), ["a"], FilterCode: "excluded"),
            ]);

        IReadOnlyList<long> result = await ExecuteAsync(
            database,
            CreateFilteredPlan(FilterSearchParamId, "included", maximum: 1));

        result.ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenAnAuthorizationConstraint_WhenLastNExecutes_ThenAuthorizationOccursInTheCandidateCte()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(1), ["a"], AuthorizationCode: "allowed"),
                new(2, Effective(2), ["a"], AuthorizationCode: "denied"),
            ]);

        SearchParameterPredicateExpression authorizationPredicate = new(
            AuthorizationSearchParameter,
            SearchComparator.Eq,
            modifier: null,
            new TokenSearchValue(system: null, code: "allowed", text: null));
        SearchOptions filters = new()
        {
            AccessConstraints = [new AccessConstraint("Observation", authorizationPredicate)],
        };
        LastNSearchOptions options = new(
            filters,
            1,
            CodeSearchParameter,
            EffectiveSearchParameter);
        SearchPlan plan = await new SearchSqlCompiler(new LastNSymbolResolver())
            .CreateLastNPlanAsync(options);

        IReadOnlyList<long> result = await ExecuteAsync(database, plan);

        result.ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenCodedAndTextGroups_WhenLastNExecutes_ThenEachGroupReturnsItsNewestObservation()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(1), ["a"]),
                new(2, Effective(2), ["a"]),
                new(3, Effective(1), [], Text: "text"),
                new(4, Effective(2), [], Text: "text"),
            ]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 1));

        result.ShouldBe([2, 4]);
    }

    [SkippableFact]
    public async Task GivenTiesBeforeAndAtTheBoundary_WhenLastNExecutes_ThenRankIncludesEveryBoundaryTie()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(4), ["a"]),
                new(2, Effective(4), ["a"]),
                new(3, Effective(3), ["a"]),
                new(4, Effective(3), ["a"]),
                new(5, Effective(2), ["a"]),
            ]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 3));

        result.Order().ShouldBe([1, 2, 3, 4]);
    }

    [SkippableFact]
    public async Task GivenMissingEffectiveValues_WhenLastNExecutes_ThenSurrogateOrderFillsOnlyOpenSlots()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(2), ["a"]),
                new(2, Effective(1), ["a"]),
                new(3, null, ["a"]),
                new(4, null, ["a"]),
                new(5, null, ["a"]),
            ]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 3));

        result.Order().ShouldBe([1, 2, 5]);
    }

    [SkippableFact]
    public async Task GivenNoCandidates_WhenLastNExecutes_ThenTheResultIsEmpty()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync([]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 1));

        result.ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenMultipleGroups_WhenLastNExecutes_ThenGroupAndResourceOrderingIsDeterministic()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(1), ["a"]),
                new(2, Effective(1), ["b"]),
                new(3, Effective(2), ["a"]),
                new(4, Effective(2), ["b"]),
                new(5, Effective(2), [], Text: "Zulu"),
                new(6, Effective(2), [], Text: "alpha"),
            ]);

        IReadOnlyList<long> first = await ExecuteAsync(database, CreatePlan(maximum: 2));
        IReadOnlyList<long> second = await ExecuteAsync(database, CreatePlan(maximum: 2));

        second.ShouldBe(first);
        first.ShouldBe([3, 1, 4, 2, 6, 5]);
    }

    [SkippableFact]
    public async Task GivenCurrentHistoricalAndDeletedRows_WhenLastNExecutes_ThenOnlyCurrentResourcesAreReturned()
    {
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, new(1, Effective(1), ["current"]));
        await SeedObservationAsync(database, new(2, Effective(2), ["history"], IsHistory: true));
        await SeedObservationAsync(database, new(3, Effective(3), ["deleted"], IsDeleted: true));
        await MaterializeAndMarkReadyAsync(database, [1, 2, 3]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 1));

        result.ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenCaseDistinctOverflowValues_WhenLastNExecutes_ThenTheyRemainDistinctGroups()
    {
        string prefix = new('x', 256);
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [
                new(1, Effective(1), [prefix], CodeOverflow: "Overflow"),
                new(2, Effective(2), [prefix], CodeOverflow: "overflow"),
            ]);

        IReadOnlyList<long> first = await ExecuteAsync(database, CreatePlan(maximum: 1));
        IReadOnlyList<long> second = await ExecuteAsync(database, CreatePlan(maximum: 1));

        second.ShouldBe(first);
        first.Order().ShouldBe([1, 2]);
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData("Pending")]
    [InlineData("Building")]
    [InlineData("Failed")]
    public async Task GivenAnAbsentOrNonReadyGeneration_WhenLastNExecutes_ThenError50403IsReturned(string? state)
    {
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        if (state is not null)
        {
            await ConfigureGenerationAsync(database, state);
        }

        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => ExecuteAsync(database, CreatePlan(maximum: 1)));

        exception.Number.ShouldBe(50403);
    }

    [SkippableFact]
    public async Task GivenAReadyGeneration_WhenLastNExecutes_ThenTheMaterializedResultIsReturned()
    {
        await using LastNTestDatabase database = await CreateReadyDatabaseAsync(
            [new(1, Effective(1), ["ready"])]);

        IReadOnlyList<long> result = await ExecuteAsync(database, CreatePlan(maximum: 1));

        result.ShouldBe([1]);
    }

    private static DateTime Effective(int day)
        => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<LastNTestDatabase> CreateReadyDatabaseAsync(IReadOnlyList<Observation> observations)
    {
        LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        try
        {
            foreach (Observation observation in observations)
            {
                await SeedObservationAsync(database, observation);
            }

            await MaterializeAndMarkReadyAsync(database, observations.Select(observation => observation.SurrogateId).ToArray());
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task SeedObservationAsync(LastNTestDatabase database, Observation observation)
    {
        await database.SeedResourceAsync(
            ObservationResourceTypeId,
            observation.SurrogateId,
            $"observation-{observation.SurrogateId}",
            1,
            observation.IsHistory,
            observation.IsDeleted,
            CancellationToken.None);

        foreach (string code in observation.Codes)
        {
            await database.SeedTokenSearchParamAsync(
                ObservationResourceTypeId,
                observation.SurrogateId,
                CodeSearchParamId,
                7,
                code,
                observation.CodeOverflow,
                CancellationToken.None);
        }

        if (observation.Text is not null)
        {
            await database.SeedTokenTextAsync(
                ObservationResourceTypeId,
                observation.SurrogateId,
                CodeSearchParamId,
                observation.Text,
                observation.IsHistory,
                CancellationToken.None);
        }

        if (observation.FilterCode is not null)
        {
            await database.SeedTokenSearchParamAsync(
                ObservationResourceTypeId,
                observation.SurrogateId,
                FilterSearchParamId,
                null,
                observation.FilterCode,
                null,
                CancellationToken.None);
        }

        if (observation.AuthorizationCode is not null)
        {
            await database.SeedTokenSearchParamAsync(
                ObservationResourceTypeId,
                observation.SurrogateId,
                AuthorizationSearchParamId,
                null,
                observation.AuthorizationCode,
                null,
                CancellationToken.None);
        }

        if (observation.Effective is DateTime effective)
        {
            await database.SeedDateTimeSearchParamAsync(
                ObservationResourceTypeId,
                observation.SurrogateId,
                EffectiveSearchParamId,
                effective,
                effective,
                isLongerThanADay: false,
                isMin: true,
                isMax: true,
                CancellationToken.None);
        }
    }

    private static async Task MaterializeAndMarkReadyAsync(
        LastNTestDatabase database,
        IReadOnlyList<long> resourceSurrogateIds)
    {
        if (resourceSurrogateIds.Count > 0)
        {
            using DataTable resources = new();
            resources.Columns.Add("ResourceTypeId", typeof(short));
            resources.Columns.Add("SearchParamId", typeof(short));
            resources.Columns.Add("ResourceSurrogateId", typeof(long));
            foreach (long resourceSurrogateId in resourceSurrogateIds)
            {
                resources.Rows.Add(ObservationResourceTypeId, CodeSearchParamId, resourceSurrogateId);
            }

            await using SqlCommand maintain = database.Connection.CreateCommand();
            maintain.CommandText = "dbo.MaintainLastNCodeGroups";
            maintain.CommandType = CommandType.StoredProcedure;
            maintain.Parameters.Add("@Mode", SqlDbType.VarChar, 8).Value = "Add";
            maintain.Parameters.Add(new SqlParameter("@Resources", SqlDbType.Structured)
            {
                TypeName = "dbo.LastNResourceScopeList",
                Value = resources,
            });
            await maintain.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await ConfigureGenerationAsync(database, "Ready");
    }

    private static async Task ConfigureGenerationAsync(LastNTestDatabase database, string state)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.LastNCodeGroupGeneration
                (ResourceTypeId, SearchParamId, Generation, State, StartedDateTime, CompletedDateTime)
            VALUES
                (@resourceTypeId, @searchParamId, 1, @state, SYSUTCDATETIME(),
                 CASE WHEN @state = 'Ready' THEN SYSUTCDATETIME() END);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
        command.Parameters.Add("@state", SqlDbType.VarChar, 16).Value = state;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static SearchPlan CreatePlan(int maximum)
        => new()
        {
            Query = new QueryPlan(
                [new CteDefinition.ResourceSource(ObservationResourceTypeId)],
                new MatchPageSpec(
                    new CteRef(0),
                    Shape: new ResultShape.LastN(
                        new LastNSpec(
                            ObservationResourceTypeId,
                            CodeSearchParamId,
                            EffectiveSearchParamId,
                            maximum)))),
        };

    private static SearchPlan CreateFilteredPlan(short searchParamId, string code, int maximum)
    {
        TableDescriptor tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        Predicate predicate = new Predicate.Equal(
            new SqlColumnRef(tokenTable.TableName, "Code"),
            new SqlParameterRef(code),
            "Latin1_General_100_CS_AS");
        return new SearchPlan
        {
            Query = new QueryPlan(
                [new CteDefinition.ParamSource(tokenTable, ObservationResourceTypeId, searchParamId, predicate)],
                new MatchPageSpec(
                    new CteRef(0),
                    Shape: new ResultShape.LastN(
                        new LastNSpec(
                            ObservationResourceTypeId,
                            CodeSearchParamId,
                            EffectiveSearchParamId,
                            maximum)))),
        };
    }

    private static async Task<IReadOnlyList<long>> ExecuteAsync(LastNTestDatabase database, SearchPlan plan)
    {
        CompiledSearch compiled = plan.Compile();
        await using SqlConnection connection = await database.OpenConnectionAsync(CancellationToken.None);
#pragma warning disable CA2100
        await using SqlCommand command = new(compiled.Sql, connection)
        {
            CommandTimeout = 30,
        };
#pragma warning restore CA2100
        foreach (EmittedSqlParameter emittedParameter in compiled.Parameters)
        {
            command.Parameters.AddWithValue(emittedParameter.Name, emittedParameter.Value);
        }

        List<long> results = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            results.Add(reader.GetInt64(1));
        }

        return results;
    }

    private sealed record Observation(
        long SurrogateId,
        DateTime? Effective,
        IReadOnlyList<string> Codes,
        string? Text = null,
        string? CodeOverflow = null,
        string? FilterCode = null,
        string? AuthorizationCode = null,
        bool IsHistory = false,
        bool IsDeleted = false);

}
