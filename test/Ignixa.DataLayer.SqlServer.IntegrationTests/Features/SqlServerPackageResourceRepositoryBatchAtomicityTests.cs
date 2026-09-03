using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// <see cref="SqlServerPackageResourceRepository.BatchUpsertAsync"/> must be all-or-nothing: a duplicate key
/// raised anywhere in a batch (the expected shape when <c>TenantPackagePreloadService</c> and
/// <c>EmbeddedPackagePreloadService</c> load the same package concurrently) must not leave the batch
/// half-applied, and must not be reported to the caller as success while resources are missing.
/// <para>
/// The duplicate key is injected deterministically via <see cref="ThrowAfterNWritesExecutionService"/> rather
/// than raced with real concurrency, for the same reason
/// <c>SqlExecutionServiceTransactionTests.CaptureTransientSqlExceptionAsync</c> captures a real exception
/// instead of trying to time one: a genuine two-writer race can land the collision on any resource in the
/// batch depending on scheduling, which makes a test that depends on it flaky and unable to pin down *which*
/// resources would have been dropped. Capturing a real <see cref="SqlException"/> from the real unique index
/// (<c>UQ_PackageResource_Identity</c>) and injecting it at a chosen point in the batch gives the same
/// error the race produces, at a position the test controls.
/// </para>
/// </summary>
public class SqlServerPackageResourceRepositoryBatchAtomicityTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static PackageResource Resource(
        string packageId,
        string resourceId,
        string packageVersion = "1.0.0",
        string json = """{"resourceType":"StructureDefinition"}""") => new()
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            ResourceType = "StructureDefinition",
            Canonical = $"http://example.org/{resourceId}",
            ResourceId = resourceId,
            ResourceJson = json,
            FhirVersion = "4.0.1",
            IsActive = true,
        };

    private static string NewPackageId() => $"pkg.{Guid.NewGuid():N}";

    private Task<int> CountAsync(string packageId) => _database.ExecuteScalarAsync<int>(
        $"SELECT COUNT(*) FROM dbo.PackageResource WHERE PackageId = '{packageId}'", CancellationToken.None);

    /// <summary>
    /// Captures a genuine <c>SqlException</c> (Number 2601: duplicate key on a unique index) by tripping
    /// <c>UQ_PackageResource_Identity</c> for real, the same way
    /// <c>SqlExecutionServiceTransactionTests.CaptureTransientSqlExceptionAsync</c> captures a real timeout --
    /// a hand-rolled stand-in would not be classified by <c>IsDuplicateKey</c>, which is the thing under test.
    /// Uses its own throwaway package id, deliberately unrelated to the id the calling test asserts row
    /// counts against, so the row this inserts never shows up in that test's counts.
    /// </summary>
    private async Task<SqlException> CaptureDuplicateKeySqlExceptionAsync()
    {
        var captureId = NewPackageId();
        var insertSql =
            $"INSERT INTO dbo.PackageResource " +
            "(PackageId, PackageVersion, ResourceType, Canonical, Version, ResourceId, ResourceJson, FhirVersion, LoadedDate, IsActive) " +
            $"VALUES ('{captureId}', '1.0.0', 'StructureDefinition', 'http://example.org/capture', NULL, 'capture', '{{}}', '4.0.1', SYSDATETIMEOFFSET(), 1)";

        await _database.ExecuteNonQueryAsync(insertSql, CancellationToken.None);

        var ex = await Should.ThrowAsync<SqlException>(() => _database.ExecuteNonQueryAsync(insertSql, CancellationToken.None));
        ex.Number.ShouldBeOneOf(2601, 2627);
        return ex;
    }

    [Fact]
    public async Task GivenANewPackage_WhenADuplicateKeyInterruptsTheMiddleOfTheBatch_ThenNoneOfTheBatchIsWritten()
    {
        var packageId = NewPackageId();
        var duplicateKey = await CaptureDuplicateKeySqlExceptionAsync();

        // Five brand-new resources; the third write in the transaction throws the captured duplicate key
        // instead of executing. If the batch were still per-resource auto-commit (the defect), the first two
        // would already be durably written by the time the exception is caught. If the batch is one
        // transaction (the fix), the exception rolls back the two writes that already ran inside it.
        var poisoned = new ThrowAfterNWritesExecutionService(_database.SqlExecutionService, throwOnWriteNumber: 3, duplicateKey);
        var repository = new SqlServerPackageResourceRepository(poisoned, _database.TenantId, NullLogger<SqlServerPackageResourceRepository>.Instance);

        await Should.NotThrowAsync(() => repository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1"),
            Resource(packageId, "sd-2"),
            Resource(packageId, "sd-3"),
            Resource(packageId, "sd-4"),
            Resource(packageId, "sd-5"),
        ], CancellationToken.None));

        // Not 2 (the first two writes that ran before the poisoned one) and not 5 (a duplicate key isn't a
        // reason to believe the rest would have succeeded either) -- zero, because the whole unit rolled back.
        (await CountAsync(packageId)).ShouldBe(0);
    }

    [Fact]
    public async Task GivenAPackageAlreadyLoadedByAnotherThread_WhenThisThreadsReloadCollidesMidBatch_ThenTheExistingRowsAreUntouchedAndNothingThrows()
    {
        // Simulates the case IsDuplicateKey's catch exists for: another loader already fully wrote this
        // package. This thread's own upsert of the same five resources -- which would otherwise be five
        // harmless updates -- collides with the other loader on the third one and must not throw, and must
        // not leave two of its five updates applied while the rest keep the other loader's original content.
        var packageId = NewPackageId();
        var duplicateKey = await CaptureDuplicateKeySqlExceptionAsync();

        var originalRepository = new SqlServerPackageResourceRepository(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPackageResourceRepository>.Instance);
        await originalRepository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", json: """{"v":"original"}"""),
            Resource(packageId, "sd-2", json: """{"v":"original"}"""),
            Resource(packageId, "sd-3", json: """{"v":"original"}"""),
            Resource(packageId, "sd-4", json: """{"v":"original"}"""),
            Resource(packageId, "sd-5", json: """{"v":"original"}"""),
        ], CancellationToken.None);

        var poisoned = new ThrowAfterNWritesExecutionService(_database.SqlExecutionService, throwOnWriteNumber: 3, duplicateKey);
        var racingRepository = new SqlServerPackageResourceRepository(poisoned, _database.TenantId, NullLogger<SqlServerPackageResourceRepository>.Instance);

        await Should.NotThrowAsync(() => racingRepository.BatchUpsertAsync(
        [
            Resource(packageId, "sd-1", json: """{"v":"racing-writer"}"""),
            Resource(packageId, "sd-2", json: """{"v":"racing-writer"}"""),
            Resource(packageId, "sd-3", json: """{"v":"racing-writer"}"""),
            Resource(packageId, "sd-4", json: """{"v":"racing-writer"}"""),
            Resource(packageId, "sd-5", json: """{"v":"racing-writer"}"""),
        ], CancellationToken.None));

        (await CountAsync(packageId)).ShouldBe(5);

        // sd-1's update ran and would have succeeded on its own before the poisoned third write fired --
        // proving the rollback undid it, not just skipped the ones after the failure.
        var sd1Json = await _database.ExecuteScalarAsync<string>(
            $"SELECT ResourceJson FROM dbo.PackageResource WHERE PackageId = '{packageId}' AND ResourceId = 'sd-1'",
            CancellationToken.None);
        sd1Json.ShouldBe("""{"v":"original"}""");
    }

    /// <summary>
    /// Forwards every call to <paramref name="inner"/> (the real <see cref="ISqlExecutionService"/> against
    /// the real test database), except that the <paramref name="throwOnWriteNumber"/>-th write -- counted
    /// across both the auto-commit path (<see cref="ExecuteNonQueryAsync"/>) and the transactional path (each
    /// <see cref="ISqlTransactionContext.ExecuteNonQueryAsync"/> call) -- throws
    /// <paramref name="toThrow"/> instead of executing. Counting across both paths is what lets the same
    /// decorator prove the defect on the old per-resource implementation and the fix on the transactional one
    /// with no change to the test.
    /// </summary>
    private sealed class ThrowAfterNWritesExecutionService(
        ISqlExecutionService inner, int throwOnWriteNumber, SqlException toThrow) : ISqlExecutionService
    {
        private int _writes;

        private void CountOrThrow()
        {
            if (Interlocked.Increment(ref _writes) == throwOnWriteNumber)
            {
                throw toThrow;
            }
        }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            CountOrThrow();
            return inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(
                tenantId,
                (context, ct) => work(new CountingTransactionContext(context, CountOrThrow), ct),
                cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(
                tenantId,
                (context, ct) => work(new CountingTransactionContext(context, CountOrThrow), ct),
                cancellationToken);

        private sealed class CountingTransactionContext(ISqlTransactionContext inner, Action countOrThrow)
            : ISqlTransactionContext
        {
            public Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
            {
                countOrThrow();
                return inner.ExecuteNonQueryAsync(command, cancellationToken);
            }

            public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
                SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken)
                => inner.ExecuteReaderAsync(command, readRow, cancellationToken);
        }
    }
}
