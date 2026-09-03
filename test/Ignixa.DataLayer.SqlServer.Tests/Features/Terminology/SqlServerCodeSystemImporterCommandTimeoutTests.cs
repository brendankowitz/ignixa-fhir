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
        sqlExecutionService.ImportCommand.ShouldNotBeNull();
        sqlExecutionService.ImportCommand.CommandTimeout.ShouldBe(7);
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

        await importer.ImportCodeSystemAsync(TestSystemPartitionId, CreatePackageResource(), CancellationToken.None);

        sqlExecutionService.ImportCommand.ShouldNotBeNull();
        sqlExecutionService.ImportCommand.CommandTimeout.ShouldBe(
            SqlServerOptions.DefaultTerminologyImportCommandTimeoutSeconds);
    }

    /// <summary>
    /// Answers the two calls <see cref="SqlServerCodeSystemImporter.ImportCodeSystemAsync"/> makes through
    /// <see cref="ISqlExecutionService.ExecuteReaderAsync{TResult}"/> -- the package-row read (keyed off
    /// <c>TResult</c> being the content-hash/status tuple) and the <c>dbo.ImportTermCodeSystem</c> call
    /// itself (keyed off <c>TResult</c> being <see langword="long"/>) -- without touching a real
    /// <see cref="SqlDataReader"/>, and records the <see cref="SqlCommand"/> the procedure call ran with.
    /// </summary>
    private sealed class CommandCapturingSqlExecutionService : ISqlExecutionService
    {
        public SqlCommand? ImportCommand { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            if (typeof(TResult) == typeof(long))
            {
                ImportCommand = command;
                return Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)1L]);
            }

            if (typeof(TResult) == typeof((string? ContentHash, string? Status)))
            {
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
            => Task.FromResult(0);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("SqlServerCodeSystemImporter does not use ExecuteInTransactionAsync.");

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("SqlServerCodeSystemImporter does not use ExecuteInTransactionAsync.");
    }

    private sealed class FixedSystemRepository(int systemId) : ISystemRepository
    {
        public Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult(systemId);

        public Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
            => Task.FromResult<int?>(systemId);
    }
}
