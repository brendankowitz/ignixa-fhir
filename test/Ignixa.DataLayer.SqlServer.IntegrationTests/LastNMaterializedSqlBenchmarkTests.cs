using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class LastNMaterializedSqlBenchmarkTests(ITestOutputHelper output)
{
    private const short ObservationResourceTypeId = 104;
    private const short CodeSearchParamId = 210;
    private const short EffectiveSearchParamId = 211;
    private const int ObservationCount = 10_000;
    private const int CodeGroupCount = 400;
    private const int WarmupCount = 5;
    private const int MeasuredRunCount = 30;
    private const double P95TargetMilliseconds = 100;
    private const int QueryCommandTimeoutSeconds = 30;
    private const int SetupCommandTimeoutSeconds = 300;
    private const long SurrogateBase = 8_100_000_000_000_000;

    [SkippableFact]
    public async Task GivenTheAcceptanceWorkload_WhenMaterializedLastNIsMeasured_ThenItMeetsLatencyCardinalityAndSpillGates()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LASTN_BENCHMARK"), "1", StringComparison.Ordinal))
        {
            throw new Xunit.SkipException("Set RUN_LASTN_BENCHMARK=1 to run the live SQL Server benchmark.");
        }

        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedBenchmarkDataAsync(database.Connection);
        await BuildReadyGenerationAsync(database.Connection);
        CompiledSearch compiled = CreatePlan().Compile();
        await using SqlConnection queryConnection = await database.OpenConnectionAsync(CancellationToken.None);

        for (int index = 0; index < WarmupCount; index++)
        {
            (await ExecuteAsync(queryConnection, compiled)).ShouldBe(CodeGroupCount);
        }

        double[] elapsedMilliseconds = new double[MeasuredRunCount];
        for (int index = 0; index < MeasuredRunCount; index++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int resultCount = await ExecuteAsync(queryConnection, compiled);
            stopwatch.Stop();

            resultCount.ShouldBe(CodeGroupCount);
            elapsedMilliseconds[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        string executionPlan = await ExecuteWithActualPlanAsync(queryConnection, compiled);
        BenchmarkEnvironment environment = await ReadEnvironmentAsync(database.Connection);
        BenchmarkCardinality cardinality = await ReadCardinalityAsync(database.Connection);
        double[] ordered = [.. elapsedMilliseconds.Order()];
        double p50 = Percentile(ordered, 0.50);
        double p95 = Percentile(ordered, 0.95);
        double maximum = ordered[^1];

        output.WriteLine($"serverVersion={environment.ServerVersion}");
        output.WriteLine(
            $"compatibilityLevel={environment.CompatibilityLevel}; logicalCpuCount={environment.LogicalCpuCount}; " +
            $"visibleMemoryMb={environment.VisibleMemoryMb}; timeoutSeconds={QueryCommandTimeoutSeconds}");
        output.WriteLine(
            $"warmups={WarmupCount}; samples={MeasuredRunCount}; p50={p50:F3}ms; " +
            $"p95={p95:F3}ms; max={maximum:F3}ms");
        output.WriteLine(
            $"candidates={cardinality.Candidates}; materializedMemberships={cardinality.MaterializedMemberships}; " +
            $"identities={cardinality.IdentityCount}; components={cardinality.ComponentCount}; " +
            $"results={CodeGroupCount}");

        cardinality.Candidates.ShouldBe(ObservationCount);
        cardinality.MaterializedMemberships.ShouldBe(19_999);
        cardinality.IdentityCount.ShouldBe(1_600);
        cardinality.ComponentCount.ShouldBe(400);
        p95.ShouldBeLessThan(P95TargetMilliseconds);
        executionPlan.Contains("SpillToTempDb", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        Regex.IsMatch(executionPlan, """SpillLevel="[1-9]""").ShouldBeFalse();
    }

    private static SearchPlan CreatePlan()
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
                            Maximum: 1)))),
        };

    private static async Task SeedBenchmarkDataAsync(SqlConnection connection)
    {
        const string sql =
            """
            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects AS first
                CROSS JOIN sys.all_objects AS second
            )
            INSERT INTO dbo.Resource (
                ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId,
                IsDeleted, RawResource)
            SELECT @resourceTypeId, CONCAT('lastn-benchmark-', n), 1, 0, @surrogateBase + n, 0, 0x01
            FROM numbers;

            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects AS first
                CROSS JOIN sys.all_objects AS second
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
            FROM observations AS observation
            CROSS JOIN (VALUES (1), (2), (3)) AS coding(Slot)
            WHERE coding.Slot <= observation.CodingCount;

            ;WITH numbers AS (
                SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY first.object_id, second.object_id) AS n
                FROM sys.all_objects AS first
                CROSS JOIN sys.all_objects AS second
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

        await using SqlCommand command = new(sql, connection)
        {
            CommandTimeout = SetupCommandTimeoutSeconds,
        };
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
        command.Parameters.Add("@surrogateBase", SqlDbType.BigInt).Value = SurrogateBase;
        command.Parameters.Add("@groupCount", SqlDbType.Int).Value = CodeGroupCount;
        command.Parameters.Add("@codeSearchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
        command.Parameters.Add("@effectiveSearchParamId", SqlDbType.SmallInt).Value = EffectiveSearchParamId;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task BuildReadyGenerationAsync(SqlConnection connection)
    {
        await using (SqlCommand enable = connection.CreateCommand())
        {
            enable.CommandText = "dbo.EnableLastNCodeGroupScope";
            enable.CommandType = CommandType.StoredProcedure;
            enable.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
            enable.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
            enable.CommandTimeout = SetupCommandTimeoutSeconds;
            await enable.ExecuteNonQueryAsync(CancellationToken.None);
        }

        long generation;
        Guid attemptId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        await using (SqlCommand start = connection.CreateCommand())
        {
            start.CommandText = "dbo.StartLastNCodeGroupGeneration";
            start.CommandType = CommandType.StoredProcedure;
            start.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
            start.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
            start.Parameters.Add("@AttemptId", SqlDbType.UniqueIdentifier).Value = attemptId;
            start.Parameters.Add("@CurrentDateTime", SqlDbType.DateTime2).Value = now;
            start.Parameters.Add("@LeaseExpiresDateTime", SqlDbType.DateTime2).Value = now.AddMinutes(1);
            SqlParameter generationParameter = start.Parameters.Add("@StartedGeneration", SqlDbType.BigInt);
            generationParameter.Direction = ParameterDirection.Output;
            start.CommandTimeout = SetupCommandTimeoutSeconds;
            await start.ExecuteNonQueryAsync(CancellationToken.None);
            generation = (long)generationParameter.Value;
        }

        await ExecuteGenerationProcedureAsync(
            connection,
            "dbo.BackfillLastNCodeGroupBatch",
            generation,
            attemptId,
            includeRange: true);
        await ExecuteGenerationProcedureAsync(
            connection,
            "dbo.CompleteLastNCodeGroupGeneration",
            generation,
            attemptId,
            includeRange: false);
    }

    private static async Task ExecuteGenerationProcedureAsync(
        SqlConnection connection,
        string procedureName,
        long generation,
        Guid attemptId,
        bool includeRange)
    {
        await using SqlCommand command = connection.CreateCommand();
#pragma warning disable CA2100 // Procedure names are private constants selected by this fixture.
        command.CommandText = procedureName;
#pragma warning restore CA2100
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
        command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
        command.Parameters.Add("@Generation", SqlDbType.BigInt).Value = generation;
        command.Parameters.Add("@AttemptId", SqlDbType.UniqueIdentifier).Value = attemptId;
        if (includeRange)
        {
            command.Parameters.Add("@StartResourceSurrogateId", SqlDbType.BigInt).Value = SurrogateBase + 1;
            command.Parameters.Add("@EndResourceSurrogateId", SqlDbType.BigInt).Value = SurrogateBase + ObservationCount;
            command.Parameters.Add("@LeaseExpiresDateTime", SqlDbType.DateTime2).Value = DateTime.UtcNow.AddMinutes(1);
        }

        command.CommandTimeout = SetupCommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, CompiledSearch compiled)
    {
        await using SqlCommand command = CreateCommand(connection, compiled);
        int count = 0;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            count++;
        }

        return count;
    }

    private static async Task<string> ExecuteWithActualPlanAsync(
        SqlConnection connection,
        CompiledSearch compiled)
    {
#pragma warning disable CA2100
        await using SqlCommand command = new(
            $"SET STATISTICS XML ON;\n{compiled.Sql}\nSET STATISTICS XML OFF;",
            connection)
        {
            CommandTimeout = QueryCommandTimeoutSeconds,
        };
#pragma warning restore CA2100
        AddParameters(command, compiled.Parameters);

        List<string> plans = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        do
        {
            while (await reader.ReadAsync(CancellationToken.None))
            {
                for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (!await reader.IsDBNullAsync(ordinal, CancellationToken.None) &&
                        reader.GetValue(ordinal) is string value &&
                        value.Contains("<ShowPlanXML", StringComparison.Ordinal))
                    {
                        plans.Add(value);
                    }
                }
            }
        }
        while (await reader.NextResultAsync(CancellationToken.None));

        plans.ShouldNotBeEmpty("SET STATISTICS XML must return actual plans for spill inspection.");
        return string.Concat(plans);
    }

    private static SqlCommand CreateCommand(SqlConnection connection, CompiledSearch compiled)
    {
#pragma warning disable CA2100
        SqlCommand command = new(compiled.Sql, connection)
        {
            CommandTimeout = QueryCommandTimeoutSeconds,
        };
#pragma warning restore CA2100
        AddParameters(command, compiled.Parameters);
        return command;
    }

    private static void AddParameters(SqlCommand command, IReadOnlyList<EmittedSqlParameter> parameters)
    {
        foreach (EmittedSqlParameter parameter in parameters)
        {
            SqlParameter sqlParameter = parameter.Value switch
            {
                short value => new(parameter.Name, SqlDbType.SmallInt) { Value = value },
                int value => new(parameter.Name, SqlDbType.Int) { Value = value },
                _ => throw new InvalidOperationException("Unexpected benchmark parameter type."),
            };
            command.Parameters.Add(sqlParameter);
        }
    }

    private static async Task<BenchmarkEnvironment> ReadEnvironmentAsync(SqlConnection connection)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
                   compatibility_level,
                   cpu_count,
                   CAST(physical_memory_kb / 1024 AS bigint)
            FROM sys.databases
            CROSS JOIN sys.dm_os_sys_info
            WHERE name = DB_NAME();
            """;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return new(
            reader.GetString(0),
            reader.GetByte(1),
            reader.GetInt32(2),
            reader.GetInt64(3));
    }

    private static async Task<BenchmarkCardinality> ReadCardinalityAsync(SqlConnection connection)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT_BIG(*) FROM dbo.Resource
                 WHERE ResourceTypeId = @resourceTypeId AND IsHistory = 0 AND IsDeleted = 0),
                (SELECT COUNT_BIG(*) FROM dbo.LastNObservationCodeMembership
                 WHERE ResourceTypeId = @resourceTypeId AND SearchParamId = @searchParamId),
                (SELECT COUNT_BIG(*) FROM dbo.LastNCodeIdentity
                 WHERE ResourceTypeId = @resourceTypeId AND SearchParamId = @searchParamId),
                (SELECT COUNT_BIG(DISTINCT ComponentCodeIdentityId) FROM dbo.LastNCodeIdentity
                 WHERE ResourceTypeId = @resourceTypeId AND SearchParamId = @searchParamId);
            """;
        command.Parameters.Add("@resourceTypeId", SqlDbType.SmallInt).Value = ObservationResourceTypeId;
        command.Parameters.Add("@searchParamId", SqlDbType.SmallInt).Value = CodeSearchParamId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
        => ordered[(int)Math.Ceiling(ordered.Count * percentile) - 1];

    private sealed record BenchmarkEnvironment(
        string ServerVersion,
        byte CompatibilityLevel,
        int LogicalCpuCount,
        long VisibleMemoryMb);

    private sealed record BenchmarkCardinality(
        long Candidates,
        long MaterializedMemberships,
        long IdentityCount,
        long ComponentCount);
}
