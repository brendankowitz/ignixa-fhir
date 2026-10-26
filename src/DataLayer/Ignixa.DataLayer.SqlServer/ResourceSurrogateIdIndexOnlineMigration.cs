using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Pre-deploy step for schema version 4: adds <c>INCLUDE (ResourceId)</c> to dbo.Resource's
/// <c>IX_Resource_ResourceTypeId_ResourceSurrgateId</c> in place and ONLINE, before DacFx compares the
/// database with the dacpac. DacFx can only express that change as <c>DROP INDEX</c> followed by
/// <c>CREATE INDEX</c> -- it drops first even when the project declares <c>ONLINE = ON</c>, and silently
/// ignores a declared <c>DROP_EXISTING</c> -- so dbo.Resource would lose the index, and its uniqueness
/// guarantee, between the two statements, and the replacement would be built offline, blocking writes for
/// the whole build. <c>CREATE ... WITH (DROP_EXISTING = ON, ONLINE = ON)</c> swaps the index atomically
/// and keeps dbo.Resource readable and writable while it builds. It produces exactly the shape
/// Tables/Resource.sql declares, so the deploy that follows has nothing left to do for this index (pinned
/// by ResourceSurrogateIdIndexOnlineMigrationTests, which fails if the two drift). If the index is missing
/// altogether from a populated dbo.Resource, it is created ONLINE the same way, without DROP_EXISTING.
/// <para>
/// Both upgrade paths run it -- SchemaDeployer.UpgradeIfNeededAsync and Ignixa.SchemaUpgrade.Cli -- before
/// the deploy. A fresh database needs nothing: DacFx creates the index with the INCLUDE directly. On an
/// engine without online index operations the step does nothing and logs a Warning, leaving DacFx's
/// offline build in charge.
/// </para>
/// <para>
/// Costs an operator must plan for. The build is not resumable (see <see cref="OnlineStatement"/>): any
/// interruption -- cancellation, a failover, a killed process -- rolls it back, leaving the original index,
/// and the next attempt starts from scratch. Its final step takes a Sch-M lock on dbo.Resource, which waits
/// for every open transaction on the table to finish; where the engine supports it the wait is
/// <c>WAIT_AT_LOW_PRIORITY</c> (see <see cref="SupportsWaitAtLowPriority"/>), so for
/// <see cref="LowPriorityMaxDurationMinutes"/> minutes it does not queue new requests behind it. The Web
/// host runs the upgrade at startup, before it serves requests, so on a large dbo.Resource a startup or
/// liveness probe can kill the replica mid-build; for such tenants run Ignixa.SchemaUpgrade.Cli before
/// rolling the new version out.
/// </para>
/// <para>
/// Concurrency: an online index build fails at once with error 1912 if another online build of the same
/// index is already running, and every replica of the Web host upgrades every tenant at startup. So the
/// build runs under an exclusive, session-owned <c>sp_getapplock</c> (<see cref="UpgradeLockResource"/>)
/// with no timeout: the first instance builds, the others wait for it, then find -- in the same batch as
/// the DDL -- that the index already has its final shape and do nothing. That guard also makes a stale
/// plan, or a re-run after a failed deploy, a no-op. Only this step is serialised; the DacFx deploy that
/// follows is not.
/// </para>
/// </summary>
internal static class ResourceSurrogateIdIndexOnlineMigration
{
    /// <summary>
    /// The schema version whose dacpac first declares the INCLUDE. Tenants stamped at or above it are never
    /// touched, so a later version that reshapes this index again is not fought by this step.
    /// </summary>
    internal const int TargetSchemaVersion = 4;

    internal const string IndexName = "IX_Resource_ResourceTypeId_ResourceSurrgateId";

    /// <summary>
    /// The <c>sp_getapplock</c> resource serialising the build. Application locks are scoped to the
    /// database, so this serialises exactly the instances upgrading the same tenant database.
    /// </summary>
    internal const string UpgradeLockResource = "Ignixa.SchemaUpgrade." + IndexName;

    /// <summary>
    /// How long the online build's lock waits -- chiefly the final Sch-M on dbo.Resource -- stay at low
    /// priority. A normal-priority Sch-M request that waits behind one long transaction blocks every new
    /// request on dbo.Resource queued behind it (a lock convoy); at low priority it does not. Five minutes
    /// outlasts the ordinary transactions on dbo.Resource by orders of magnitude, so normally the swap
    /// happens without ever blocking anyone, and it is noise next to a build that runs for minutes to
    /// hours. After it, <c>ABORT_AFTER_WAIT = NONE</c> continues waiting at normal priority: SELF would
    /// throw the finished build away, BLOCKERS would kill application transactions.
    /// </summary>
    internal const int LowPriorityMaxDurationMinutes = 5;

    private const string IndexExistsPredicate =
        $"EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Resource') AND name = N'{IndexName}')";

    private const string IndexIncludesResourceIdPredicate = $"""
        EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.Resource') AND i.name = N'{IndexName}'
              AND c.name = N'ResourceId' AND ic.is_included_column = 1)
        """;

    private const string ResourceTableExistsPredicate = "OBJECT_ID(N'dbo.Resource', N'U') IS NOT NULL";

    private const string PlanQuery = $"""
        SELECT CONVERT(int, SERVERPROPERTY('EngineEdition')),
               ISNULL(TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion')), 0),
               CASE WHEN NOT ({ResourceTableExistsPredicate}) THEN 0
                    WHEN NOT {IndexExistsPredicate} THEN 1
                    WHEN NOT {IndexIncludesResourceIdPredicate} THEN 2
                    ELSE 3 END
        """;

    /// <summary>
    /// Decides what to do for the tenant at <paramref name="connectionString"/>. Reads catalog metadata only.
    /// Does not connect at all once <paramref name="currentSchemaVersion"/> has reached
    /// <see cref="TargetSchemaVersion"/>.
    /// </summary>
    public static async Task<ResourceSurrogateIdIndexMigrationPlan> PlanAsync(
        string connectionString, int currentSchemaVersion, CancellationToken cancellationToken)
    {
        if (currentSchemaVersion >= TargetSchemaVersion)
        {
            return new ResourceSurrogateIdIndexMigrationPlan(
                ResourceSurrogateIdIndexMigrationAction.None,
                WaitAtLowPriority: false,
                $"the tenant is already at schema version {currentSchemaVersion}, at or above {TargetSchemaVersion}");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = PlanQuery;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var indexState = reader.GetInt32(2) switch
        {
            0 => ResourceSurrogateIdIndexState.NoResourceTable,
            1 => ResourceSurrogateIdIndexState.Missing,
            2 => ResourceSurrogateIdIndexState.LacksInclude,
            3 => ResourceSurrogateIdIndexState.IncludesResourceId,
            var other => throw new InvalidOperationException($"Unexpected index state {other} from the plan query."),
        };
        return Decide(indexState, engineEdition: reader.GetInt32(0), productMajorVersion: reader.GetInt32(1));
    }

    /// <summary>
    /// The decision itself, given what <see cref="PlanAsync"/> read. <paramref name="engineEdition"/> is
    /// <c>SERVERPROPERTY('EngineEdition')</c>, <paramref name="productMajorVersion"/>
    /// <c>SERVERPROPERTY('ProductMajorVersion')</c>.
    /// </summary>
    internal static ResourceSurrogateIdIndexMigrationPlan Decide(
        ResourceSurrogateIdIndexState indexState, int engineEdition, int productMajorVersion)
    {
        switch (indexState)
        {
            case ResourceSurrogateIdIndexState.IncludesResourceId:
                return new ResourceSurrogateIdIndexMigrationPlan(
                    ResourceSurrogateIdIndexMigrationAction.None, WaitAtLowPriority: false, "the index already includes ResourceId");
            case ResourceSurrogateIdIndexState.NoResourceTable:
                return new ResourceSurrogateIdIndexMigrationPlan(
                    ResourceSurrogateIdIndexMigrationAction.None,
                    WaitAtLowPriority: false,
                    "dbo.Resource does not exist yet; the deploy creates it and the index together");
        }

        var missing = indexState == ResourceSurrogateIdIndexState.Missing;
        if (!SupportsOnlineIndexOperations(engineEdition))
        {
            return new ResourceSurrogateIdIndexMigrationPlan(
                missing
                    ? ResourceSurrogateIdIndexMigrationAction.DeferCreationToOfflineDeploy
                    : ResourceSurrogateIdIndexMigrationAction.DeferConversionToOfflineDeploy,
                WaitAtLowPriority: false,
                $"engine edition {engineEdition} (SERVERPROPERTY('EngineEdition')) does not support online index operations");
        }

        var waitAtLowPriority = SupportsWaitAtLowPriority(engineEdition, productMajorVersion);
        return new ResourceSurrogateIdIndexMigrationPlan(
            missing ? ResourceSurrogateIdIndexMigrationAction.CreateOnline : ResourceSurrogateIdIndexMigrationAction.ConvertOnline,
            waitAtLowPriority,
            $"engine edition {engineEdition} supports online index operations" + (waitAtLowPriority
                ? $"; its final Sch-M lock waits at low priority for up to {LowPriorityMaxDurationMinutes} minutes"
                : $"; WAIT_AT_LOW_PRIORITY needs SQL Server 2022 (major version 16) or later, this is major version " +
                  $"{productMajorVersion}, so its final Sch-M lock waits at normal priority"));
    }

    /// <summary>
    /// Decided from the engine edition rather than by attempting <c>ONLINE = ON</c> and catching the
    /// rejection: the answer is known before any DDL runs, the reason can be stated in the log, and an
    /// edition this list does not know degrades to the logged offline path rather than to a failed
    /// upgrade. 3 = Enterprise, Developer and Evaluation; 5 = Azure SQL Database (the production
    /// target); 8 = Azure SQL Managed Instance; 12 = SQL database in Microsoft Fabric. Standard, Web and
    /// Express (2, 4) and Azure SQL Edge (9) do not support online index operations.
    /// </summary>
    internal static bool SupportsOnlineIndexOperations(int engineEdition) => engineEdition is 3 or 5 or 8 or 12;

    /// <summary>
    /// <c>CREATE INDEX ... WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (...)))</c> is accepted by SQL Server 2022
    /// (major version 16) and later and by the Azure SQL engines (Database, Managed Instance, Fabric), whose
    /// <c>ProductMajorVersion</c> does not track features. Only consulted for online-capable editions.
    /// </summary>
    internal static bool SupportsWaitAtLowPriority(int engineEdition, int productMajorVersion)
        => engineEdition is 5 or 8 or 12 || productMajorVersion >= 16;

    /// <summary>
    /// The guarded DDL batch for an online <paramref name="plan"/>. It re-checks the index's shape itself,
    /// after the upgrade lock is held, and returns one bit: 1 if this batch built the index, 0 if it was
    /// already in its final shape.
    /// <para>
    /// Key columns, uniqueness, filter and partition scheme must stay identical to Tables/Resource.sql, and
    /// no persisted index option may be added that the project does not declare -- otherwise the deploy sees
    /// a difference and drops and recreates the index anyway. <c>ONLINE</c> and <c>WAIT_AT_LOW_PRIORITY</c>
    /// are not persisted. <c>RESUMABLE = ON</c> would let an interrupted build continue where it stopped,
    /// but SQL Server rejects it for filtered indexes (error 10671, "The RESUMABLE option is not supported
    /// for creating a filtered index"), and this index is filtered.
    /// </para>
    /// </summary>
    internal static string OnlineStatement(ResourceSurrogateIdIndexMigrationPlan plan)
    {
        var online = plan.WaitAtLowPriority
            ? $"ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = {LowPriorityMaxDurationMinutes} MINUTES, ABORT_AFTER_WAIT = NONE))"
            : "ONLINE = ON";
        var (guard, options) = plan.Action switch
        {
            ResourceSurrogateIdIndexMigrationAction.ConvertOnline =>
                ($"{IndexExistsPredicate} AND NOT {IndexIncludesResourceIdPredicate}", $"DROP_EXISTING = ON, {online}"),
            ResourceSurrogateIdIndexMigrationAction.CreateOnline =>
                ($"{ResourceTableExistsPredicate} AND NOT {IndexExistsPredicate}", online),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Action, "Not an online index action."),
        };
        return $"""
            IF {guard}
            BEGIN
                CREATE UNIQUE NONCLUSTERED INDEX {IndexName}
                    ON dbo.Resource (ResourceTypeId, ResourceSurrogateId)
                    INCLUDE (ResourceId)
                    WHERE IsHistory = 0 AND IsDeleted = 0
                    WITH ({options})
                    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
                SELECT CONVERT(bit, 1);
            END
            ELSE
                SELECT CONVERT(bit, 0);
            """;
    }

    /// <summary>
    /// Carries out <paramref name="plan"/>. Only the online actions touch the database; the deferred actions
    /// log a Warning, because the deploy about to run will build the index offline.
    /// </summary>
    public static async Task ApplyAsync(
        string connectionString,
        ResourceSurrogateIdIndexMigrationPlan plan,
        int tenantId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        switch (plan.Action)
        {
            case ResourceSurrogateIdIndexMigrationAction.None:
                logger.LogDebug(
                    "Tenant {TenantId}: no pre-deploy work on {IndexName} needed: {Reason}.",
                    tenantId, IndexName, plan.Reason);
                return;

            case ResourceSurrogateIdIndexMigrationAction.DeferConversionToOfflineDeploy:
                logger.LogWarning(
                    "Tenant {TenantId}: {IndexName} on dbo.Resource cannot be converted online because {Reason}. {Consequence}",
                    tenantId, IndexName, plan.Reason, plan.OfflineDeployConsequence);
                return;

            case ResourceSurrogateIdIndexMigrationAction.DeferCreationToOfflineDeploy:
                logger.LogWarning(
                    "Tenant {TenantId}: {IndexName} is missing from dbo.Resource and cannot be created online because {Reason}. {Consequence}",
                    tenantId, IndexName, plan.Reason, plan.OfflineDeployConsequence);
                return;

            case ResourceSurrogateIdIndexMigrationAction.ConvertOnline:
            case ResourceSurrogateIdIndexMigrationAction.CreateOnline:
                await BuildOnlineAsync(connectionString, plan, tenantId, logger, cancellationToken);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan.Action, "Unknown index migration action.");
        }
    }

    private static async Task BuildOnlineAsync(
        string connectionString,
        ResourceSurrogateIdIndexMigrationPlan plan,
        int tenantId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var converting = plan.Action == ResourceSurrogateIdIndexMigrationAction.ConvertOnline;
        logger.LogInformation(
            "Tenant {TenantId}: {Operation} {IndexName} on dbo.Resource ONLINE before the schema deploy ({Reason}). " +
            "This can take a long time on a large table; an interruption rolls the build back and the next attempt restarts it from scratch.",
            tenantId, converting ? "converting" : "creating", IndexName, plan.Reason);

        // Not pooled: the application lock is owned by the session, so if releasing it fails the lock must
        // still end with the connection rather than survive in a pooled session and block every later upgrade.
        var unpooled = new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var connection = new SqlConnection(unpooled);
        await connection.OpenAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        if (await AcquireUpgradeLockAsync(connection, cancellationToken))
        {
            logger.LogInformation(
                "Tenant {TenantId}: waited {Elapsed} for another instance's upgrade lock on {IndexName}.",
                tenantId, stopwatch.Elapsed, IndexName);
        }

        bool built;
        stopwatch.Restart();
        try
        {
            await using var command = connection.CreateCommand();
            // CA2100 suppressed: OnlineStatement is assembled only from this class's constants and an option
            // chosen by Decide; nothing in it comes from input.
#pragma warning disable CA2100
            command.CommandText = OnlineStatement(plan);
#pragma warning restore CA2100
            // Unbounded, like DacFx's own default LongRunningCommandTimeout (0) that the offline build would
            // otherwise run under: an online build of dbo.Resource takes as long as the table is large. The
            // cancellation token still stops it; a cancelled online build rolls back and leaves the original index.
            command.CommandTimeout = 0;
            built = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            await ReleaseUpgradeLockAsync(connection, tenantId, logger);
        }

        if (built)
        {
            logger.LogInformation(
                "Tenant {TenantId}: {Operation} {IndexName} on dbo.Resource ONLINE in {Elapsed}.",
                tenantId, converting ? "converted" : "created", IndexName, stopwatch.Elapsed);
        }
        else
        {
            logger.LogInformation(
                "Tenant {TenantId}: {IndexName} on dbo.Resource was already {State} when this instance held the upgrade lock " +
                "(another instance built it); nothing to do.",
                tenantId, IndexName, converting ? "converted" : "created");
        }
    }

    /// <summary>Blocks until the lock is granted; returns whether it had to wait for another session.</summary>
    private static async Task<bool> AcquireUpgradeLockAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "sys.sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        // The wait is unbounded: the holder is building the same index, which can take hours, and giving up
        // would only fail this instance's upgrade. The cancellation token still ends the wait.
        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Resource", UpgradeLockResource);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Session");
        command.Parameters.AddWithValue("@LockTimeout", -1);
        var result = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
        result.Direction = ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // 0 = granted at once, 1 = granted after waiting; negative = timeout, cancellation, deadlock victim or
        // a parameter error -- none of which may proceed to the DDL.
        var code = (int)result.Value;
        return code switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(
                $"Could not acquire the application lock '{UpgradeLockResource}' that serialises the online build of " +
                $"{IndexName}: sp_getapplock returned {code}."),
        };
    }

    private static async Task ReleaseUpgradeLockAsync(SqlConnection connection, int tenantId, ILogger logger)
    {
        if (connection.State != ConnectionState.Open)
        {
            // A broken connection has ended its session, and the session-owned lock with it.
            return;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "sys.sp_releaseapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", UpgradeLockResource);
            command.Parameters.AddWithValue("@LockOwner", "Session");
            // Not the caller's token: a cancelled build still has to give the lock back.
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // Not rethrown: it would replace whatever the build itself threw. The connection is unpooled, so
            // closing it, which follows immediately, ends the session and releases the lock regardless.
            logger.LogWarning(
                ex,
                "Tenant {TenantId}: releasing the application lock '{LockResource}' failed; it is released when the connection closes.",
                tenantId, UpgradeLockResource);
        }
    }
}
