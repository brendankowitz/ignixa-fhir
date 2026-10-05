using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Registrations;

public class SharedContentPackageRegistrationTests
{
    [SqlFact]
    public async Task GivenSplitContentDestinations_WhenRegisteredPackageRepositoryWrites_ThenTopologyFailsBeforePackageSql()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING is required.");
        var connection = new SqlConnectionStringBuilder(configured);
        string[] databases = [$"IgnixaSharedContentPackages_{Guid.NewGuid():N}", $"IgnixaSharedContentTerms_{Guid.NewGuid():N}"];
        try
        {
            connection.InitialCatalog = "master";
            foreach (var database in databases)
            {
                await ExecuteAsync(connection.ConnectionString, $"CREATE DATABASE [{database}]");
            }
            var store = Substitute.For<ITenantConfigurationStore>();
            store.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var tenantId = call.Arg<int>();
                var destination = new SqlConnectionStringBuilder(configured) { InitialCatalog = databases[tenantId == 1 ? 0 : 1] };
                return new ValueTask<TenantConfiguration?>(new TenantConfiguration
                {
                    TenantId = tenantId, DisplayName = "Shared content", FhirVersion = "4.0",
                    Storage = new() { Type = "SqlServer", ConnectionString = destination.ConnectionString },
                });
            });
            var sql = new SqlExecutionService(store,
                new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
                NullLogger<SqlExecutionService>.Instance);
            var builder = new ContainerBuilder();
            builder.RegisterDataLayerServices(new ConfigurationBuilder().Build(), "Development");
            builder.RegisterInstance(sql).As<ISqlExecutionService>();
            builder.RegisterInstance(NullLogger<SqlServerPackageResourceRepository>.Instance).As<ILogger<SqlServerPackageResourceRepository>>();
            using var container = builder.Build();
            var repository = container.Resolve<IPackageResourceRepository>();

            var error = await Should.ThrowAsync<InvalidOperationException>(() => repository.UpsertAsync(new PackageResource
            {
                PackageId = "shared.content", PackageVersion = "1", ResourceId = "shared-content", ResourceType = "ValueSet",
                Canonical = "http://terminology-contract.example/registration", ResourceJson = "{}", FhirVersion = "4.0.1",
            }, CancellationToken.None));

            error.Message.ShouldContain("shared SQL database");
        }
        finally
        {
            connection.InitialCatalog = "master";
            foreach (var database in databases)
            {
                await ExecuteAsync(connection.ConnectionString, $"""
                    IF DB_ID('{database}') IS NOT NULL
                    BEGIN
                        ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{database}];
                    END
                    """);
            }
        }
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

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")))
            {
                Skip = "Requires TEST_SQL_CONNECTION_STRING and isolated database creation.";
            }
        }
    }
}
