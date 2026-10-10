using Ignixa.Api.Services;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

public class SqlSearchParameterCatalogSynchronizerTests
{
    [Fact]
    public async Task GivenFileSystemTenant_WhenSynchronized_ThenNoSqlCatalogIsTouched()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        var synchronizer = new SqlSearchParameterCatalogSynchronizer(registry);

        await synchronizer.SynchronizeAsync(
            new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "File tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "FileSystem" },
            },
            definitions,
            CancellationToken.None);

        sql.ReceivedCalls().ShouldBeEmpty();
        definitions.ReceivedCalls().ShouldBeEmpty();
    }
}
