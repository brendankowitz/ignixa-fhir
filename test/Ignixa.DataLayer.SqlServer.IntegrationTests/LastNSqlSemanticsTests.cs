// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class LastNSqlSemanticsTests(LastNCteTestDatabase database) : IClassFixture<LastNCteTestDatabase>
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
    public async Task GivenACycleAndADisconnectedCode_WhenLastNExecutes_ThenEachComponentHasOneResult()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a", "b"]),
                new(2, Effective(2), ["b", "c"]),
                new(3, Effective(3), ["c", "a"]),
                new(4, Effective(4), ["z"]),
            ]);

        result.Order().ShouldBe([3, 4]);
    }

    [SkippableFact]
    public async Task GivenATransitivePathWithDescendingSteps_WhenLastNExecutes_ThenItDoesNotSplitTheComponent()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a", "d"]),
                new(2, Effective(2), ["d", "b"]),
                new(3, Effective(3), ["b", "c"]),
            ]);

        result.ShouldBe([3]);
    }

    [SkippableFact]
    public async Task GivenMoreThanOneHundredTranslationHops_WhenLastNExecutes_ThenItDoesNotTruncateRecursion()
    {
        Observation[] observations = Enumerable.Range(1, 105)
            .Select(index => new Observation(
                index,
                Effective(1).AddDays(index),
                [$"code-{index:D3}", $"code-{index + 1:D3}"]))
            .ToArray();

        IReadOnlyList<int> result = await ExecuteAsync(maximum: 1, observations);

        result.ShouldBe([105]);
    }

    [SkippableFact]
    public async Task GivenABridgeOutsideTheCandidateFilter_WhenLastNExecutes_ThenItDoesNotMergeCandidateGroups()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a"]),
                new(2, Effective(2), ["b"]),
                new(3, Effective(3), ["a", "b"]),
            ],
            maximumOffset: 2);

        result.ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenHistoricalAndDeletedBridges_WhenLastNExecutes_ThenOnlyCurrentLiveCandidatesContribute()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a"]),
                new(2, Effective(2), ["b"]),
                new(3, Effective(3), ["a", "b"], IsHistory: true),
                new(4, Effective(4), ["a", "b"], IsDeleted: true),
            ]);

        result.ShouldBe([1, 2]);
    }

    [SkippableFact]
    public async Task GivenDuplicateCodingsAndTextOnACodedObservation_WhenLastNExecutes_ThenNeitherChangesItsRank()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 2,
            [
                new(1, Effective(3), ["a", "a"], Text: "text"),
                new(2, Effective(2), ["a"]),
                new(3, Effective(1), ["a"]),
                new(4, Effective(1), [], Text: "text"),
            ]);

        result.Order().ShouldBe([1, 2, 4]);
    }

    [SkippableFact]
    public async Task GivenDifferentSystemsAndCodeCase_WhenLastNExecutes_ThenIdentitiesRemainDistinct()
    {
        IReadOnlyList<int> result = await ExecuteAsync(
            maximum: 1,
            [
                new(1, Effective(1), ["a"], SystemId: null),
                new(2, Effective(2), ["a"], SystemId: 1),
                new(3, Effective(3), ["a"], SystemId: 2),
                new(4, Effective(4), ["A"], SystemId: 1),
            ]);

        result.Order().ShouldBe([1, 2, 3, 4]);
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

    private async Task<IReadOnlyList<int>> ExecuteAsync(
        int maximum,
        IReadOnlyList<Observation> observations,
        ResourceVisibility? visibility = null,
        int maximumOffset = 9999)
    {
        string connectionString = database.GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        long surrogateBase = Interlocked.Add(ref _surrogateSeed, 10000);

        foreach (Observation observation in observations)
        {
            await SeedAsync(connection, surrogateBase, observation);
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
                            maximum)),
                    SurrogateRange: new SurrogateIdRange(
                        new SqlParameterRef(surrogateBase),
                        new SqlParameterRef(surrogateBase + maximumOffset))),
                Visibility: visibility),
        };
        CompiledSearch compiled = plan.Compile();

        var executor = new LastNSearchExecutor(new SqlExecutionService(
            new LastNSingleTenantStore(connectionString),
            NullLogger<SqlExecutionService>.Instance));
        return await executor.ExecuteAsync(
            1,
            compiled,
            reader => checked((int)(reader.GetInt64(1) - surrogateBase)),
            CancellationToken.None);
    }

    private static async Task SeedAsync(
        SqlConnection connection,
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
                @resourceTypeId, @resourceId, 1, @isHistory, @surrogateId, @isDeleted, 0x01);
            """,
            connection))
        {
            resource.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            resource.Parameters.AddWithValue("@resourceId", $"lastn-{Guid.NewGuid():N}");
            resource.Parameters.AddWithValue("@isHistory", observation.IsHistory);
            resource.Parameters.AddWithValue("@isDeleted", observation.IsDeleted);
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
                    @resourceTypeId, @surrogateId, @searchParamId, @systemId, @code, @codeOverflow);
                """,
                connection);
            token.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
            token.Parameters.AddWithValue("@surrogateId", surrogateId);
            token.Parameters.AddWithValue("@searchParamId", CodeSearchParamId);
            token.Parameters.AddWithValue("@code", codePrefix);
            token.Parameters.AddWithValue("@systemId", (object?)observation.SystemId ?? DBNull.Value);
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
                connection);
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
                connection);
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
        bool IsHistory = false,
        bool IsDeleted = false,
        int? SystemId = 1);
}
