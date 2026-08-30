using System.Data;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class LastNCodeGroupBackfillTests
{
    private const short ResourceTypeId = 104;
    private const short SearchParamId = 210;
    private static readonly LastNCodeGroupScope Scope = new(ResourceTypeId, SearchParamId);

    [SkippableFact]
    public async Task GivenAnEnabledPendingScope_WhenEnabledAgain_ThenGenerationAndStateArePreserved()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        ILastNCodeGroupBackfillService service = CreateService(database);
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);

        // Act
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);

        // Assert
        LastNCodeGroupGenerationStatus status = await ReadGenerationAsync(database);
        status.Generation.ShouldBe(0);
        status.State.ShouldBe("Pending");
        status.SnapshotHighWaterSurrogateId.ShouldBeNull();
    }

    [SkippableFact]
    public async Task GivenAScopeThatWasNotEnabled_WhenBuildStarts_ThenItFailsWithoutCreatingState()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        ILastNCodeGroupBackfillService service = CreateService(database);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => service.BuildAsync(1, Scope, 100, CancellationToken.None));

        // Assert
        exception.Number.ShouldBe(50422);
        (await ReadCountAsync(database, "dbo.LastNCodeGroupGeneration")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenAnEmptyEnabledScope_WhenBuilt_ThenItBecomesReadyWithoutBatches()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        ILastNCodeGroupBackfillService service = CreateService(database);
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);

        // Act
        await service.BuildAsync(1, Scope, 100, CancellationToken.None);

        // Assert
        LastNCodeGroupGenerationStatus status = await ReadGenerationAsync(database);
        status.Generation.ShouldBe(1);
        status.State.ShouldBe("Ready");
        status.SnapshotHighWaterSurrogateId.ShouldBeNull();
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenCurrentResourcesAcrossBatches_WhenBuiltThroughTheService_ThenEveryResourceIsMaterialized()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 10, "a");
        await SeedObservationAsync(database, 12, "b");
        ILastNCodeGroupBackfillService service = CreateService(database);
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);

        // Act
        await service.BuildAsync(1, Scope, 2, CancellationToken.None);

        // Assert
        (await ReadGenerationAsync(database)).State.ShouldBe("Ready");
        (await ReadMembershipCodesAsync(database, 10)).ShouldBe(["a"]);
        (await ReadMembershipCodesAsync(database, 12)).ShouldBe(["b"]);
    }

    [SkippableFact]
    public async Task GivenACommittedFirstBatch_WhenTheGenerationResumes_ThenReplayIsIdempotentAndCompletes()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await EnableAsync(database);
        await SeedObservationAsync(database, 10, "a");
        await SeedObservationAsync(database, 20, "b");
        LastNCodeGroupGenerationStatus generation = await StartAsync(database);
        await BackfillBatchAsync(database, generation.Generation, 10, 10);

        // Act
        await BackfillBatchAsync(database, generation.Generation, 10, 10);
        await BackfillBatchAsync(database, generation.Generation, 11, 20);
        await CompleteAsync(database, generation.Generation);

        // Assert
        (await ReadGenerationAsync(database)).State.ShouldBe("Ready");
        (await ReadMembershipCodesAsync(database, 10)).ShouldBe(["a"]);
        (await ReadMembershipCodesAsync(database, 20)).ShouldBe(["b"]);
        (await ReadCountAsync(database, "dbo.LastNObservationCodeGroup")).ShouldBe(2);
    }

    [SkippableFact]
    public async Task GivenAWriteDuringBuilding_WhenGenerationCompletes_ThenDirtyCurrentVersionWins()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await EnableAsync(database);
        await SeedObservationAsync(database, 50, "old", resourceId: "changing", version: 1);
        LastNCodeGroupGenerationStatus generation = await StartAsync(database);
        await BackfillBatchAsync(database, generation.Generation, 1, 100);
        await ReplaceObservationAndMarkDirtyAsync(database, generation.Generation);

        // Act
        await CompleteAsync(database, generation.Generation);

        // Assert
        (await ReadGenerationAsync(database)).State.ShouldBe("Ready");
        (await ReadMembershipCodesAsync(database, 50)).ShouldBeEmpty();
        (await ReadMembershipCodesAsync(database, 150)).ShouldBe(["new"]);
        (await ReadDirtyCountAsync(database, generation.Generation)).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenDirtyRowsFromAStaleGeneration_WhenCurrentGenerationCompletes_ThenOnlyCurrentRowsAreConsumed()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await EnableAsync(database);
        await SeedObservationAsync(database, 10, "current");
        LastNCodeGroupGenerationStatus first = await StartAsync(database);
        await FailAsync(database, first.Generation, "first attempt");
        LastNCodeGroupGenerationStatus second = await StartAsync(database);
        await InsertDirtyAsync(database, first.Generation, 999);
        await InsertDirtyAsync(database, second.Generation, 10);

        // Act
        await CompleteAsync(database, second.Generation);

        // Assert
        (await ReadGenerationAsync(database)).State.ShouldBe("Ready");
        (await ReadMembershipCodesAsync(database, 10)).ShouldBe(["current"]);
        (await ReadDirtyCountAsync(database, first.Generation)).ShouldBe(1);
        (await ReadDirtyCountAsync(database, second.Generation)).ShouldBe(0);
    }

    [SkippableFact]
    public async Task GivenAForcedLiveSqlFailure_WhenBuildFails_ThenTheGenerationIsFailedAndOriginalErrorEscapes()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 10, "a");
        ILastNCodeGroupBackfillService service = CreateService(database);
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);
        await ExecuteNonQueryAsync(
            database,
            """
            CREATE TRIGGER dbo.ForceLastNBackfillFailure
            ON dbo.LastNObservationCodeGroup
            AFTER INSERT
            AS
                THROW 50991, 'Forced backfill failure.', 1;
            """);

        // Act
        SqlException exception = await Should.ThrowAsync<SqlException>(
            () => service.BuildAsync(1, Scope, 100, CancellationToken.None));

        // Assert
        exception.Number.ShouldBe(50991);
        LastNCodeGroupGenerationStatus status = await ReadGenerationAsync(database);
        status.State.ShouldBe("Failed");
        (await ReadFailureReasonAsync(database)).ShouldNotBeNull().ShouldContain("Forced backfill failure.");
    }

    [SkippableFact]
    public async Task GivenCancellationDuringALiveSqlBatch_WhenBuildStops_ThenTheGenerationIsFailedAsCancelled()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedObservationAsync(database, 10, "a");
        using CancellationTokenSource cancellation = new();
        ISqlExecutionService execution = new CancelAfterGenerationStartExecutionService(
            CreateExecutionService(database),
            cancellation);
        ILastNCodeGroupBackfillService service = new LastNCodeGroupBackfillService(execution);
        await service.EnableScopeAsync(1, Scope, CancellationToken.None);

        // Act
        await Should.ThrowAsync<OperationCanceledException>(
            () => service.BuildAsync(1, Scope, 100, cancellation.Token));

        // Assert
        LastNCodeGroupGenerationStatus status = await ReadGenerationAsync(database);
        status.State.ShouldBe("Failed");
        (await ReadFailureReasonAsync(database)).ShouldBe("Generation cancelled.");
    }

    [SkippableFact]
    public async Task GivenAFailedGeneration_WhenAnotherAttemptStarts_ThenGenerationIncrementsAndFailureClears()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await EnableAsync(database);
        LastNCodeGroupGenerationStatus first = await StartAsync(database);
        await FailAsync(database, first.Generation, new string('x', 1200));
        (await ReadFailureReasonAsync(database)).ShouldNotBeNull().Length.ShouldBe(1000);

        // Act
        LastNCodeGroupGenerationStatus second = await StartAsync(database);
        await FailAsync(database, first.Generation, "stale failure");

        // Assert
        second.Generation.ShouldBe(first.Generation + 1);
        second.State.ShouldBe("Building");
        (await ReadFailureReasonAsync(database)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenANonPositiveBatchSize_WhenBuildStarts_ThenItIsRejected()
    {
        // Arrange
        ILastNCodeGroupBackfillService service = new LastNCodeGroupBackfillService(new UnexpectedExecutionService());

        // Act
        ArgumentOutOfRangeException exception = await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => service.BuildAsync(1, Scope, 0, CancellationToken.None));

        // Assert
        exception.ParamName.ShouldBe("batchSize");
    }

    private static ILastNCodeGroupBackfillService CreateService(LastNTestDatabase database)
        => new LastNCodeGroupBackfillService(CreateExecutionService(database));

    private static ISqlExecutionService CreateExecutionService(LastNTestDatabase database)
        => new SqlExecutionService(
            new LastNSingleTenantStore(BuildTenantConnectionString(database)),
            NullLogger<SqlExecutionService>.Instance);

    private static string BuildTenantConnectionString(LastNTestDatabase database)
    {
        string baseConnectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING is required.");
        SqlConnectionStringBuilder builder = new(baseConnectionString)
        {
            InitialCatalog = database.Connection.Database,
        };
        return builder.ConnectionString;
    }

    private static Task EnableAsync(LastNTestDatabase database)
        => ExecuteProcedureAsync(database, "dbo.EnableLastNCodeGroupScope");

    private static async Task<LastNCodeGroupGenerationStatus> StartAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = "dbo.StartLastNCodeGroupGeneration";
        command.CommandType = CommandType.StoredProcedure;
        AddScopeParameters(command);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return ReadStatus(reader);
    }

    private static Task BackfillBatchAsync(
        LastNTestDatabase database,
        long generation,
        long startId,
        long endId)
        => ExecuteProcedureAsync(
            database,
            "dbo.BackfillLastNCodeGroupBatch",
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation },
            new SqlParameter("@StartResourceSurrogateId", SqlDbType.BigInt) { Value = startId },
            new SqlParameter("@EndResourceSurrogateId", SqlDbType.BigInt) { Value = endId });

    private static Task CompleteAsync(LastNTestDatabase database, long generation)
        => ExecuteProcedureAsync(
            database,
            "dbo.CompleteLastNCodeGroupGeneration",
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });

    private static Task FailAsync(LastNTestDatabase database, long generation, string reason)
        => ExecuteProcedureAsync(
            database,
            "dbo.FailLastNCodeGroupGeneration",
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation },
            new SqlParameter("@FailureReason", SqlDbType.VarChar, -1) { Value = reason });

    private static async Task ExecuteProcedureAsync(
        LastNTestDatabase database,
        string procedureName,
        params SqlParameter[] parameters)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = procedureName;
#pragma warning restore CA2100
        command.CommandType = CommandType.StoredProcedure;
        AddScopeParameters(command);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<LastNCodeGroupGenerationStatus> ReadGenerationAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT Generation, State, SnapshotHighWaterSurrogateId
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId AND SearchParamId = @SearchParamId;
            """;
        AddScopeParameters(command);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        return ReadStatus(reader);
    }

    private static LastNCodeGroupGenerationStatus ReadStatus(SqlDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));

    private static async Task<string?> ReadFailureReasonAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT FailureReason
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId AND SearchParamId = @SearchParamId;
            """;
        AddScopeParameters(command);
        object? value = await command.ExecuteScalarAsync(CancellationToken.None);
        return value is null or DBNull ? null : (string)value;
    }

    private static async Task<int> ReadDirtyCountAsync(LastNTestDatabase database, long generation)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM dbo.LastNCodeGroupDirtyObservation
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId
                AND Generation = @Generation;
            """;
        AddScopeParameters(command);
        command.Parameters.Add("@Generation", SqlDbType.BigInt).Value = generation;
        return (int)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task<IReadOnlyList<string>> ReadMembershipCodesAsync(
        LastNTestDatabase database,
        long resourceSurrogateId)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT identityRow.Code
            FROM dbo.LastNObservationCodeMembership AS membership
            INNER JOIN dbo.LastNCodeIdentity AS identityRow
                ON identityRow.CodeIdentityId = membership.CodeIdentityId
                AND identityRow.ResourceTypeId = membership.ResourceTypeId
                AND identityRow.SearchParamId = membership.SearchParamId
            WHERE membership.ResourceTypeId = @ResourceTypeId
                AND membership.SearchParamId = @SearchParamId
                AND membership.ResourceSurrogateId = @ResourceSurrogateId
            ORDER BY identityRow.Code;
            """;
        AddScopeParameters(command);
        command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken.None);
        List<string> values = [];
        while (await reader.ReadAsync(CancellationToken.None))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task SeedObservationAsync(
        LastNTestDatabase database,
        long resourceSurrogateId,
        string code,
        string? resourceId = null,
        int version = 1)
    {
        await database.SeedResourceAsync(
            ResourceTypeId,
            resourceSurrogateId,
            resourceId ?? $"observation-{resourceSurrogateId}",
            version,
            isHistory: false,
            isDeleted: false,
            CancellationToken.None);
        await database.SeedTokenSearchParamAsync(
            ResourceTypeId,
            resourceSurrogateId,
            SearchParamId,
            7,
            code,
            null,
            CancellationToken.None);
    }

    private static async Task ReplaceObservationAndMarkDirtyAsync(
        LastNTestDatabase database,
        long generation)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = DeclareWriteTvpsSql + """
            INSERT INTO @Resources
                (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare,
                 IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod,
                 SearchParamHash)
            VALUES
                (@ResourceTypeId, 150, 'changing', 2, 1,
                 0, 0, 1, 0x01, 0, 'PUT', 'new-hash');
            INSERT INTO @TokenSearchParams
                (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow)
            VALUES (@ResourceTypeId, 150, @SearchParamId, 7, 'new', NULL);
            DECLARE @AffectedRows INT;
            EXEC dbo.MergeResourcesAndMaintainLastNGroups
                @AffectedRows = @AffectedRows OUTPUT,
                @RaiseExceptionOnConflict = 1,
                @IsResourceChangeCaptureEnabled = 0,
                @TransactionId = NULL,
                @SingleTransaction = 1,
                @Resources = @Resources,
                @ResourceWriteClaims = @ResourceWriteClaims,
                @ReferenceSearchParams = @ReferenceSearchParams,
                @TokenSearchParams = @TokenSearchParams,
                @TokenTexts = @TokenTexts,
                @StringSearchParams = @StringSearchParams,
                @UriSearchParams = @UriSearchParams,
                @NumberSearchParams = @NumberSearchParams,
                @QuantitySearchParams = @QuantitySearchParams,
                @DateTimeSearchParms = @DateTimeSearchParams,
                @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;
            """;
        AddScopeParameters(command);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        (await ReadDirtyCountAsync(database, generation)).ShouldBe(2);
    }

    private static Task InsertDirtyAsync(
        LastNTestDatabase database,
        long generation,
        long resourceSurrogateId)
        => ExecuteNonQueryAsync(
            database,
            """
            INSERT INTO dbo.LastNCodeGroupDirtyObservation
                (ResourceTypeId, SearchParamId, Generation, ResourceSurrogateId)
            VALUES (@ResourceTypeId, @SearchParamId, @Generation, @ResourceSurrogateId);
            """,
            new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt) { Value = ResourceTypeId },
            new SqlParameter("@SearchParamId", SqlDbType.SmallInt) { Value = SearchParamId },
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation },
            new SqlParameter("@ResourceSurrogateId", SqlDbType.BigInt) { Value = resourceSurrogateId });

    private static async Task<int> ReadCountAsync(LastNTestDatabase database, string tableName)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
#pragma warning restore CA2100
        return (int)(await command.ExecuteScalarAsync(CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task ExecuteNonQueryAsync(
        LastNTestDatabase database,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = commandText;
#pragma warning restore CA2100
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static void AddScopeParameters(SqlCommand command)
    {
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = ResourceTypeId;
        command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = SearchParamId;
    }

    private const string DeclareWriteTvpsSql = """
        DECLARE @Resources dbo.ResourceList;
        DECLARE @ResourceWriteClaims dbo.ResourceWriteClaimList;
        DECLARE @ReferenceSearchParams dbo.ReferenceSearchParamList;
        DECLARE @TokenSearchParams dbo.TokenSearchParamList;
        DECLARE @TokenTexts dbo.TokenTextList;
        DECLARE @StringSearchParams dbo.StringSearchParamList;
        DECLARE @UriSearchParams dbo.UriSearchParamList;
        DECLARE @NumberSearchParams dbo.NumberSearchParamList;
        DECLARE @QuantitySearchParams dbo.QuantitySearchParamList;
        DECLARE @DateTimeSearchParams dbo.DateTimeSearchParamList;
        DECLARE @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList;
        DECLARE @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList;
        DECLARE @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList;
        DECLARE @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList;
        DECLARE @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList;
        DECLARE @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList;
        """;

    private sealed class UnexpectedExecutionService : ISqlExecutionService
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("SQL execution was not expected.");

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            bool disableRetries = false)
            => throw new InvalidOperationException("SQL execution was not expected.");
    }

    private sealed class CancelAfterGenerationStartExecutionService(
        ISqlExecutionService inner,
        CancellationTokenSource cancellation) : ISqlExecutionService
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken)
            => inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken);

        public async Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            bool disableRetries = false)
        {
            int result = await inner.ExecuteNonQueryAsync(
                tenantId,
                command,
                cancellationToken,
                disableRetries);
            if (command.CommandText == "dbo.StartLastNCodeGroupGeneration")
            {
                await cancellation.CancelAsync();
            }

            return result;
        }
    }
}
