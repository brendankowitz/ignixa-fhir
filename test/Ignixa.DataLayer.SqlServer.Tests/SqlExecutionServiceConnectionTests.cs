using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Net;
using System.Net.Sockets;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SqlExecutionServiceConnectionTests
{
    // Accepts TCP connections on loopback but never responds, so Microsoft.Data.SqlClient's own
    // pre-login handshake timeout fires (SqlException.Number == -2) without needing a live SQL
    // Server. Unlike pointing at a closed port (OS-dependent "connection refused" timing/error
    // classification differs between Windows and Linux -- confirmed the hard way in CI), this is
    // driven entirely by the client library's own timer once the TCP handshake itself succeeds,
    // so it is deterministic across platforms.
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

        public string ConnectionString =>
            $"Data Source=127.0.0.1,{Port};Connect Timeout=1;TrustServerCertificate=True;Encrypt=False;Initial Catalog=test;User ID=sa;Password=x";

        private async Task AcceptAndIgnoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Retain each accepted client (do not dispose it here) -- disposing/closing it
                    // immediately would send a TCP reset/FIN, which produces a "connection reset"
                    // style SqlException instead of the intended silent hang. All accepted clients
                    // are disposed together when the listener itself is disposed.
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

    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }

        public int ErrorCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
            else if (logLevel == LogLevel.Error)
            {
                ErrorCount++;
            }
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

    [Fact]
    public async Task GivenATenantThatDoesNotExist_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(999, CancellationToken.None));
        ex.Message.ShouldContain("999");
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("FileSystem");
        ex.Message.ShouldContain("SqlServer");
    }

    [Fact]
    public async Task GivenATenantConfiguredForSqlServerWithNoConnectionString_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = null },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("ConnectionString");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(1205)]
    [InlineData(4060)]
    [InlineData(4221)]
    [InlineData(615)]
    [InlineData(926)]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40613)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    [InlineData(233)]
    [InlineData(64)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(258)]
    public void GivenADocumentedTransientSqlErrorNumber_WhenClassifiedByIsTransient_ThenReturnsTrue(int sqlErrorNumber)
    {
        // Act & Assert
        SqlExecutionService.IsTransient(sqlErrorNumber).ShouldBeTrue();
    }

    [Theory]
    [InlineData(547)] // constraint violation
    [InlineData(0)]
    [InlineData(40614)] // just outside the documented Azure SQL throttling range
    [InlineData(4059)] // just outside the documented "cannot open database" range
    public void GivenANonTransientSqlErrorNumber_WhenClassifiedByIsTransient_ThenReturnsFalse(int sqlErrorNumber)
    {
        // Act & Assert
        SqlExecutionService.IsTransient(sqlErrorNumber).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenANullCommand_WhenExecutingReaderAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteReaderAsync<int>(1, null!, reader => reader.GetInt32(0), CancellationToken.None));
    }

    [Fact]
    public async Task GivenANullReadRow_WhenExecutingReaderAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);
        await using var command = new SqlCommand("SELECT 1");

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteReaderAsync<int>(1, command, null!, CancellationToken.None));
    }

    [Fact]
    public async Task GivenANullCommand_WhenExecutingNonQueryAsync_ThenThrowsArgumentNullException()
    {
        // Arrange
        var service = new SqlExecutionService(new FakeTenantConfigurationStore(), NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ExecuteNonQueryAsync(1, null!, CancellationToken.None));
    }

    [Fact]
    public async Task GivenATransientConnectionFailure_WhenExecutingReaderAsync_ThenThePipelineRetriesAndEventuallyFails()
    {
        // Arrange -- a fake listener that accepts the TCP connection but never responds, forcing
        // Microsoft.Data.SqlClient's own pre-login handshake timeout (SqlException.Number == -2)
        // without needing a live SQL Server. This proves the retry pipeline is actually wired
        // end-to-end (ShouldHandle -> OnRetry -> MaxRetryAttempts), not just that IsTransient(int)
        // classifies correctly in isolation -- the previous version of this class had zero
        // coverage of that wiring.
        using var listener = new UnresponsiveTcpListener();
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = listener.ConnectionString },
        };
        var logger = new CountingLogger<SqlExecutionService>();
        var service = new SqlExecutionService(store, logger);
        await using var command = new SqlCommand("SELECT 1");

        // Act
        var ex = await Should.ThrowAsync<SqlException>(() =>
            service.ExecuteReaderAsync(1, command, reader => reader.GetInt32(0), CancellationToken.None));

        // Assert -- a pre-login handshake timeout (-2) is transient by design, so it should have
        // been retried MaxRetryAttempts (3) times before the pipeline finally rethrows and logs
        // the final failure once.
        ex.Number.ShouldBe(-2);
        logger.WarningCount.ShouldBe(3);
        logger.ErrorCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenATransientConnectionFailureAndDisableRetriesIsTrue_WhenExecutingNonQueryAsync_ThenFailsOnFirstAttemptWithoutRetrying()
    {
        // Arrange -- same unresponsive listener as above, but with disableRetries: true. Proves the
        // opt-out actually bypasses the pipeline rather than just being accepted and ignored.
        using var listener = new UnresponsiveTcpListener();
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = listener.ConnectionString },
        };
        var logger = new CountingLogger<SqlExecutionService>();
        var service = new SqlExecutionService(store, logger);
        await using var command = new SqlCommand("SELECT 1");

        // Act
        var ex = await Should.ThrowAsync<SqlException>(() =>
            service.ExecuteNonQueryAsync(1, command, CancellationToken.None, disableRetries: true));

        // Assert -- no OnRetry firings at all: the pipeline was bypassed, not merely configured
        // with zero retries.
        ex.Number.ShouldBe(-2);
        logger.WarningCount.ShouldBe(0);
        logger.ErrorCount.ShouldBe(1);
    }
}
