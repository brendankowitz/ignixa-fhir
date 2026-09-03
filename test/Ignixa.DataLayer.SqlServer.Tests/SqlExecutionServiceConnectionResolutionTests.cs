using System.Net;
using System.Net.Sockets;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// Covers the two things <see cref="SqlExecutionService.OpenConnectionAsync"/> did not do before: resolve a
/// tenant's connection string through the shared resolver (so system-partition inheritance and the legacy
/// storage alias apply), and run the Production credential guard.
/// <para>
/// Every test that expects resolution to <i>succeed</i> asserts a <see cref="SqlException"/> comes back from
/// the connection attempt rather than an <see cref="InvalidOperationException"/> from resolution. That
/// distinction is the whole assertion: reaching the network at all means the string was resolved. The
/// listener below makes reaching the network cheap and deterministic.
/// </para>
/// </summary>
public sealed class SqlExecutionServiceConnectionResolutionTests : IDisposable
{
    // Accepts TCP connections on loopback but never responds, so Microsoft.Data.SqlClient's own pre-login
    // handshake timeout fires (SqlException.Number == -2) without needing a live SQL Server. Same rationale
    // as SqlExecutionServiceConnectionTests' copy: pointing at a closed port classifies differently on
    // Windows and Linux, this does not.
    private sealed class UnresponsiveTcpListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _acceptedClients = [];

        public UnresponsiveTcpListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAndIgnoreAsync(_cts.Token);
        }

        public int Port { get; }

        // No password: these tests run the credential guard in Production, and a password here would make
        // every one of them pass for the wrong reason.
        public string ConnectionString =>
            $"{Prefix};Integrated Security=True";

        /// <summary>The same reachable endpoint, but with SQL authentication -- what the guard rejects.</summary>
        public string PasswordConnectionString =>
            $"{Prefix};User ID=sa;Password=Secret123";

        private string Prefix =>
            $"Data Source=127.0.0.1,{Port};Connect Timeout=1;TrustServerCertificate=True;Encrypt=False;Initial Catalog=test";

        private async Task AcceptAndIgnoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    lock (_acceptedClients)
                    {
                        _acceptedClients.Add(client);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Dispose();
            lock (_acceptedClients)
            {
                foreach (var client in _acceptedClients)
                {
                    client.Dispose();
                }
            }

            _cts.Dispose();
        }
    }

    private sealed class FakeTenantConfigurationStore : ITenantConfigurationStore
    {
        public Dictionary<int, TenantConfiguration> Tenants { get; } = new();

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(Tenants.TryGetValue(tenantId, out var config) ? config : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)Tenants.Values.ToList());

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    /// <summary>Counts how many times the validator logged a per-tenant verdict, at any level.</summary>
    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int VerdictCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => VerdictCount++;
    }

    private const string PasswordConnectionString =
        "Server=tcp:server.database.windows.net,1433;Database=Fhir;User ID=sa;Password=Secret123;";

    private readonly UnresponsiveTcpListener _listener = new();

    public void Dispose() => _listener.Dispose();

    private static TenantConfiguration Tenant(
        int tenantId,
        string storageType = "SqlServer",
        string? connectionString = null,
        bool isSystemPartition = false,
        int inheritFrom = 1)
        => new()
        {
            TenantId = tenantId,
            DisplayName = $"Tenant {tenantId}",
            FhirVersion = "4.0",
            IsSystemPartition = isSystemPartition,
            Storage = new TenantStorageConfiguration
            {
                Type = storageType,
                ConnectionString = connectionString,
                InheritConnectionStringFromTenant = inheritFrom,
            },
        };

    private static SqlExecutionService CreateService(
        ITenantConfigurationStore store,
        string environmentName = "Development",
        ILogger<ManagedIdentityConnectionStringValidator>? validatorLogger = null)
        => new(
            store,
            new ManagedIdentityConnectionStringValidator(
                environmentName, validatorLogger ?? NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);

    // Part A. Before this, OpenConnectionAsync read Storage.ConnectionString raw: the system partition --
    // which every $lookup/$expand/$validate-code and every profile-binding validation now goes through --
    // failed with "has no ConnectionString" no matter how its inheritance was configured.
    [Fact]
    public async Task GivenTheSystemPartitionWithNoConnectionString_WhenOpeningAConnection_ThenItInheritsFromTheConfiguredTenant()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, isSystemPartition: true, inheritFrom: 1);
        store.Tenants[1] = Tenant(1, connectionString: _listener.ConnectionString);
        var service = CreateService(store);

        var ex = await Should.ThrowAsync<SqlException>(() => service.OpenConnectionAsync(0, CancellationToken.None));

        ex.Number.ShouldBe(-2);
    }

    // The legacy storage alias. Deployed tenant configurations and App Service environment variables still
    // carry "SqlEntityFramework", and CompositeRepositoryFactory routes it to SQL Server -- so rejecting it
    // here is the routes-then-throws split this consolidation removes.
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("SqlEntityFramework")]
    public async Task GivenEitherSqlStorageTypeSynonym_WhenOpeningAConnection_ThenResolutionSucceedsAndTheConnectionIsAttempted(string storageType)
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, storageType: storageType, connectionString: _listener.ConnectionString);
        var service = CreateService(store);

        var ex = await Should.ThrowAsync<SqlException>(() => service.OpenConnectionAsync(1, CancellationToken.None));

        ex.Number.ShouldBe(-2);
    }

    [Fact]
    public async Task GivenANonSqlStorageType_WhenOpeningAConnection_ThenTheErrorNamesBothAcceptedTypes()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, storageType: "CosmosDb", connectionString: _listener.ConnectionString);
        var service = CreateService(store);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => service.OpenConnectionAsync(1, CancellationToken.None));

        ex.Message.ShouldContain("CosmosDb");
        ex.Message.ShouldContain("SqlServer");
        ex.Message.ShouldContain("SqlEntityFramework");
    }

    // The shipped configurations do not merely leave tenant 0's connection string empty -- the tenant is
    // dropped from the bound list entirely, because a nested property fails to convert and
    // ConfigurationBinder discards the whole element. The message has to point at that, or an operator
    // reads "does not exist" against an appsettings.json that visibly contains the tenant.
    [Fact]
    public async Task GivenTheSystemPartitionIsAbsentFromTheTenantList_WhenOpeningAConnection_ThenTheErrorPointsAtConfigurationBinding()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: _listener.ConnectionString);
        var service = CreateService(store);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => service.OpenConnectionAsync(0, CancellationToken.None));

        ex.Message.ShouldContain("Tenant 0");
        ex.Message.ShouldContain("TenantId 0");
        ex.Message.ShouldContain("dropped");
    }

    [Fact]
    public async Task GivenTheSystemPartitionInheritsFromAnAbsentTenant_WhenOpeningAConnection_ThenTheErrorNamesBothTenants()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[0] = Tenant(0, isSystemPartition: true, inheritFrom: 7);
        var service = CreateService(store);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => service.OpenConnectionAsync(0, CancellationToken.None));

        ex.Message.ShouldContain("Tenant 0");
        ex.Message.ShouldContain("Tenant 7");
        ex.Message.ShouldContain("not found");
    }

    // Part B. The credential guard's only call site used to be SqlServerTenantServiceFactory, on the
    // FHIR-repository path. The package repository, event store, background-job repository, terminology
    // service and importer all reach a tenant database through ISqlExecutionService without ever going
    // near that factory.
    [Fact]
    public async Task GivenProductionAndAPasswordConnectionString_WhenAnyQueryOpensAConnection_ThenTheCredentialGuardRejectsIt()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: PasswordConnectionString);
        var service = CreateService(store, environmentName: "Production");
        await using var command = new SqlCommand("SELECT 1");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => service.ExecuteReaderAsync(1, command, reader => reader.GetInt32(0), CancellationToken.None));

        ex.Message.ShouldContain("Managed Identity");
    }

    /// <summary>
    /// The same guard reached through a real consumer that has no repository factory anywhere in its
    /// dependency graph -- proving the fix covers the bypass, not just the method it was added to.
    /// </summary>
    [Fact]
    public async Task GivenProductionAndAPasswordConnectionString_WhenTheNonRepositoryPackageRepositoryQueries_ThenTheCredentialGuardRejectsIt()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: PasswordConnectionString);
        var repository = new SqlServerPackageResourceRepository(
            CreateService(store, environmentName: "Production"),
            connectionTenantId: 1,
            NullLogger<SqlServerPackageResourceRepository>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => repository.ListLoadedPackagesAsync(CancellationToken.None));

        ex.Message.ShouldContain("Managed Identity");
    }

    /// <summary>
    /// The negative control for the two above: without it they would pass for a service that rejected every
    /// connection string, guard or no guard.
    /// </summary>
    [Fact]
    public async Task GivenANonProductionEnvironmentAndAPasswordConnectionString_WhenOpeningAConnection_ThenTheGuardAllowsItThrough()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(
            1,
            connectionString: _listener.PasswordConnectionString);
        var service = CreateService(store, environmentName: "Development");

        var ex = await Should.ThrowAsync<SqlException>(() => service.OpenConnectionAsync(1, CancellationToken.None));

        ex.Number.ShouldBe(-2);
    }

    // The guard is a string scan plus a log line. SqlServerTenantServiceFactory ran it once per tenant; the
    // connection path runs on every command, so it has to memoise or it puts both on the hot path.
    [Fact]
    public async Task GivenRepeatedConnectionsForOneTenant_WhenTheConnectionStringIsUnchanged_ThenTheCredentialGuardRunsOnce()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: _listener.ConnectionString);
        var validatorLogger = new CountingLogger<ManagedIdentityConnectionStringValidator>();
        var service = CreateService(store, environmentName: "Production", validatorLogger: validatorLogger);

        for (var i = 0; i < 3; i++)
        {
            await Should.ThrowAsync<SqlException>(() => service.OpenConnectionAsync(1, CancellationToken.None));
        }

        validatorLogger.VerdictCount.ShouldBe(1);
    }

    // Memoising on the resolved string rather than on a bare "already seen this tenant" flag: a rotated
    // credential has to be re-checked, or the guard goes stale the first time configuration reloads.
    [Fact]
    public async Task GivenATenantWhoseConnectionStringChangesToAPasswordOne_WhenOpeningAConnectionAgain_ThenTheGuardRejectsTheNewOne()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: _listener.ConnectionString);
        var service = CreateService(store, environmentName: "Production");

        await Should.ThrowAsync<SqlException>(() => service.OpenConnectionAsync(1, CancellationToken.None));

        store.Tenants[1] = Tenant(1, connectionString: PasswordConnectionString);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("Managed Identity");
    }

    // A rejected connection string must be rejected again, not recorded as "seen" by the memo.
    [Fact]
    public async Task GivenAConnectionStringTheGuardRejected_WhenOpeningAConnectionAgain_ThenItIsRejectedAgain()
    {
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = Tenant(1, connectionString: PasswordConnectionString);
        var service = CreateService(store, environmentName: "Production");

        for (var i = 0; i < 2; i++)
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(
                () => service.OpenConnectionAsync(1, CancellationToken.None));
            ex.Message.ShouldContain("Managed Identity");
        }
    }
}
