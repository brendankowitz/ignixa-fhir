// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Microsoft.Data.SqlClient;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

public class LastNSqlSemanticsTests
{
    private const short ObservationResourceTypeId = 104;
    private const short CodeSearchParamId = 210;
    private const short EffectiveSearchParamId = 211;
    private static long _surrogateSeed = 8_000_000_000_000_000;

    [SkippableFact]
    public async Task GivenTransitiveCodingBridges_WhenLastNExecutes_ThenAllConnectedCodesShareOneGroup()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a"]),
                new(2, Effective(2), ["a", "b"]),
                new(3, Effective(3), ["b", "c"]),
            ]);

        result.ShouldBe([3]);
    }

    [SkippableFact]
    public async Task GivenADenseCyclicComponent_WhenLastNExecutes_ThenClosureTerminatesWithTheExactGroup()
    {
        string[] clique = Enumerable.Range(0, 12).Select(index => $"code-{index}").ToArray();

        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), clique),
                new(2, Effective(2), ["code-11"]),
            ]);

        result.ShouldBe([2]);
    }

    [SkippableFact]
    public async Task GivenOneObservationWithEquivalentCodings_WhenLastNExecutes_ThenTheResourceIsReturnedOnce()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 10,
            [
                new(1, Effective(1), ["a", "b"]),
                new(2, Effective(2), ["b"]),
            ]);

        result.Order().ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenTiesBeforeAndAtTheNthPosition_WhenLastNExecutes_ThenEveryBoundaryTieIsReturned()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 3,
            [
                new(1, Effective(4), ["a"]),
                new(2, Effective(4), ["a"]),
                new(3, Effective(3), ["a"]),
                new(4, Effective(3), ["a"]),
                new(5, Effective(2), ["a"]),
            ]);

        result.Order().ShouldBe([1, 2, 3, 4]);
    }

    [SkippableFact]
    public async Task GivenMissingEffectiveValuesAndOpenSlots_WhenLastNExecutes_ThenSurrogateOrderFillsOnlyTheOpenSlots()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 3,
            [
                new(1, Effective(2), ["a"]),
                new(2, Effective(1), ["a"]),
                new(3, null, ["a"]),
                new(4, null, ["a"]),
                new(5, null, ["a"]),
            ]);

        result.Order().ShouldBe([1, 2, 5]);
    }

    [SkippableFact]
    public async Task GivenTextOnlyCodes_WhenLastNExecutes_ThenGroupingIsTextBasedAndCaseSensitive()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), [], Text: "Alpha"),
                new(2, Effective(2), [], Text: "Alpha"),
                new(3, Effective(1), [], Text: "alpha"),
            ]);

        result.Order().ShouldBe([2, 3]);
    }

    [SkippableFact]
    public async Task GivenNoCandidates_WhenLastNExecutes_ThenTheResultIsEmpty()
    {
        IReadOnlyList<int> result = await ExecuteAsync(maximum: 1, []);

        result.ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenAHistoryOnlyTextObservation_WhenLastNExecutes_ThenResourceVisibilitySelectsTheHistoricalTokenText()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [new(1, Effective(1), [], Text: "history", IsHistory: true)],
            new ResourceVisibility(IsHistory: true, IsDeleted: false));

        result.ShouldBe([1]);
    }

    [SkippableFact]
    public async Task GivenLongCodesWithTheSamePrefixAndDifferentSuffixes_WhenLastNExecutes_ThenTheyRemainDistinctGroups()
    {
        string prefix = new('a', 256);

        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), [prefix + "-first"]),
                new(2, Effective(2), [prefix + "-second"]),
            ]);

        result.Order().ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenLongCodesWithDifferentPrefixesAndTheSameSuffix_WhenLastNExecutes_ThenTheyRemainDistinctGroups()
    {
        string suffix = "-shared-suffix";

        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), [new string('a', 256) + suffix]),
                new(2, Effective(2), [new string('b', 256) + suffix]),
            ]);

        result.Order().ShouldBe([1, 2]);
    }

    private static DateTime Effective(int day)
        => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private static string GetConnectionString()
    {
        string? connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new Xunit.SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
        }

        return connectionString;
    }

    private static async Task<IReadOnlyList<int>> ExecuteAsync(
        int maximum,
        IReadOnlyList<Observation> observations,
        ResourceVisibility? visibility = null)
    {
        string connectionString = GetConnectionString();
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        long surrogateBase = Interlocked.Add(ref _surrogateSeed, 100);

        foreach (Observation observation in observations)
        {
            await SeedAsync(connection, transaction, surrogateBase, observation);
        }

        var plan = new SearchPlan
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
                            maximum))),
                Visibility: visibility),
        };
        CompiledSearch compiled = plan.Compile();

#pragma warning disable CA2100
        await using var command = new SqlCommand(compiled.Sql, connection, transaction)
        {
            CommandTimeout = 15,
        };
#pragma warning restore CA2100
        foreach (var parameter in compiled.Parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var result = new List<int>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(checked((int)(reader.GetInt64(1) - surrogateBase)));
        }

        return result;
    }

    private static async Task SeedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long surrogateBase,
        Observation observation)
    {
        long surrogateId = surrogateBase + observation.Offset;
        await using (var resource = new SqlCommand(
            """
            INSERT INTO dbo.Resource (
                ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId,
                IsDeleted, RawResource)
            VALUES (
                @resourceTypeId, @resourceId, 1, @isHistory, @surrogateId, 0, 0x01);
            """,
            connection,
            transaction))
        {
            resource.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            resource.Parameters.AddWithValue("@resourceId", $"lastn-{Guid.NewGuid():N}");
            resource.Parameters.AddWithValue("@isHistory", observation.IsHistory);
            resource.Parameters.AddWithValue("@surrogateId", surrogateId);
            await resource.ExecuteNonQueryAsync();
        }

        foreach (string code in observation.Codes)
        {
            string codePrefix = code[..Math.Min(code.Length, 256)];
            string? codeOverflow = code.Length > 256 ? code[256..] : null;
            await using var token = new SqlCommand(
                """
                INSERT INTO dbo.TokenSearchParam (
                    ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
                VALUES (
                    @resourceTypeId, @surrogateId, @searchParamId, 1, @code, @codeOverflow);
                """,
                connection,
                transaction);
            token.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            token.Parameters.AddWithValue("@surrogateId", surrogateId);
            token.Parameters.AddWithValue("@searchParamId", CodeSearchParamId);
            token.Parameters.AddWithValue("@code", codePrefix);
            token.Parameters.AddWithValue("@codeOverflow", (object?)codeOverflow ?? DBNull.Value);
            await token.ExecuteNonQueryAsync();
        }

        if (observation.Text is not null)
        {
            await using var text = new SqlCommand(
                """
                INSERT INTO dbo.TokenText (
                    ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
                VALUES (
                    @resourceTypeId, @surrogateId, @searchParamId, @text, 0);
                """,
                connection,
                transaction);
            text.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            text.Parameters.AddWithValue("@surrogateId", surrogateId);
            text.Parameters.AddWithValue("@searchParamId", CodeSearchParamId);
            text.Parameters.AddWithValue("@text", observation.Text);
            await text.ExecuteNonQueryAsync();
        }

        if (observation.Effective is { } effective)
        {
            await using var date = new SqlCommand(
                """
                INSERT INTO dbo.DateTimeSearchParam (
                    ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime,
                    EndDateTime, IsLongerThanADay, IsMin, IsMax)
                VALUES (
                    @resourceTypeId, @surrogateId, @searchParamId, @effective,
                    @effective, 0, 1, 1);
                """,
                connection,
                transaction);
            date.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            date.Parameters.AddWithValue("@surrogateId", surrogateId);
            date.Parameters.AddWithValue("@searchParamId", EffectiveSearchParamId);
            date.Parameters.AddWithValue("@effective", effective);
            await date.ExecuteNonQueryAsync();
        }
    }

    private sealed record Observation(
        int Offset,
        DateTime? Effective,
        IReadOnlyList<string> Codes,
        string? Text = null,
        bool IsHistory = false);
}
