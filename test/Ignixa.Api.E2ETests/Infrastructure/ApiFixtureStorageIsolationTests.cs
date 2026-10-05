using Ignixa.Api.E2ETests._Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Ignixa.Api.E2ETests.Infrastructure;

public class ApiFixtureStorageIsolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("FhirConformanceTest")]
    public async Task GivenTwoFixtures_WhenConfiguringStorage_ThenEachOwnsAUniqueLocation(string? prefix)
    {
        var first = new ConfigurationProbeFixture(prefix);
        var second = new ConfigurationProbeFixture(prefix);
        try
        {
            var firstConfiguration = first.ReadConfiguration();
            var secondConfiguration = second.ReadConfiguration();
            if (firstConfiguration["Tenants:Configurations:1:Storage:Type"] == "SqlServer")
            {
                var firstConnection = new SqlConnectionStringBuilder(firstConfiguration["Tenants:Configurations:1:Storage:ConnectionString"]);
                var secondConnection = new SqlConnectionStringBuilder(secondConfiguration["Tenants:Configurations:1:Storage:ConnectionString"]);
                firstConnection.InitialCatalog.ShouldNotBe(secondConnection.InitialCatalog);
                firstConnection.InitialCatalog.ShouldNotBeOneOf("FHIR_R4", "FHIR_R5", "FhirConformanceTest", "FhirDatabase");

                var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(configured))
                {
                    var baseConnection = new SqlConnectionStringBuilder(configured);
                    firstConnection.InitialCatalog.ShouldNotBe(baseConnection.InitialCatalog);
                    firstConnection.Remove("Initial Catalog");
                    baseConnection.Remove("Initial Catalog");
                    firstConnection.EquivalentTo(baseConnection).ShouldBeTrue("Non-database connection options must be preserved.");
                }
            }
            else
            {
                firstConfiguration["Tenants:Configurations:1:Storage:Type"].ShouldBe("FileSystem");
                firstConfiguration["Tenants:Configurations:1:Storage:BaseDirectory"]
                    .ShouldNotBe(secondConfiguration["Tenants:Configurations:1:Storage:BaseDirectory"]);
            }
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenStartupFailure_WhenInitializingFixture_ThenOwnedStorageIsCleanedAndFailureSurfaces()
    {
        var prefix = $"IgnixaFailedStartup_{Guid.NewGuid():N}";
        var fixture = new ConfigurationProbeFixture(prefix, failStartup: true);
        var configuration = fixture.ReadConfiguration();
        var connectionString = configuration["Tenants:Configurations:1:Storage:ConnectionString"];
        SqlConnection? master = null;
        var databaseName = string.Empty;
        try
        {
            if (connectionString is not null)
            {
                var settings = new SqlConnectionStringBuilder(connectionString);
                databaseName = settings.InitialCatalog;
                databaseName.ShouldStartWith(prefix);
                settings.InitialCatalog = "master";
                master = new SqlConnection(settings.ConnectionString);
                await master.OpenAsync();
            }

            var exception = await Should.ThrowAsync<InvalidOperationException>(() => fixture.InitializeAsync());

            exception.Message.ShouldBe("Synthetic fixture startup failure");
            if (master is not null)
            {
                (await DatabaseExistsAsync(master, databaseName)).ShouldBeFalse();
            }
            Directory.Exists(Path.GetDirectoryName(configuration["BlobStorage:RootDirectory"])).ShouldBeFalse();
        }
        finally
        {
            await fixture.DisposeAsync();
            if (master is not null)
            {
                await using (master)
                {
                    if (await DatabaseExistsAsync(master, databaseName))
                    {
                        await ExecuteDatabaseCommandAsync(master,
                            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task GivenFixtureStorage_WhenDisposed_ThenOnlyItsOwnedStorageIsRemoved()
    {
        var prefix = $"IgnixaLifecycle_{Guid.NewGuid():N}";
        var fixture = new ConfigurationProbeFixture(prefix);
        var configuration = fixture.ReadConfiguration();
        if (configuration["Tenants:Configurations:1:Storage:Type"] == "FileSystem")
        {
            await AssertFileSystemCleanupAsync(fixture, configuration);
        }
        else
        {
            await AssertSqlCleanupAsync(fixture, configuration["Tenants:Configurations:1:Storage:ConnectionString"]!, prefix);
        }
    }

    private static async Task AssertFileSystemCleanupAsync(ConfigurationProbeFixture fixture, IConfiguration configuration)
    {
        var sentinel = new ConfigurationProbeFixture(null);
        var directory = configuration["Tenants:Configurations:1:Storage:BaseDirectory"]!;
        var sentinelDirectory = sentinel.ReadConfiguration()["Tenants:Configurations:1:Storage:BaseDirectory"]!;
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(sentinelDirectory);

            await fixture.DisposeAsync();

            Directory.Exists(directory).ShouldBeFalse();
            Directory.Exists(sentinelDirectory).ShouldBeTrue();
        }
        finally
        {
            await fixture.DisposeAsync();
            await sentinel.DisposeAsync();
        }
    }

    private static async Task AssertSqlCleanupAsync(ConfigurationProbeFixture fixture, string connectionString, string prefix)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        settings.InitialCatalog.ShouldStartWith(prefix);
        var databaseName = settings.InitialCatalog;
        var sentinelName = $"IgnixaLifecycleSentinel_{Guid.NewGuid():N}";
        settings.InitialCatalog = "master";
        await using var master = new SqlConnection(settings.ConnectionString);
        await master.OpenAsync();
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE [{sentinelName}]");
        try
        {
            await fixture.InitializeAsync();
            (await DatabaseExistsAsync(master, databaseName)).ShouldBeTrue();

            await fixture.DisposeAsync();

            (await DatabaseExistsAsync(master, databaseName)).ShouldBeFalse();
            (await DatabaseExistsAsync(master, sentinelName)).ShouldBeTrue();
        }
        finally
        {
            await fixture.DisposeAsync();
            // Both names are unique to this test, including when run against the old leaking fixture.
            foreach (var name in new[] { databaseName, sentinelName })
            {
                if (await DatabaseExistsAsync(master, name))
                {
                    await ExecuteDatabaseCommandAsync(master,
                        $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
                }
            }
        }
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection connection, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task ExecuteDatabaseCommandAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 180;
        command.CommandText = "EXEC sys.sp_executesql @statement";
        command.Parameters.AddWithValue("@statement", sql);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ConfigurationProbeFixture(string? prefix, bool failStartup = false) : IgnixaApiFixture(prefix)
    {
        public IConfiguration ReadConfiguration()
        {
            using var host = new HostBuilder().ConfigureWebHost(builder =>
            {
                builder.Configure(_ => { });
                base.ConfigureWebHost(builder);
            }).Build();
            return host.Services.GetRequiredService<IConfiguration>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (failStartup)
            {
                builder.ConfigureServices(_ => throw new InvalidOperationException("Synthetic fixture startup failure"));
            }
        }
    }
}
