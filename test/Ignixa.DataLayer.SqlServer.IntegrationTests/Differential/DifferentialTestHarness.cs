using System.Globalization;
using System.Text;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

/// <summary>
/// Row-level differential-test harness comparing database state between the legacy EF-based
/// <see cref="SqlEntityFrameworkRepository"/> (database A / "legacy") and the new
/// <c>Ignixa.DataLayer.SqlServer</c> port (database B / "new", wired once Task 6 lands). Provisions
/// two independent, throwaway tenant databases via <see cref="TestTenantDatabase.CreateEmptyAsync"/>
/// so the two sides never share state.
///
/// Every consuming test MUST call <see cref="SnapshotLegacyAsync"/> and <see cref="SnapshotNewAsync"/>
/// explicitly -- never call one of them twice with identical arguments to "compare both sides", since
/// each targets exactly one database. A single ambiguous snapshot method was deliberately rejected
/// during plan review: it could not target one database over the other, so a differential comparison
/// built on it would silently pass on every real divergence, including a genuinely broken port.
/// </summary>
public sealed class DifferentialTestHarness : IAsyncDisposable
{
    private readonly TestTenantDatabase _legacyDatabase;
    private readonly TestTenantDatabase _newDatabase;
    private readonly FhirDbContext _legacyRepositoryDbContext;
    private readonly FhirDbContext _legacyCacheDbContext;
    private readonly SearchIndexReferenceDataCache _legacyCache;

    private DifferentialTestHarness(
        TestTenantDatabase legacyDatabase,
        TestTenantDatabase newDatabase,
        FhirDbContext legacyRepositoryDbContext,
        FhirDbContext legacyCacheDbContext,
        SearchIndexReferenceDataCache legacyCache,
        IFhirRepository legacyRepository,
        SqlMergeRepository legacyMergeRepository)
    {
        _legacyDatabase = legacyDatabase;
        _newDatabase = newDatabase;
        _legacyRepositoryDbContext = legacyRepositoryDbContext;
        _legacyCacheDbContext = legacyCacheDbContext;
        _legacyCache = legacyCache;
        LegacyRepository = legacyRepository;
        LegacyMergeRepository = legacyMergeRepository;
    }

    /// <summary>The real EF-based repository, wired to database A ("legacy").</summary>
    public IFhirRepository LegacyRepository { get; }

    /// <summary>
    /// The new port, wired to database B ("new"). Throws until Task 6 constructs the new port's
    /// <see cref="IFhirRepository"/> implementation.
    /// </summary>
    // CA1065 suppressed: the task brief's exact API mandates a get-only property (not a method) that
    // throws NotImplementedException until Task 6 wires this member -- a deliberate, documented,
    // temporary placeholder shape, not an accidental design mistake.
#pragma warning disable CA1065
    public IFhirRepository NewRepository =>
        throw new NotImplementedException(
            "DifferentialTestHarness.NewRepository is not wired yet -- it is set once Task 6 builds the new port's IFhirRepository implementation.");
#pragma warning restore CA1065

    /// <summary>
    /// The same <see cref="SqlMergeRepository"/> instance <see cref="LegacyRepository"/> was
    /// constructed with -- for tests that need to force a SQL-level condition deterministically
    /// (e.g. error 50409), bypassing IFhirRepository's own client-side version lookup.
    /// </summary>
    public SqlMergeRepository LegacyMergeRepository { get; }

    /// <summary>
    /// Same as <see cref="LegacyMergeRepository"/>, for the new port. Throws until Task 6 constructs
    /// the new port's <c>SqlServerMergeRepository</c>.
    /// </summary>
    // CA1065 suppressed: same rationale as NewRepository above -- exact API mandates a get-only
    // property, deliberately throwing NotImplementedException until Task 6 wires this member.
#pragma warning disable CA1065
    public SqlServerMergeRepository NewMergeRepository =>
        throw new NotImplementedException(
            "DifferentialTestHarness.NewMergeRepository is not wired yet -- it is set once Task 6 constructs the new port's SqlServerMergeRepository.");
#pragma warning restore CA1065

    /// <summary>
    /// Provisions two independent, throwaway tenant databases and wires <see cref="LegacyRepository"/>
    /// / <see cref="LegacyMergeRepository"/> against database A, matching
    /// <c>SqlEntityFrameworkRepositoryFactory.CreateServiceFactory</c>'s production wiring.
    /// </summary>
    public static async Task<DifferentialTestHarness> CreateAsync(CancellationToken cancellationToken)
    {
        var legacyDatabaseTask = TestTenantDatabase.CreateEmptyAsync(cancellationToken);
        var newDatabaseTask = TestTenantDatabase.CreateEmptyAsync(cancellationToken);
        await Task.WhenAll(legacyDatabaseTask, newDatabaseTask);

        var legacyDatabase = await legacyDatabaseTask;
        var newDatabase = await newDatabaseTask;

        var dbContextOptions = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(
                legacyDatabase.ConnectionString,
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                    sqlOptions.CommandTimeout(30);
                })
            .Options;

        // Mirrors SqlEntityFrameworkRepositoryFactory's production wiring: the reference-data cache
        // owns a DbContext separate from the repository's own (see MultiTenantSearchIndexCache).
        var legacyCacheDbContext = new FhirDbContext(dbContextOptions);
        var legacyCache = new SearchIndexReferenceDataCache(legacyCacheDbContext, NullLogger<SearchIndexReferenceDataCache>.Instance);

        var legacyRepositoryDbContext = new FhirDbContext(dbContextOptions);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());

        var legacyMergeRepository = new SqlMergeRepository(
            legacyRepositoryDbContext,
            compressor,
            NullLogger<SqlMergeRepository>.Instance,
            legacyCache,
            NullLogger<PostMergeExtensionUpdater>.Instance);

        var legacyRepository = new SqlEntityFrameworkRepository(
            legacyRepositoryDbContext,
            compressor,
            legacyMergeRepository,
            legacyCache,
            NullLogger<SqlEntityFrameworkRepository>.Instance);

        return new DifferentialTestHarness(
            legacyDatabase,
            newDatabase,
            legacyRepositoryDbContext,
            legacyCacheDbContext,
            legacyCache,
            legacyRepository,
            legacyMergeRepository);
    }

    /// <summary>Dumps a table's rows from database A (legacy) ONLY via <c>SELECT * FROM tableName WHERE whereClause</c>.</summary>
    public Task<RowStateSnapshot> SnapshotLegacyAsync(string tableName, string whereClause, CancellationToken cancellationToken)
        => SnapshotAsync(_legacyDatabase, tableName, whereClause, cancellationToken);

    /// <summary>Dumps a table's rows from database B (new) ONLY via <c>SELECT * FROM tableName WHERE whereClause</c>.</summary>
    public Task<RowStateSnapshot> SnapshotNewAsync(string tableName, string whereClause, CancellationToken cancellationToken)
        => SnapshotAsync(_newDatabase, tableName, whereClause, cancellationToken);

    /// <summary>
    /// Test-only escape hatch: executes raw SQL directly against database B (new) ONLY, with no
    /// equivalent call against database A. Used exclusively to prove <see cref="AssertEquivalent"/>
    /// genuinely detects a real divergence, rather than comparing a dataset to itself.
    /// </summary>
    public async Task InsertIntoNewDatabaseOnlyForTestingAsync(string sql, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);

        // CA2100 suppressed: test-only raw-SQL escape hatch -- callers pass literal, test-controlled
        // SQL text, never untrusted input, matching TestTenantDatabase's identical rationale.
#pragma warning disable CA2100
        using var command = new SqlCommand(sql);
#pragma warning restore CA2100
        await _newDatabase.SqlExecutionService.ExecuteNonQueryAsync(_newDatabase.TenantId, command, cancellationToken);
    }

    /// <summary>
    /// Column-by-column row comparison. Compares row COUNT first (fail fast with a clear message),
    /// then compares every non-ignored column of every row for exact equality after normalization
    /// (see <see cref="NormalizeValue"/>). Rows are sorted by a normalized-value key before comparing
    /// since SQL gives no row-order guarantee. <paramref name="ignoredColumns"/> is a narrow escape
    /// hatch for genuinely nondeterministic identifier/timestamp columns -- never for silencing a
    /// real, unexplained mismatch.
    /// </summary>
    public void AssertEquivalent(RowStateSnapshot legacy, RowStateSnapshot @new, params string[] ignoredColumns)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(@new);

        if (legacy.Rows.Count != @new.Rows.Count)
        {
            throw new ShouldAssertException(
                $"Differential row count mismatch on table '{legacy.TableName}': legacy snapshot has {legacy.Rows.Count} row(s), new snapshot has {@new.Rows.Count} row(s).");
        }

        var ignored = new HashSet<string>(ignoredColumns, StringComparer.OrdinalIgnoreCase);
        var sortedLegacy = legacy.Rows.OrderBy(row => BuildSortKey(row, ignored), StringComparer.Ordinal).ToList();
        var sortedNew = @new.Rows.OrderBy(row => BuildSortKey(row, ignored), StringComparer.Ordinal).ToList();

        for (var rowIndex = 0; rowIndex < sortedLegacy.Count; rowIndex++)
        {
            AssertRowEquivalent(sortedLegacy[rowIndex], sortedNew[rowIndex], rowIndex, ignored, legacy.TableName);
        }
    }

    private static void AssertRowEquivalent(
        IReadOnlyDictionary<string, object?> legacyRow,
        IReadOnlyDictionary<string, object?> newRow,
        int rowIndex,
        IReadOnlySet<string> ignoredColumns,
        string tableName)
    {
        foreach (var columnName in legacyRow.Keys)
        {
            if (ignoredColumns.Contains(columnName))
            {
                continue;
            }

            if (!newRow.TryGetValue(columnName, out var newRawValue))
            {
                throw new ShouldAssertException(
                    $"Differential mismatch on table '{tableName}' at row {rowIndex}, column '{columnName}': present in legacy snapshot but missing from new snapshot.");
            }

            var legacyValue = NormalizeValue(legacyRow[columnName]);
            var newValue = NormalizeValue(newRawValue);

            if (!Equals(legacyValue, newValue))
            {
                throw new ShouldAssertException(
                    $"Differential mismatch on table '{tableName}' at row {rowIndex}, column '{columnName}': legacy='{legacyValue ?? "<null>"}', new='{newValue ?? "<null>"}'.");
            }
        }
    }

    private static async Task<RowStateSnapshot> SnapshotAsync(
        TestTenantDatabase database, string tableName, string whereClause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(whereClause);

        // CA2100 suppressed: test-only raw-SQL helper -- callers pass literal, test-controlled table
        // names and WHERE clauses, never untrusted input, matching TestTenantDatabase's identical
        // rationale. A generic column-name/value dictionary per row is required here (not a hardcoded
        // column list) since this must work across all 15 search-index tables plus
        // dbo.Resource/dbo.Transactions/dbo.ResourceTtl.
#pragma warning disable CA2100
        using var command = new SqlCommand($"SELECT * FROM {tableName} WHERE {whereClause}");
#pragma warning restore CA2100

        var rows = await database.SqlExecutionService.ExecuteReaderAsync(
            database.TenantId,
            command,
            ReadRow,
            cancellationToken);

        return new RowStateSnapshot(rows, tableName);
    }

    private static IReadOnlyDictionary<string, object?> ReadRow(SqlDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = NormalizeValue(reader.GetValue(i));
        }

        return row;
    }

    // byte[] normalizes to a hex string for both sorting and equality -- object.Equals on two
    // content-equal byte[] instances (e.g. RawResource) is reference equality, which is always false
    // even when the bytes match. DBNull.Value normalizes to CLR null uniformly, whether it comes from
    // a live SqlDataReader or a hand-built test snapshot.
    private static object? NormalizeValue(object? value) => value switch
    {
        null or DBNull => null,
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value,
    };

    // Stable, deterministic sort key built from every normalized, NON-ignored column value in a fixed
    // (alphabetical) column order -- SQL doesn't guarantee row order without ORDER BY, and TVP-based
    // bulk inserts don't guarantee insertion order either, so an unsorted comparison would spuriously
    // fail on correct data just because two result sets came back in different orders. Ignored columns
    // (e.g. ResourceSurrogateId, TransactionId) are excluded from the key: they hold independently
    // allocated, genuinely nondeterministic values on each side, so including them in a multi-row
    // snapshot can make legacy and new sort into different orders and pair up the wrong rows.
    private static string BuildSortKey(IReadOnlyDictionary<string, object?> row, IReadOnlySet<string> ignoredColumns)
    {
        var builder = new StringBuilder();
        foreach (var columnName in row.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (ignoredColumns.Contains(columnName))
            {
                continue;
            }

            var normalized = NormalizeValue(row[columnName]);
            var formatted = normalized is null ? "<NULL>" : Convert.ToString(normalized, CultureInfo.InvariantCulture);
            builder.Append(columnName).Append('=').Append(formatted).Append("|COL|");
        }

        return builder.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _legacyCache.Dispose();
        await _legacyRepositoryDbContext.DisposeAsync();
        await _legacyCacheDbContext.DisposeAsync();
        await _legacyDatabase.DisposeAsync();
        await _newDatabase.DisposeAsync();
    }
}
