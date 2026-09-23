using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests.Features.Terminology;

/// <summary>
/// Pins that <c>dbo.ImportTermCodeSystem</c> runs with a configurable <see cref="SqlCommand.CommandTimeout"/>
/// rather than the ADO.NET default of 30 seconds. Left at that default, a large CodeSystem (LOINC, SNOMED)
/// that overruns 30 seconds is classified transient by <c>SqlExecutionService.IsTransient</c> and retried up
/// to three more times before the import is marked <c>Failed</c> -- and <c>Failed</c> is not terminal, so the
/// package is re-offered and re-fails on every subsequent startup. No real database is involved: a fake
/// <see cref="ISqlExecutionService"/> captures the <see cref="SqlCommand"/> handed to it instead of executing
/// it, which is what lets this run without one.
/// </summary>
public class SqlServerCodeSystemImporterCommandTimeoutTests
{
    private const int TestSystemPartitionId = 0;
    private const string CodeSystemUrl = "http://example.org/fhir/CodeSystem/timeout-test";

    private static PackageResource CreatePackageResource() => new()
    {
        PackageResourceId = 42,
        PackageId = "test.package",
        PackageVersion = "1.0.0",
        ResourceType = "CodeSystem",
        Canonical = CodeSystemUrl,
        ResourceId = "timeout-test",
        ResourceJson =
            "{\"resourceType\":\"CodeSystem\",\"url\":\"" + CodeSystemUrl + "\"," +
            "\"content\":\"complete\",\"concept\":[{\"code\":\"a\"}]}",
        FhirVersion = "4.0.1",
        IsActive = true,
    };

    [Fact]
    public async Task GivenACommandTimeoutIsConfigured_WhenACodeSystemIsImported_ThenTheImportCommandUsesIt()
    {
        var sqlExecutionService = new CommandCapturingSqlExecutionService();

        var importer = new SqlServerCodeSystemImporter(
            sqlExecutionService,
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger<SqlServerCodeSystemImporter>.Instance,
            commandTimeoutSeconds: 7);

        var result = await importer.ImportCodeSystemAsync(
            TestSystemPartitionId, CreatePackageResource(), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Completed);
        sqlExecutionService.GuardTransactions.ShouldBe(1);
        sqlExecutionService.GuardProbes.ShouldBe(1);
        sqlExecutionService.ImportCommand.ShouldNotBeNull();
        sqlExecutionService.ImportCommand.CommandTimeout.ShouldBe(7);
    }

    /// <summary>
    /// A zero or negative <see cref="SqlCommand.CommandTimeout"/> is not "unbounded" in ADO.NET -- it either
    /// disables the timeout entirely (0) or is rejected downstream with an opaque error (negative), neither
    /// of which is the intent of a misconfigured value. Failing at construction turns a configuration mistake
    /// into an immediate, diagnosable error instead of a command that silently never times out or a cryptic
    /// failure the first time an import runs.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenANonPositiveCommandTimeout_WhenConstructed_ThenItThrows(int commandTimeoutSeconds)
    {
        var error = Should.Throw<ArgumentOutOfRangeException>(() => new SqlServerCodeSystemImporter(
            new CommandCapturingSqlExecutionService(),
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger<SqlServerCodeSystemImporter>.Instance,
            commandTimeoutSeconds));

        error.ParamName.ShouldBe("commandTimeoutSeconds");
    }

    [Fact]
    public async Task GivenNoCommandTimeoutIsSpecified_WhenACodeSystemIsImported_ThenItDefaultsToTheConfiguredDefault()
    {
        var sqlExecutionService = new CommandCapturingSqlExecutionService();

        // No commandTimeoutSeconds argument -- exercises the constructor's default, which every call site
        // that does not read SqlServerOptions itself (this repo's TerminologyTestFixture included) relies on.
        var importer = new SqlServerCodeSystemImporter(
            sqlExecutionService,
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger<SqlServerCodeSystemImporter>.Instance);

        var result = await importer.ImportCodeSystemAsync(TestSystemPartitionId, CreatePackageResource(), CancellationToken.None);

        result.Status.ShouldBe(TerminologyImportStatus.Completed);
        sqlExecutionService.GuardTransactions.ShouldBe(1);
        sqlExecutionService.GuardProbes.ShouldBe(1);
        sqlExecutionService.ImportCommand.ShouldNotBeNull();
        sqlExecutionService.ImportCommand.CommandTimeout.ShouldBe(
            SqlServerOptions.DefaultTerminologyImportCommandTimeoutSeconds);
    }

    [Fact]
    public async Task GivenSeparateContentDatabases_WhenImportIsRequested_ThenTheGuardRejectsBeforePackageReadOrImport()
    {
        var sqlExecutionService = new CommandCapturingSqlExecutionService { SharedDatabase = false };
        var importer = new SqlServerCodeSystemImporter(
            sqlExecutionService,
            TestSystemPartitionId,
            new FixedSystemRepository(systemId: 1),
            NullLogger<SqlServerCodeSystemImporter>.Instance);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => importer.ImportCodeSystemAsync(
            TestSystemPartitionId, CreatePackageResource(), CancellationToken.None));

        error.Message.ShouldContain("same shared SQL database");
        sqlExecutionService.GuardTransactions.ShouldBe(1);
        sqlExecutionService.GuardProbes.ShouldBe(1);
        sqlExecutionService.PackageRowReads.ShouldBe(0);
        sqlExecutionService.ImportCommand.ShouldBeNull();
    }

    /// <summary>
    /// Models the database-local lock probe, package read and import command without constructing a
    /// SqlDataReader. The real guard callback must acquire a transaction-owned nonce and probe that same
    /// nonce before package access is allowed. Real SQL topology semantics are covered by integration tests.
    /// </summary>
    private sealed class CommandCapturingSqlExecutionService : ISqlExecutionService
    {
        private bool _transactionActive;
        private string? _lockedResource;
        private bool _guardVerified;

        public bool SharedDatabase { get; init; } = true;
        public int GuardTransactions { get; private set; }
        public int GuardProbes { get; private set; }
        public int PackageRowReads { get; private set; }
        public SqlCommand? ImportCommand { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tenantId.ShouldBe(TestSystemPartitionId);
            if (typeof(TResult) == typeof(int) && command.CommandText.Contains("APPLOCK_TEST", StringComparison.Ordinal))
            {
                _transactionActive.ShouldBeTrue();
                _lockedResource.ShouldNotBeNullOrWhiteSpace();
                command.Parameters["@resource"].Value.ShouldBe(_lockedResource);
                GuardProbes++;
                _guardVerified = SharedDatabase;
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)(SharedDatabase ? 0 : 1)]);
            }

            _guardVerified.ShouldBeTrue("the real shared-content guard must complete before package access");
            _transactionActive.ShouldBeFalse("the guard's probe transaction must end before import");
            if (typeof(TResult) == typeof(long) && command.CommandText == "dbo.ImportTermCodeSystem")
            {
                command.CommandType.ShouldBe(System.Data.CommandType.StoredProcedure);
                ImportCommand = command;
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)1L]);
            }

            if (typeof(TResult) == typeof((string? ContentHash, string? Status))
                && command.CommandText.Contains("PackageResource", StringComparison.Ordinal))
            {
                PackageRowReads++;
                // No existing package row content: ImportAsync's unchanged-content guard must not skip the
                // import, or the procedure call this test is pinning would never run.
                return Task.FromResult<IReadOnlyList<TResult>>(
                    [(TResult)(object)((string?)null, (string?)null)]);
            }

            throw new NotSupportedException(
                $"This fixture has no canned response for TResult={typeof(TResult)} (command: {command.CommandText}).");
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => throw new NotSupportedException($"Unexpected non-query: {command.CommandText}");

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tenantId.ShouldBe(1);
            _transactionActive.ShouldBeFalse();
            _transactionActive = true;
            GuardTransactions++;
            try
            {
                return await work(new ProbeTransaction(this), cancellationToken);
            }
            finally
            {
                _lockedResource = null;
                _transactionActive = false;
            }
        }

        public async Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            await ExecuteInTransactionAsync(tenantId, async (transaction, token) =>
            {
                await work(transaction, token);
                return true;
            }, cancellationToken);
        }

        private sealed class ProbeTransaction(CommandCapturingSqlExecutionService owner) : ISqlTransactionContext
        {
            public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
                SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner._transactionActive.ShouldBeTrue();
                owner._lockedResource.ShouldBeNull();
                typeof(TResult).ShouldBe(typeof(int));
                command.CommandText.ShouldContain("sys.sp_getapplock");
                command.CommandText.ShouldContain("@LockOwner = 'Transaction'");
                owner._lockedResource = command.Parameters["@resource"].Value.ShouldBeOfType<string>();
                owner._lockedResource.ShouldNotBeNullOrWhiteSpace();
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)0]);
            }

            public Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
                => throw new NotSupportedException($"Unexpected transaction non-query: {command.CommandText}");
        }
    }

    private sealed class FixedSystemRepository(int systemId) : ISystemRepository
    {
        public Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult(systemId);

        public Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult<int?>(systemId);
    }
}
