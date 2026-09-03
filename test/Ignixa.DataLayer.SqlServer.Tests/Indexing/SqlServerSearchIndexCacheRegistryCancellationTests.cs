using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// Same regression as <see cref="SqlServerSearchIndexReferenceDataCacheCancellationTests"/>, one layer up:
/// once a tenant's cache entry exists, <see cref="SqlServerSearchIndexCacheRegistry.GetOrCreateAsync"/>
/// returns the already-completed creation task without ever consulting the caller's own token. The creation
/// task itself is deliberately built with <see cref="CancellationToken.None"/> (its result is shared across
/// every tenant request, so one caller's cancellation must not poison it for everyone else) -- that is by
/// design and untouched here; this pins only the warm-entry return path.
/// </summary>
public class SqlServerSearchIndexCacheRegistryCancellationTests
{
    [Fact]
    public async Task GivenAWarmCacheEntryAndACancelledToken_WhenGetOrCreateAsync_ThenThrows()
    {
        // Arrange: an empty row set is enough for both eager preloads the factory runs on creation.
        var sql = new FixedRowsSqlExecutionService<(short Id, string Name)>();
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        await registry.GetOrCreateAsync(tenantId: 1, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => registry.GetOrCreateAsync(tenantId: 1, cts.Token));
    }
}
