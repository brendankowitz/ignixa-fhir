using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Terminology;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// Builds <see cref="SqlServerCodeSystemImporter"/> instances, replacing the by-hand construction
/// <c>ImportTerminologyResourceActivity</c> used to do against a tenant-scoped <c>FhirDbContext</c>.
/// <para>
/// The cache comes from <see cref="SqlServerSearchIndexCacheRegistry"/> rather than being newed up, so the
/// system ids the importer resolves are the ones the write path already has cached. A fresh cache would
/// re-query <c>dbo.System</c> for every url on every import and, worse, would not see systems another
/// component had just created.
/// </para>
/// <para>
/// Everything here is pinned to the system partition, including the cache. Terminology tables are
/// server-wide, and <see cref="SqlServerCodeSystemImporter"/> issues every one of its statements against
/// that partition — a cache built for the triggering tenant would resolve <c>dbo.System</c> ids out of a
/// different database than the one the foreign keys point into.
/// </para>
/// <para>
/// Package rows and terminology share a physical database. The guard rejects split destinations before
/// cache initialization or import can use a database-local PackageResourceId from another database.
/// </para>
/// </summary>
public sealed class SqlServerTerminologyImporterFactory(
    ISqlExecutionService sqlExecutionService,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    int systemPartitionId,
    ILoggerFactory loggerFactory,
    int commandTimeoutSeconds = SqlServerOptions.DefaultTerminologyImportCommandTimeoutSeconds,
    int packageTenantId = 1) : ITerminologyImporterFactory
{
    private readonly int _commandTimeoutSeconds = commandTimeoutSeconds > 0
        ? commandTimeoutSeconds
        : throw new ArgumentOutOfRangeException(
            nameof(commandTimeoutSeconds), commandTimeoutSeconds, "Command timeout must be positive.");

    public async Task<ITerminologyImporter> CreateAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cacheRegistry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        await new SharedContentDatabaseGuard(sqlExecutionService, packageTenantId, systemPartitionId)
            .EnsureCompatibleAsync(cancellationToken);

        var cache = await cacheRegistry.GetOrCreateAsync(systemPartitionId, cancellationToken);

        var systemRepository = new SqlServerSystemRepository(
            cache, loggerFactory.CreateLogger<SqlServerSystemRepository>());

        return new SqlServerCodeSystemImporter(
            sqlExecutionService,
            systemPartitionId,
            systemRepository,
            loggerFactory.CreateLogger<SqlServerCodeSystemImporter>(),
            _commandTimeoutSeconds,
            packageTenantId);
    }
}
