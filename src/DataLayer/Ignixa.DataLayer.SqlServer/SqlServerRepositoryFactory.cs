using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Composition root for the SqlServer-native write path, relocated here from
/// Ignixa.DataLayer.SqlEntityFramework's SqlEntityFrameworkRepositoryFactory (which now calls
/// into this class instead of constructing these types inline). Preserves the original's
/// two-scope construction split exactly: <see cref="CreateReferenceDataCacheAsync"/> is called ONCE
/// PER TENANT (outside any per-request scope), immediately followed by both eager preloads;
/// <see cref="CreateRepository"/> is called PER REQUEST, reusing the tenant-scoped cache passed
/// in. Flattening these into one per-request call would change the cache's cardinality and
/// re-run both preloads on every repository creation -- a real, silent behavior/performance
/// regression, not a refactor-neutral change.
/// </summary>
public static class SqlServerRepositoryFactory
{
    public static async Task<SqlServerSearchIndexReferenceDataCache> CreateReferenceDataCacheAsync(
        ISqlExecutionService sqlExecutionService,
        int tenantId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cache = new SqlServerSearchIndexReferenceDataCache(
            sqlExecutionService,
            tenantId,
            loggerFactory.CreateLogger<SqlServerSearchIndexReferenceDataCache>());

        await cache.PreloadResourceTypesAsync(cancellationToken);
        await cache.PreloadSearchParamsAsync(maxRows: null, cancellationToken);

        return cache;
    }

    public static IFhirRepository CreateRepository(
        ISqlExecutionService sqlExecutionService,
        int tenantId,
        SqlServerSearchIndexReferenceDataCache cache,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILoggerFactory loggerFactory)
    {
        var compressor = new GzipResourceCompressor(memoryStreamManager);

        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            sqlExecutionService, tenantId, loggerFactory.CreateLogger<SqlServerPostMergeExtensionUpdater>());

        var mergeRepository = new SqlServerMergeRepository(
            sqlExecutionService, tenantId, compressor, cache, extensionUpdater,
            loggerFactory.CreateLogger<SqlServerMergeRepository>());

        return new SqlServerFhirRepository(
            sqlExecutionService, tenantId, compressor, cache, mergeRepository,
            loggerFactory.CreateLogger<SqlServerFhirRepository>());
    }
}
