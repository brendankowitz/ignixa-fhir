using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SqlExecutionServiceConnectionTests
{
    // Unreachable-but-non-blocking loopback endpoint: connecting fails fast (bounded by
    // "Connect Timeout") without needing a live SQL Server, but still exercises a real
    // Microsoft.Data.SqlClient connection attempt and a genuine SqlException end to end.
    private const string UnreachableConnectionString =
        "Data Source=127.0.0.1,1;Connect Timeout=1;TrustServerCertificate=True;Encrypt=False;Initial Catalog=test;User ID=sa;Password=x";

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
        // Arrange -- points at an address that fails fast (bounded by Connect Timeout) rather than
        // a live SQL Server, but still produces a genuine Microsoft.Data.SqlClient SqlException.
        // This proves the retry pipeline is actually wired end-to-end (ShouldHandle -> OnRetry ->
        // MaxRetryAttempts), not just that IsTransient(int) classifies correctly in isolation --
        // the previous version of this class had zero coverage of that wiring.
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = UnreachableConnectionString },
        };
        var logger = new CountingLogger<SqlExecutionService>();
        var service = new SqlExecutionService(store, logger);
        await using var command = new SqlCommand("SELECT 1");

        // Act
        var ex = await Should.ThrowAsync<SqlException>(() =>
            service.ExecuteReaderAsync(1, command, reader => reader.GetInt32(0), CancellationToken.None));

        // Assert -- the connection-establishment failure must be one this service classifies as
        // transient (it is, by design of the connection string above), so it should have been
        // retried MaxRetryAttempts (3) times before the pipeline finally rethrows and logs once.
        SqlExecutionService.IsTransient(ex.Number).ShouldBeTrue();
        logger.WarningCount.ShouldBe(3);
        logger.ErrorCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenATransientConnectionFailureAndDisableRetriesIsTrue_WhenExecutingNonQueryAsync_ThenFailsOnFirstAttemptWithoutRetrying()
    {
        // Arrange -- same unreachable endpoint as above, but with disableRetries: true. Proves the
        // opt-out actually bypasses the pipeline rather than just being accepted and ignored.
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = UnreachableConnectionString },
        };
        var logger = new CountingLogger<SqlExecutionService>();
        var service = new SqlExecutionService(store, logger);
        await using var command = new SqlCommand("SELECT 1");

        // Act
        var ex = await Should.ThrowAsync<SqlException>(() =>
            service.ExecuteNonQueryAsync(1, command, CancellationToken.None, disableRetries: true));

        // Assert -- no OnRetry firings at all: the pipeline was bypassed, not merely configured
        // with zero retries.
        SqlExecutionService.IsTransient(ex.Number).ShouldBeTrue();
        logger.WarningCount.ShouldBe(0);
        logger.ErrorCount.ShouldBe(1);
    }
}
