using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SharedContentDatabaseTopologyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenDistinctContentDatabases_WhenImportingMissingOrCollidingIdentity_ThenNeitherDatabaseIsModified(bool collidingIdentity)
    {
        var packages = await TestTenantDatabase.CreateEmptyAsync();
        var terminology = await TestTenantDatabase.CreateEmptyAsync();
        try
        {
            var sql = CreateSql(packages.ConnectionString, terminology.ConnectionString);
            var repository = new SqlServerPackageResourceRepository(
                packages.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);
            const string canonical = "http://terminology-contract.example/CodeSystem/topology";
            await repository.UpsertAsync(new PackageResource
            {
                PackageId = "terminology.contract.topology", PackageVersion = "1.0.0", ResourceId = "topology",
                ResourceType = "CodeSystem", Canonical = canonical, FhirVersion = "4.0.1", IsActive = true,
                ResourceJson = TerminologyTestFixture.HierarchicalCodeSystemJson(canonical),
            }, CancellationToken.None);
            var resource = (await repository.GetFromPackageAsync("terminology.contract.topology", "1.0.0", canonical))!;
            if (collidingIdentity)
            {
                await terminology.ExecuteNonQueryAsync($$"""
                    SET IDENTITY_INSERT dbo.PackageResource ON;
                    INSERT dbo.PackageResource
                        (PackageResourceId, PackageId, PackageVersion, ResourceType, Canonical, ResourceId, ResourceJson, FhirVersion, IsActive)
                    VALUES ({{resource.PackageResourceId}}, 'unrelated', '1', 'ValueSet', 'http://unrelated.example/value', 'unrelated', '{}', '4.0.1', 1);
                    SET IDENTITY_INSERT dbo.PackageResource OFF;
                    """);
            }
            using var cache = new SqlServerSearchIndexReferenceDataCache(
                sql, 0, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
            var importer = new SqlServerCodeSystemImporter(
                sql, 0, new SqlServerSystemRepository(cache, NullLogger<SqlServerSystemRepository>.Instance),
                NullLogger<SqlServerCodeSystemImporter>.Instance);

            var exception = await Should.ThrowAsync<InvalidOperationException>(
                () => importer.ImportCodeSystemAsync(1, resource, CancellationToken.None));

            exception.Message.ShouldContain("shared SQL database");
            (await terminology.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TermCodeSystem")).ShouldBe(0);
            (await terminology.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PackageResource WHERE TerminologyImportStatus IS NOT NULL OR ContentHash IS NOT NULL")).ShouldBe(0);
            (await packages.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PackageResource WHERE TerminologyImportStatus IS NOT NULL OR ContentHash IS NOT NULL")).ShouldBe(0);
        }
        finally
        {
            await terminology.DisposeAsync();
            await packages.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenEquivalentResolvedServerAliases_WhenCreatingImporter_ThenSharedDatabaseIsAccepted()
    {
        var database = await TestTenantDatabase.CreateEmptyAsync();
        try
        {
            var original = new SqlConnectionStringBuilder(database.ConnectionString);
            var alternate = new SqlConnectionStringBuilder(database.ConnectionString)
            {
                DataSource = EquivalentAlias(original.DataSource),
            };
            alternate.DataSource.ShouldNotBe(original.DataSource);
            var sql = CreateSql(database.ConnectionString, alternate.ConnectionString);
            using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
            var factory = new SqlServerTerminologyImporterFactory(sql, registry, 0, NullLoggerFactory.Instance);

            (await factory.CreateAsync(CancellationToken.None)).ShouldNotBeNull();
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>
    /// A textually different data source that reaches the same endpoint: toggles an explicit protocol
    /// prefix. Not SERVERPROPERTY('ServerName'): in a container that is the container's internal hostname,
    /// which the test host cannot resolve.
    /// </summary>
    private static string EquivalentAlias(string dataSource)
    {
        foreach (var prefix in (string[])["tcp:", "np:", "lpc:"])
        {
            if (dataSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return dataSource[prefix.Length..];
            }
        }

        return "tcp:" + dataSource;
    }

    internal static SqlExecutionService CreateSql(string packages, string terminology) => new(
        new ContentTenantStore(packages, terminology),
        new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
        NullLogger<SqlExecutionService>.Instance);

    private sealed class ContentTenantStore(string packages, string terminology) : ITenantConfigurationStore
    {
        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken cancellationToken = default)
            => new(new TenantConfiguration
            {
                TenantId = tenantId, DisplayName = "Shared content", FhirVersion = "4.0",
                Storage = new() { Type = "SqlServer", ConnectionString = tenantId == 0 ? terminology : packages },
            });

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => new(Array.Empty<TenantConfiguration>());

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }
}
