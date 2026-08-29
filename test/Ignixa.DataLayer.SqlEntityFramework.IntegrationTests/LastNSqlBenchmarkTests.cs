using System.Diagnostics;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

public sealed class LastNSqlBenchmarkTests(ITestOutputHelper output)
{
    private const short ObservationResourceTypeId = 104;
    private const short CodeSearchParamId = 210;
    private const short EffectiveSearchParamId = 211;
    private const int ObservationCount = 10_000;
    private const int CodeGroupCount = 400;
    private const int WarmupCount = 5;
    private const int MeasuredRunCount = 30;
    private const double P95TargetMilliseconds = 100;
    private const long SurrogateBase = 8_100_000_000_000_000;

    [SkippableFact]
    public async Task GivenTypicalPatientCategoryCandidates_WhenBenchmarkingLastN_ThenRecordsDistributionAndEnforcesP95Target()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LASTN_BENCHMARK"), "1", StringComparison.Ordinal))
        {
            throw new Xunit.SkipException("Set RUN_LASTN_BENCHMARK=1 to run the live SQL Server benchmark.");
        }

        string connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING must name a live SQL Server database.");
        await TestSchemaInitializer.InitializeAsync(connectionString, CancellationToken.None);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await SeedBenchmarkDataAsync(connection, transaction);

        CompiledSearch compiled = CreatePlan().Compile();
        for (var i = 0; i < WarmupCount; i++)
        {
            await ExecuteAsync(connection, transaction, compiled.Sql, compiled.Parameters);
        }

        var elapsedMilliseconds = new double[MeasuredRunCount];
        for (var i = 0; i < MeasuredRunCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            int resultCount = await ExecuteAsync(connection, transaction, compiled.Sql, compiled.Parameters);
            stopwatch.Stop();

            resultCount.ShouldBe(CodeGroupCount);
            elapsedMilliseconds[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        BenchmarkCardinality cardinality = await ReadCardinalityAsync(
            connection,
            transaction,
            compiled.Sql,
            compiled.Parameters);
        string executionPlan = await ExecuteWithActualPlanAsync(
            connection,
            transaction,
            compiled.Sql,
            compiled.Parameters);
        bool spilled = executionPlan.Contains("SpillToTempDb", StringComparison.OrdinalIgnoreCase)
            || executionPlan.Contains("SpillLevel", StringComparison.OrdinalIgnoreCase);

        double[] ordered = [.. elapsedMilliseconds.Order()];
        double p50 = Percentile(ordered, 0.50);
        double p95 = Percentile(ordered, 0.95);
        double maximum = ordered[^1];

        output.WriteLine(
            $"runs={MeasuredRunCount}; warmups={WarmupCount}; p50={p50:F3}ms; p95={p95:F3}ms; max={maximum:F3}ms");
        output.WriteLine(
            $"candidates={cardinality.Candidates}; memberships={cardinality.Memberships}; " +
            $"nodes={cardinality.Nodes}; edges={cardinality.Edges}; " +
            $"componentLabels={cardinality.ComponentLabels}; components={cardinality.Components}; spilled={spilled}");

        cardinality.Candidates.ShouldBe(ObservationCount);
        cardinality.Memberships.ShouldBeGreaterThan(ObservationCount);
        cardinality.Nodes.ShouldBe(CodeGroupCount * 4);
        cardinality.ComponentLabels.ShouldBe(cardinality.Nodes);
        cardinality.Components.ShouldBe(CodeGroupCount);
        p95.ShouldBeLessThan(P95TargetMilliseconds);
    }

    private static SearchPlan CreatePlan() => new()
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
                        Maximum: 1)))),
    };

    private static async Task SeedBenchmarkDataAsync(
        SqlConnection connection,
        SqlTransaction transaction)
    {
        const string sql =
            """
            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects first
                CROSS JOIN sys.all_objects second
            )
            INSERT INTO dbo.Resource (
                ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId,
                IsDeleted, RawResource)
            SELECT @resourceTypeId, CONCAT('lastn-benchmark-', n), 1, 0, @surrogateBase + n, 0, 0x01
            FROM numbers;

            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects first
                CROSS JOIN sys.all_objects second
            ),
            observations AS (
                SELECT n, (n - 1) % @groupCount AS GroupId, ((n - 1) % 3) + 1 AS CodingCount
                FROM numbers
            )
            INSERT INTO dbo.TokenSearchParam (
                ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code)
            SELECT @resourceTypeId, @surrogateBase + observation.n, @codeSearchParamId, 1,
                   CONCAT(
                       'group-', observation.GroupId, '-',
                       CASE
                           WHEN observation.CodingCount = 1 THEN 'a'
                           WHEN observation.CodingCount = 2 AND coding.Slot = 1 THEN 'a'
                           WHEN observation.CodingCount = 2 THEN 'b'
                           WHEN coding.Slot = 1 THEN 'b'
                           WHEN coding.Slot = 2 THEN 'c'
                           ELSE 'd'
                       END)
            FROM observations observation
            CROSS JOIN (VALUES (1), (2), (3)) coding(Slot)
            WHERE coding.Slot <= observation.CodingCount;

            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects first
                CROSS JOIN sys.all_objects second
            )
            INSERT INTO dbo.DateTimeSearchParam (
                ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime,
                EndDateTime, IsLongerThanADay, IsMin, IsMax)
            SELECT @resourceTypeId, @surrogateBase + n, @effectiveSearchParamId,
                   DATEADD(minute, n, CAST('2026-01-01' AS datetime2)),
                   DATEADD(minute, n, CAST('2026-01-01' AS datetime2)),
                   0, 1, 1
            FROM numbers;
            """;

        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 60,
        };
        command.Parameters.AddWithValue("@resourceTypeId", ObservationResourceTypeId);
        command.Parameters.AddWithValue("@surrogateBase", SurrogateBase);
        command.Parameters.AddWithValue("@groupCount", CodeGroupCount);
        command.Parameters.AddWithValue("@codeSearchParamId", CodeSearchParamId);
        command.Parameters.AddWithValue("@effectiveSearchParamId", EffectiveSearchParamId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        IReadOnlyList<EmittedSqlParameter> parameters)
    {
#pragma warning disable CA2100
        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 30,
        };
#pragma warning restore CA2100
        AddParameters(command, parameters);

        var count = 0;
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            count++;
        }

        return count;
    }

    private static async Task<BenchmarkCardinality> ReadCardinalityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        IReadOnlyList<EmittedSqlParameter> parameters)
    {
        const string dropStatement =
            "DROP TABLE #code_edges, #code_nodes, #coded_membership, #lastn_candidates;";
        const string metricsStatement =
            """
            SELECT
                (SELECT COUNT_BIG(*) FROM #lastn_candidates) AS Candidates,
                (SELECT COUNT_BIG(*) FROM #coded_membership) AS Memberships,
                (SELECT COUNT_BIG(*) FROM #code_nodes) AS Nodes,
                (SELECT COUNT_BIG(*) FROM #code_edges) AS Edges,
                (SELECT COUNT_BIG(*) FROM #code_nodes) AS ComponentLabels,
                (SELECT COUNT_BIG(DISTINCT ComponentId) FROM #code_nodes) AS Components;
            DROP TABLE #code_edges, #code_nodes, #coded_membership, #lastn_candidates;
            """;
        string instrumentedSql = sql.Replace(dropStatement, metricsStatement, StringComparison.Ordinal);
        instrumentedSql.ShouldNotBe(sql);

#pragma warning disable CA2100
        await using var command = new SqlCommand(instrumentedSql, connection, transaction)
        {
            CommandTimeout = 30,
        };
#pragma warning restore CA2100
        AddParameters(command, parameters);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
        }

        (await reader.NextResultAsync()).ShouldBeTrue();
        (await reader.ReadAsync()).ShouldBeTrue();
        return new BenchmarkCardinality(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }

    private static async Task<string> ExecuteWithActualPlanAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        IReadOnlyList<EmittedSqlParameter> parameters)
    {
#pragma warning disable CA2100
        await using var command = new SqlCommand(
            $"SET STATISTICS XML ON;\n{sql}\nSET STATISTICS XML OFF;",
            connection,
            transaction)
        {
            CommandTimeout = 30,
        };
#pragma warning restore CA2100
        AddParameters(command, parameters);

        var plans = new List<string>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (!await reader.IsDBNullAsync(ordinal) &&
                        reader.GetValue(ordinal) is string value &&
                        value.Contains("<ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Add(value);
                    }
                }
            }
        }
        while (await reader.NextResultAsync());

        plans.ShouldNotBeEmpty("SET STATISTICS XML must return the actual execution plans used for spill inspection.");
        return string.Concat(plans);
    }

    private static void AddParameters(
        SqlCommand command,
        IReadOnlyList<EmittedSqlParameter> parameters)
    {
        foreach (EmittedSqlParameter parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
        => ordered[(int)Math.Ceiling(ordered.Count * percentile) - 1];

    private sealed record BenchmarkCardinality(
        long Candidates,
        long Memberships,
        long Nodes,
        long Edges,
        long ComponentLabels,
        long Components);
}
