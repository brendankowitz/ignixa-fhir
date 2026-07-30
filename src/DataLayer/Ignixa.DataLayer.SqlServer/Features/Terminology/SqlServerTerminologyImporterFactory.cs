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
/// <b>Known hazard, pre-existing and not introduced here.</b> The importer also reads and writes
/// <c>dbo.PackageResource</c> against the system partition, while <c>IPackageResourceRepository</c> is
/// registered against tenant 1 — and <c>SqlServerTerminologyService</c>, which reads the import status back,
/// is on the system partition too. Those agree only because the system partition has no connection string of
/// its own and inherits tenant 1's. Give partition 0 its own database and the split becomes real: the
/// importer would look for the package row in the wrong database, and <c>PackageResourceId</c> is a
/// per-database IDENTITY, so a same-numbered row elsewhere would be found instead of nothing. Resolving it
/// means giving the importer a package-row tenant distinct from its terminology-table partition.
/// </para>
/// </summary>
public sealed class SqlServerTerminologyImporterFactory(
    ISqlExecutionService sqlExecutionService,
    SqlServerSearchIndexCacheRegistry cacheRegistry,
    int systemPartitionId,
    ILoggerFactory loggerFactory) : ITerminologyImporterFactory
{
    public async Task<ITerminologyImporter> CreateAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cacheRegistry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var cache = await cacheRegistry.GetOrCreateAsync(systemPartitionId, cancellationToken);

        var systemRepository = new SqlServerSystemRepository(
            cache, loggerFactory.CreateLogger<SqlServerSystemRepository>());

        return new SqlServerCodeSystemImporter(
            sqlExecutionService,
            systemPartitionId,
            systemRepository,
            loggerFactory.CreateLogger<SqlServerCodeSystemImporter>());
    }
}
