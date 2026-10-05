using System.Security.Cryptography;
using System.Text.Json;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Validation.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class PopulatedTerminologySchemaUpgradeTests
{
    // Immutable SQL project snapshots: schema 1 is 40b0d80271780b414d34653470b1ae68dfb3b069;
    // schema 2 is b133498ed3a43b645eda0339a8e6640a4a5e8c9f. Neither is a current schema with a lowered stamp.
    [Theory]
    [InlineData("terminology-schema-version-1.dacpac", 1, "DF8DEF8E025EF2DFFD201E9AFF98BE4AC0D665ED16C9FBB38F19C02B822DE6B3")]
    [InlineData("terminology-schema-version-2.dacpac", 2, "9B539780C7E9838F08DE1CCF75D8B661C6EF377054C71A29BC4A40B856BF528B")]
    public async Task GivenPopulatedExactReleasedSchema_WhenUpgraded_ThenIdentitiesIndexesAndTerminologySurvive(
        string fixtureFile, int originalVersion, string fixtureSha256)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING is required.");
        var builder = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"IgnixaTerminologyUpgrade_{Guid.NewGuid():N}",
        };
        var databaseName = builder.InitialCatalog;
        var connectionString = builder.ConnectionString;
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFile);
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixturePath))).ShouldBe(fixtureSha256);
            using var baseline = DacPackage.Load(fixturePath);
            new DacServices(connectionString).Deploy(baseline, databaseName, options: new DacDeployOptions
            {
                AllowIncompatiblePlatform = true,
                BlockOnPossibleDataLoss = true,
            });
            await ExecuteAsync(connectionString, $"INSERT dbo.SchemaVersion (Version) VALUES ({originalVersion});");
            if (originalVersion == 1)
            {
                (await ScalarAsync<string>(connectionString,
                    "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.System') AND name = 'Value'"))
                    .ShouldContain("_CI_");
                (await ScalarAsync<int>(connectionString,
                    "SELECT COUNT(*) FROM sys.procedures WHERE name IN ('ImportTermCodeSystem', 'ImportTermValueSet', 'ImportTermConceptMap')"))
                    .ShouldBe(0);
                (await ScalarAsync<int>(connectionString,
                    "SELECT COUNT(*) FROM sys.table_types WHERE name IN ('TermConceptList', 'TermValueSetExpansionList', 'TermConceptMapElementList')"))
                    .ShouldBe(0);
            }
            await ExecuteAsync(connectionString, """
                INSERT dbo.ResourceType (Name) VALUES ('Patient');
                DECLARE @type smallint = scope_identity();
                INSERT dbo.System (Value) VALUES ('http://terminology-contract.example/upgrade');
                DECLARE @system int = scope_identity();
                INSERT dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported)
                VALUES ('http://hl7.org/fhir/SearchParameter/Patient-identifier', 'Enabled', SYSDATETIMEOFFSET(), 0);
                DECLARE @search smallint = scope_identity();
                INSERT dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RawResource)
                VALUES (@type, 'terminology-upgrade', 1, 0, 123456, 0,
                    COMPRESS('{"resourceType":"Patient","id":"terminology-upgrade","identifier":[{"system":"http://terminology-contract.example/upgrade","value":"upgrade-token"}]}'));
                INSERT dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code)
                VALUES (@type, 123456, @search, @system, 'upgrade-token');
                INSERT dbo.ResourceChangeData (ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId)
                SELECT 'terminology-upgrade', @type, 1, MIN(ResourceChangeTypeId) FROM dbo.ResourceChangeType;
                INSERT dbo.PackageResource (PackageId, PackageVersion, ResourceType, Canonical, Version, ResourceId, ResourceJson, FhirVersion,
                    IsActive, TerminologyImportStatus, ContentHash)
                VALUES ('terminology.upgrade', '1', 'CodeSystem', 'http://terminology-contract.example/upgrade', '1', 'cs',
                    '{"resourceType":"CodeSystem","id":"cs","url":"http://terminology-contract.example/upgrade","version":"1","status":"active","content":"complete","caseSensitive":true,"hierarchyMeaning":"is-a","concept":[{"code":"parent","display":"Parent","concept":[{"code":"child","display":"Child"}]}]}',
                    '4.0.1', 1, 'Completed', 'old-hash');
                DECLARE @package bigint = scope_identity();
                INSERT dbo.TermCodeSystem (PackageResourceId, SystemId, Version, ConceptCount, Content, IsHierarchical, CaseSensitive, Compositional)
                VALUES (@package, @system, '1', 2, 'complete', 1, 1, 0);
                DECLARE @cs bigint = scope_identity();
                INSERT dbo.TermConcept (TermCodeSystemId, Code, Display, Level, IsActive) VALUES (@cs, 'parent', 'Parent', 0, 1);
                DECLARE @parent bigint = scope_identity();
                INSERT dbo.TermConcept (TermCodeSystemId, Code, Display, ParentConceptId, Level, IsActive)
                VALUES (@cs, 'child', 'Child', @parent, 1, 1);
                INSERT dbo.PackageResource (PackageId, PackageVersion, ResourceType, Canonical, Version, ResourceId, ResourceJson, FhirVersion, IsActive)
                VALUES ('terminology.upgrade', '1', 'ValueSet', 'http://terminology-contract.example/upgrade/vs', '1', 'vs',
                    '{"resourceType":"ValueSet","id":"vs","url":"http://terminology-contract.example/upgrade/vs","version":"1","name":"BeforeUpgrade","status":"active","compose":{"include":[{"system":"http://terminology-contract.example/upgrade","concept":[{"code":"child"}]}]}}',
                    '4.0.1', 1);
                SET @package = scope_identity();
                INSERT dbo.TermValueSet (PackageResourceId, Canonical, Version, Name, Immutable, IsExpanded, ExpansionCodeCount)
                VALUES (@package, 'http://terminology-contract.example/upgrade/vs', '1', 'BeforeUpgrade', 0, 1, 1);
                DECLARE @vs bigint = scope_identity();
                INSERT dbo.TermValueSetExpansion (TermValueSetId, SystemId, Code, IsActive, Ordinal)
                VALUES (@vs, @system, 'child', 1, 0);
                INSERT dbo.PackageResource (PackageId, PackageVersion, ResourceType, Canonical, Version, ResourceId, ResourceJson, FhirVersion, IsActive)
                VALUES ('terminology.upgrade', '1', 'ConceptMap', 'http://terminology-contract.example/upgrade/cm', '1', 'cm',
                    '{"resourceType":"ConceptMap","id":"cm","url":"http://terminology-contract.example/upgrade/cm","version":"1","name":"BeforeUpgrade","status":"active","group":[{"source":"http://terminology-contract.example/upgrade","target":"http://terminology-contract.example/upgrade","element":[{"code":"parent","target":[{"code":"child","equivalence":"equivalent"}]}]}]}',
                    '4.0.1', 1);
                SET @package = scope_identity();
                INSERT dbo.TermConceptMap (PackageResourceId, Canonical, Version, Name)
                VALUES (@package, 'http://terminology-contract.example/upgrade/cm', '1', 'BeforeUpgrade');
                DECLARE @cm bigint = scope_identity();
                INSERT dbo.TermConceptMapElement (TermConceptMapId, SourceSystemId, SourceCode, TargetSystemId, TargetCode, Equivalence, GroupIndex)
                VALUES (@cm, @system, 'parent', @system, 'child', 'equivalent', 0);
                """);
            var before = await SnapshotAsync(connectionString);
            (await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('dbo.TermValueSet'), OBJECT_ID('dbo.TermConceptMap')) AND name = 'Name' AND is_nullable = 0")).ShouldBe(2);
            var store = new UpgradeTenantStore(connectionString);
            var resolver = new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance);
            var deployer = new SchemaDeployer(store, new UpgradeHostEnvironment(),
                Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true, AllowIncompatiblePlatform = true }),
                resolver, NullLogger<SchemaDeployer>.Instance);

            await deployer.UpgradeIfNeededAsync(1, CancellationToken.None);

            (await resolver.GetCurrentVersionAsync(1, CancellationToken.None)).ShouldBe(3);
            (await SnapshotAsync(connectionString)).ShouldBe(before);
            (await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('dbo.TermValueSet'), OBJECT_ID('dbo.TermConceptMap')) AND name = 'Name' AND is_nullable = 1")).ShouldBe(2);
            (await ScalarAsync<int>(connectionString, """
                SELECT COUNT(*) FROM sys.columns
                WHERE collation_name = 'Latin1_General_100_CS_AS'
                  AND ((object_id = OBJECT_ID('dbo.TermCodeSystem') AND name = 'Version')
                    OR (object_id IN (OBJECT_ID('dbo.TermValueSet'), OBJECT_ID('dbo.TermConceptMap'), OBJECT_ID('dbo.PackageResource'))
                        AND name IN ('Canonical', 'Version')))
                """)).ShouldBe(7);
            (await ScalarAsync<string>(connectionString,
                "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.System') AND name = 'Value'"))
                .ShouldBe("Latin1_General_100_CS_AS");
            (await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM sys.procedures WHERE name IN ('ImportTermCodeSystem', 'ImportTermValueSet', 'ImportTermConceptMap')"))
                .ShouldBe(3);
            await ExecuteAsync(connectionString, "UPDATE dbo.TermValueSet SET Name = NULL; UPDATE dbo.TermConceptMap SET Name = NULL;");
            (await ScalarAsync<int>(connectionString, """
                SELECT COUNT(*) FROM dbo.Resource r
                JOIN dbo.TokenSearchParam t ON t.ResourceTypeId = r.ResourceTypeId AND t.ResourceSurrogateId = r.ResourceSurrogateId
                JOIN dbo.System s ON s.SystemId = t.SystemId
                JOIN dbo.SearchParam p ON p.SearchParamId = t.SearchParamId
                WHERE r.ResourceId = 'terminology-upgrade' AND t.Code = 'upgrade-token' AND s.Value = 'http://terminology-contract.example/upgrade'
                """)).ShouldBe(1);
            await AssertTerminologyAsync(store);
            await ExecuteAsync(connectionString, "INSERT dbo.System (Value) VALUES ('http://terminology-contract.example/UPGRADE');");
            (await ScalarAsync<int>(connectionString,
                "SELECT COUNT(*) FROM dbo.System WHERE Value IN ('http://terminology-contract.example/upgrade', 'http://terminology-contract.example/UPGRADE')"))
                .ShouldBe(2);
        }
        finally
        {
            builder.InitialCatalog = "master";
            await ExecuteAsync(builder.ConnectionString, $"""
                IF DB_ID('{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END
                """);
        }
    }

    private static async Task<string> SnapshotAsync(string connectionString)
    {
        string[] tables = ["Resource", "ResourceType", "SearchParam", "System", "TokenSearchParam", "ResourceChangeData", "PackageResource",
            "TermCodeSystem", "TermConcept", "TermValueSet", "TermValueSetExpansion", "TermConceptMap", "TermConceptMapElement"];
        var snapshots = new List<string>();
        foreach (var table in tables)
        {
            var json = await ScalarAsync<string>(connectionString, $"SELECT (SELECT * FROM dbo.{table} FOR JSON PATH, INCLUDE_NULL_VALUES)");
            using var rows = JsonDocument.Parse(json);
            snapshots.Add(string.Join("\n", rows.RootElement.EnumerateArray().Select(row => row.GetRawText()).Order(StringComparer.Ordinal)));
        }
        return string.Join("\n", snapshots);
    }

    private static async Task AssertTerminologyAsync(ITenantConfigurationStore store)
    {
        const string system = "http://terminology-contract.example/upgrade";
        var sql = new SqlExecutionService(store,
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new SqlServerTerminologyService(sql, 0, cache, NullLogger<SqlServerTerminologyService>.Instance);
        (await service.LookupCodeAsync(system, "child", "1", CancellationToken.None)).Display.ShouldBe("Child");
        (await service.LookupCodeAsync(system, "CHILD", "1", CancellationToken.None)).Found.ShouldBeFalse();
        (await service.SubsumesAsync(new SubsumesParameters("parent", "child", system, "1"), CancellationToken.None))
            .Outcome.ShouldBe("subsumes");
        (await service.ExpandValueSetAsync(new ExpansionParameters(system + "/vs"), CancellationToken.None))!
            .Contains.Single().Code.ShouldBe("child");
        (await service.ExpandValueSetAsync(new ExpansionParameters(system + "/VS"), CancellationToken.None)).ShouldBeNull();
        (await service.TranslateCodeAsync(
            new TranslateParameters(system + "/cm", "1", "parent", system, null, null, null, null), CancellationToken.None))
            .Matches.Single().Concept.Code.ShouldBe("child");
        (await service.TranslateCodeAsync(
            new TranslateParameters(system + "/CM", "1", "parent", system, null, null, null, null), CancellationToken.None))
            .Result.ShouldBeFalse();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100
        using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100
        using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private sealed class UpgradeTenantStore(string connectionString) : ITenantConfigurationStore
    {
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken cancellationToken = default)
            => new(new TenantConfiguration { TenantId = tenantId, DisplayName = "Upgrade", FhirVersion = "4.0",
                Storage = new() { Type = "SqlServer", ConnectionString = connectionString } });
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => new(Array.Empty<TenantConfiguration>());
        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private sealed class UpgradeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "TerminologySchemaUpgrade";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
