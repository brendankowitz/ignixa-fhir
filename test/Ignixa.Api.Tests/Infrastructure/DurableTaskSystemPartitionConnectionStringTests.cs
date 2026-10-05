using System.Globalization;
using Ignixa.Api.Infrastructure;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// DurableTask's SQL Server backend runs on the system partition's database. It used to reach that
/// connection string through its own hand-rolled copy of the inheritance rule, read off raw
/// <see cref="IConfiguration"/> sections, with no storage-type gate, no parse guard and no Production
/// credential guard -- a fourth implementation that could hand the orchestration backend a connection
/// string every FHIR path would have rejected.
/// <para>
/// The first three tests pin the answers the hand-rolled copy already got right, so consolidating onto
/// <see cref="TenantConnectionStringResolver"/> is held to producing the same result. The rest pin the
/// three guarantees it never had.
/// </para>
/// </summary>
public class DurableTaskSystemPartitionConnectionStringTests
{
    private const string Tenant0ConnectionString = "Server=tenant0;Database=System;Integrated Security=true;";
    private const string Tenant1ConnectionString = "Server=tenant1;Database=Fhir1;Integrated Security=true;";
    private const string Tenant2ConnectionString = "Server=tenant2;Database=Fhir2;Integrated Security=true;";

    [Fact]
    public void ResolveSystemPartitionConnectionString_UsesTenant0DirectConnectionString()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlServer", Tenant0ConnectionString, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", Tenant1ConnectionString));

        DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider)
            .ShouldBe(Tenant0ConnectionString);
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_InheritsFromTenant1ByDefault()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlServer", connectionString: null, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", Tenant1ConnectionString));

        DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider)
            .ShouldBe(Tenant1ConnectionString);
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_InheritsFromTheNamedTenant()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlServer", connectionString: null, inheritFromTenant: 2, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", Tenant1ConnectionString),
            Tenant(index: 2, tenantId: 2, "SqlServer", Tenant2ConnectionString));

        DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider)
            .ShouldBe(Tenant2ConnectionString);
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_NamesDurableTaskWhenTenant0IsAbsent()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 1, "SqlServer", Tenant1ConnectionString));

        var exception = Should.Throw<InvalidOperationException>(
            () => DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider));

        exception.Message.ShouldContain("DurableTask:Provider");
        exception.Message.ShouldContain("Tenant 0");
    }

    /// <summary>
    /// "SqlEntityFramework" is the legacy synonym for "this tenant's data lives in SQL Server" that
    /// deployed configurations still carry. The hand-rolled copy accepted it by never looking at the
    /// storage type at all; the shared resolver accepts it deliberately.
    /// </summary>
    [Fact]
    public void ResolveSystemPartitionConnectionString_AcceptsLegacySqlEntityFrameworkStorageType()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlEntityFramework", connectionString: null, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlEntityFramework", Tenant1ConnectionString));

        DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider)
            .ShouldBe(Tenant1ConnectionString);
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_RejectsInheritingFromANonSqlServerTenant()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlServer", connectionString: null, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "FileSystem", Tenant1ConnectionString));

        var exception = Should.Throw<InvalidOperationException>(
            () => DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider));

        exception.Message.ShouldContain("FileSystem");
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_RejectsAnUnparsableConnectionString()
    {
        using var provider = BuildProvider(
            Tenant(index: 0, tenantId: 0, "SqlServer", "Server=tenant0;Not A Keyword=1;", isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", Tenant1ConnectionString));

        var exception = Should.Throw<InvalidOperationException>(
            () => DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider));

        exception.Message.ShouldContain("could not be parsed");
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_RejectsAPasswordBearingConnectionStringInProduction()
    {
        using var provider = BuildProvider(
            environmentName: "Production",
            Tenant(index: 0, tenantId: 0, "SqlServer", connectionString: null, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", "Server=tenant1;Database=Fhir1;User ID=sa;Password=hunter2;"));

        var exception = Should.Throw<InvalidOperationException>(
            () => DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider));

        exception.Message.ShouldContain("Managed Identity");
    }

    [Fact]
    public void ResolveSystemPartitionConnectionString_AllowsAPasswordOutsideProduction()
    {
        const string PasswordBearing = "Server=tenant1;Database=Fhir1;User ID=sa;Password=hunter2;";

        using var provider = BuildProvider(
            environmentName: "Development",
            Tenant(index: 0, tenantId: 0, "SqlServer", connectionString: null, isSystemPartition: true),
            Tenant(index: 1, tenantId: 1, "SqlServer", PasswordBearing));

        DurableTaskConfiguration.ResolveSystemPartitionConnectionString(provider)
            .ShouldBe(PasswordBearing);
    }

    private static ServiceProvider BuildProvider(params Dictionary<string, string?>[] tenants)
        => BuildProvider("Development", tenants);

    private static ServiceProvider BuildProvider(string environmentName, params Dictionary<string, string?>[] tenants)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var tenant in tenants)
        {
            foreach (var setting in tenant)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantConfigurationStore, AppSettingsTenantConfigurationStore>();
        services.AddSingleton(sp => new ManagedIdentityConnectionStringValidator(
            environmentName,
            sp.GetRequiredService<ILogger<ManagedIdentityConnectionStringValidator>>()));

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Tenant(
        int index,
        int tenantId,
        string storageType,
        string? connectionString = null,
        int? inheritFromTenant = null,
        bool isSystemPartition = false)
    {
        var prefix = $"Tenants:Configurations:{index.ToString(CultureInfo.InvariantCulture)}:";

        var settings = new Dictionary<string, string?>
        {
            [prefix + "TenantId"] = tenantId.ToString(CultureInfo.InvariantCulture),
            [prefix + "DisplayName"] = $"Tenant {tenantId.ToString(CultureInfo.InvariantCulture)}",
            [prefix + "FhirVersion"] = "4.0",
            [prefix + "IsActive"] = "true",
            [prefix + "IsSystemPartition"] = isSystemPartition ? "true" : "false",
            [prefix + "Storage:Type"] = storageType,
        };

        if (connectionString is not null)
        {
            settings[prefix + "Storage:ConnectionString"] = connectionString;
        }

        if (inheritFromTenant is not null)
        {
            settings[prefix + "Storage:InheritConnectionStringFromTenant"] =
                inheritFromTenant.Value.ToString(CultureInfo.InvariantCulture);
        }

        return settings;
    }
}
