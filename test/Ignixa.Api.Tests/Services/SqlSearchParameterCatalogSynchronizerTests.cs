using Ignixa.Api.Services;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Application.Tests.BackgroundOperations.Reindex;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

/// <summary>Spec §5.4: a refresh syncs SQL tenants' catalog rows before publication and skips FileSystem tenants.</summary>
public class SqlSearchParameterCatalogSynchronizerTests
{
    [Fact]
    public async Task GivenFileSystemTenant_WhenSynchronized_ThenNoSqlCatalogIsTouched()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        var synchronizer = new SqlSearchParameterCatalogSynchronizer(registry);

        await synchronizer.SynchronizeAsync(ReindexTestHelper.Tenant(1, "FileSystem"), definitions, CancellationToken.None);

        sql.ReceivedCalls().ShouldBeEmpty();
        definitions.ReceivedCalls().ShouldBeEmpty();
    }
}
