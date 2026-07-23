using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using SqlServerGzipResourceCompressor = Ignixa.DataLayer.SqlServer.Compression.GzipResourceCompressor;

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
    private readonly GzipResourceCompressor _compressor;
    private readonly SqlServerSearchIndexReferenceDataCache _newSearchCache;

    private DifferentialTestHarness(
        TestTenantDatabase legacyDatabase,
        TestTenantDatabase newDatabase,
        FhirDbContext legacyRepositoryDbContext,
        FhirDbContext legacyCacheDbContext,
        SearchIndexReferenceDataCache legacyCache,
        GzipResourceCompressor compressor,
        IFhirRepository legacyRepository,
        SqlMergeRepository legacyMergeRepository,
        ISearchService legacySearchService,
        SqlServerSearchIndexReferenceDataCache newSearchCache,
        ISearchService newSearchService)
    {
        _legacyDatabase = legacyDatabase;
        _newDatabase = newDatabase;
        _legacyRepositoryDbContext = legacyRepositoryDbContext;
        _legacyCacheDbContext = legacyCacheDbContext;
        _legacyCache = legacyCache;
        _compressor = compressor;
        LegacyRepository = legacyRepository;
        LegacyMergeRepository = legacyMergeRepository;
        NewRepository = newDatabase.Repository;
        NewMergeRepository = newDatabase.MergeRepository;
        LegacySearchService = legacySearchService;
        _newSearchCache = newSearchCache;
        NewSearchService = newSearchService;
    }

    /// <summary>The real EF-based repository, wired to database A ("legacy").</summary>
    public IFhirRepository LegacyRepository { get; }

    /// <summary>
    /// The new port, wired to database B ("new"). Set by <see cref="CreateAsync"/> via
    /// <see cref="TestTenantDatabase.CreateSqlServerFhirRepositoryAsync"/> (Task 6).
    /// </summary>
    public IFhirRepository NewRepository { get; }

    /// <summary>
    /// The same <see cref="SqlMergeRepository"/> instance <see cref="LegacyRepository"/> was
    /// constructed with -- for tests that need to force a SQL-level condition deterministically
    /// (e.g. error 50409), bypassing IFhirRepository's own client-side version lookup.
    /// </summary>
    public SqlMergeRepository LegacyMergeRepository { get; }

    /// <summary>
    /// Same as <see cref="LegacyMergeRepository"/>, for the new port. Set by <see cref="CreateAsync"/>
    /// via <see cref="TestTenantDatabase.CreateSqlServerFhirRepositoryAsync"/> (Task 6).
    /// </summary>
    public SqlServerMergeRepository NewMergeRepository { get; }

    /// <summary>
    /// The real EF-based search service, wired to database A ("legacy") -- mirrors
    /// <c>SqlEntityFrameworkRepositoryFactory.CreateServiceFactory</c>'s <c>createSearchService</c>
    /// closure's production wiring exactly (same generator/processor chain feeding
    /// <see cref="SqlEntityFrameworkSearchService"/>).
    /// </summary>
    public ISearchService LegacySearchService { get; }

    /// <summary>
    /// The compiler-driven <see cref="SqlServerCompiledSearchService"/>, wired to database B ("new").
    /// Mirrors <c>SqlServerCompiledSearchServiceTests.InitializeAsync</c>'s construction pattern.
    /// </summary>
    public ISearchService NewSearchService { get; }

    /// <summary>
    /// Provisions two independent, throwaway tenant databases and wires <see cref="LegacyRepository"/>
    /// / <see cref="LegacyMergeRepository"/> against database A, matching
    /// <c>SqlEntityFrameworkRepositoryFactory.CreateServiceFactory</c>'s production wiring.
    /// </summary>
    public static async Task<DifferentialTestHarness> CreateAsync(CancellationToken cancellationToken)
    {
        var legacyDatabaseTask = TestTenantDatabase.CreateEmptyAsync(cancellationToken);
        var newDatabaseTask = TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
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

        // Definition managers are pure, I/O-free lookup structures over the pre-generated R4 catalog
        // (matches every FhirVersion.R4-hardcoded fixture elsewhere in this project, e.g.
        // SqlServerCompiledSearchServiceTests.cs) -- safe to share one instance across both search
        // services below, exactly as SqlEntityFrameworkRepositoryFactory.GetOrCreateDefinitionManagers
        // shares its cached pair across every tenant of the same FHIR version.
        var compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);

        // Mirrors SqlEntityFrameworkRepositoryFactory.CreateServiceFactory's createSearchService closure
        // exactly (same generator chain feeding SqlEntityFrameworkSearchService), constructed here against
        // database A's own DbContext/repository/cache instead of a per-request closure.
        var compositeQueryGenerator = new CompositeSearchParameterQueryGenerator(
            legacyRepositoryDbContext, legacyCache, NullLogger<CompositeSearchParameterQueryGenerator>.Instance);
        var parameterQueryGenerator = new SearchParameterQueryGenerator(
            legacyRepositoryDbContext, legacyCache, NullLogger<SearchParameterQueryGenerator>.Instance, compositeQueryGenerator);
        var chainedExpressionProcessor = new ChainedExpressionProcessor(
            legacyRepositoryDbContext, legacyCache, parameterQueryGenerator, NullLogger<ChainedExpressionProcessor>.Instance);
        var compartmentQueryGenerator = new CompartmentSearchQueryGenerator(
            legacyRepositoryDbContext, legacyCache, compartmentDefinitionManager, searchParameterDefinitionManager,
            NullLogger<CompartmentSearchQueryGenerator>.Instance);
        var patientEverythingQueryGenerator = new PatientEverythingQueryGenerator(
            legacyRepositoryDbContext, compartmentQueryGenerator, NullLogger<PatientEverythingQueryGenerator>.Instance);
        var legacyQueryBuilder = new SearchExpressionQueryBuilder(
            legacyRepositoryDbContext,
            parameterQueryGenerator,
            chainedExpressionProcessor,
            compartmentQueryGenerator,
            patientEverythingQueryGenerator,
            searchParameterDefinitionManager,
            NullLogger<SearchExpressionQueryBuilder>.Instance);
        var legacyIncludeProcessor = new IncludeProcessor(
            legacyRepositoryDbContext, legacyCache, compressor, NullLogger<IncludeProcessor>.Instance);
        var legacyRevIncludeProcessor = new RevIncludeProcessor(
            legacyRepositoryDbContext, legacyCache, compressor, NullLogger<RevIncludeProcessor>.Instance);
        var legacyIterateProcessor = new IterateProcessor(
            legacyIncludeProcessor, legacyRevIncludeProcessor, NullLogger<IterateProcessor>.Instance);

        var legacySearchService = new SqlEntityFrameworkSearchService(
            legacyRepositoryDbContext,
            legacyRepository,
            legacyQueryBuilder,
            legacyIncludeProcessor,
            legacyRevIncludeProcessor,
            legacyIterateProcessor,
            compressor,
            legacyCache,
            NullLogger<SqlEntityFrameworkSearchService>.Instance);

        // Mirrors SqlServerCompiledSearchServiceTests.InitializeAsync's construction exactly, against
        // database B -- a search-side reference-data cache distinct from newDatabase's own internal one
        // (private, write-path-only), matching that test's identical rationale for a separate instance.
        var newSearchCache = new SqlServerSearchIndexReferenceDataCache(
            newDatabase.SqlExecutionService, newDatabase.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await newSearchCache.PreloadResourceTypesAsync(cancellationToken);
        var newSymbolResolver = new SqlServerSymbolResolver(newSearchCache);
        var newCompressor = new SqlServerGzipResourceCompressor(new RecyclableMemoryStreamManager());

        var newSearchService = new SqlServerCompiledSearchService(
            newDatabase.SqlExecutionService,
            newDatabase.TenantId,
            newSymbolResolver,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            newCompressor,
            NullLogger.Instance);

        return new DifferentialTestHarness(
            legacyDatabase,
            newDatabase,
            legacyRepositoryDbContext,
            legacyCacheDbContext,
            legacyCache,
            compressor,
            legacyRepository,
            legacyMergeRepository,
            legacySearchService,
            newSearchCache,
            newSearchService);
    }

    /// <summary>
    /// Seeds an identical dbo.SearchParam catalog row -- the same literal INSERT statement -- into BOTH
    /// database A (legacy) and database B (new). Both write paths' RowGenerators (see
    /// SearchParameterIdLookupHelper.TryGetSearchParamId, present verbatim in both DataLayer projects) and
    /// both search paths' symbol resolution (Resolve.RunAsync's per-parameter GetSearchParamIdAsync;
    /// SearchExpressionQueryBuilder's equivalent) silently treat an unseeded URL as "not found" -- an empty
    /// result on the compiled side, a compile failure via Resolve's Unresolved list on the other -- rather
    /// than creating the catalog row on demand (dbo.SearchParam has no seed data of its own, matching
    /// dbo.ResourceType's on-demand-only story before Task 6, and dbo.SearchParam never got that same
    /// on-demand path -- see SqlServerCompiledSearchServiceSortTests.cs's identical manual-INSERT
    /// requirement). Every differential search test (Tasks 11-13) needs this before its first
    /// CreateOrUpdateAsync/SearchStreamAsync call for any parameter beyond the resource columns
    /// (_id/_type/_lastUpdated), so it lives on the shared harness rather than being duplicated per test.
    /// </summary>
    public async Task SeedSearchParameterCatalogAsync(IEnumerable<Uri> searchParameterUrls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchParameterUrls);

        foreach (var url in searchParameterUrls.Distinct())
        {
            await SeedSearchParameterOnAsync(_legacyDatabase, url, cancellationToken);
            await SeedSearchParameterOnAsync(_newDatabase, url, cancellationToken);
        }
    }

    private static async Task SeedSearchParameterOnAsync(TestTenantDatabase database, Uri url, CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES (@Uri, 'active', SYSDATETIMEOFFSET(), 0)");
        command.Parameters.Add("@Uri", System.Data.SqlDbType.VarChar).Value = url.ToString();
        await database.SqlExecutionService.ExecuteNonQueryAsync(database.TenantId, command, cancellationToken);
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

    /// <summary>
    /// Content-aware companion to <see cref="AssertEquivalent"/> for the one column that can never
    /// pass a byte-level comparison: <c>RawResource</c>. <c>CreateOrUpdateAsync</c> bakes
    /// <c>Meta.LastUpdated</c> (derived from each side's independently-allocated <c>TransactionId</c>)
    /// into the compressed JSON before storage, so the compressed bytes themselves can never
    /// byte-match between legacy and new -- that is why <c>RawResource</c> must still be passed to
    /// <see cref="AssertEquivalent"/>'s <c>ignoredColumns</c>. This method decompresses both sides,
    /// parses the JSON, strips ONLY <c>meta.lastUpdated</c> (the one field with a known, legitimate
    /// per-database divergence reason) from a copy of each, and asserts the remainder is deep-equal --
    /// so a real serialization/compression bug (wrong field, dropped property, encoding mismatch)
    /// still fails loudly instead of riding along inside a blanket-ignored column. Reusable by any
    /// future differential test comparing RawResource-bearing rows (Tasks 7, 9, 10).
    /// </summary>
    public void AssertResourceContentEquivalent(byte[] legacyRawResource, byte[] newRawResource)
    {
        ArgumentNullException.ThrowIfNull(legacyRawResource);
        ArgumentNullException.ThrowIfNull(newRawResource);

        var legacyContent = DecompressAndNormalizeResourceContent(legacyRawResource);
        var newContent = DecompressAndNormalizeResourceContent(newRawResource);

        if (!JsonNode.DeepEquals(legacyContent, newContent))
        {
            throw new ShouldAssertException(
                $"Differential content mismatch on RawResource (after stripping meta.lastUpdated): "
                + $"legacy='{legacyContent?.ToJsonString() ?? "<null>"}', new='{newContent?.ToJsonString() ?? "<null>"}'.");
        }
    }

    /// <summary>
    /// Pulls a compressed <c>byte[]</c> column back out of a <see cref="RowStateSnapshot"/> row for
    /// use with <see cref="AssertResourceContentEquivalent"/>. Snapshot rows normalize <c>byte[]</c>
    /// columns to hex strings (see <see cref="NormalizeValue"/>, needed for sorting/equality in
    /// <see cref="AssertEquivalent"/>), so this reverses that encoding rather than re-querying the
    /// database. Reusable by any future differential test (Tasks 7, 9, 10).
    /// </summary>
    public static byte[] ExtractRawResourceBytes(RowStateSnapshot snapshot, int rowIndex = 0, string columnName = "RawResource")
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (rowIndex < 0 || rowIndex >= snapshot.Rows.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowIndex), rowIndex, $"Snapshot of table '{snapshot.TableName}' has {snapshot.Rows.Count} row(s).");
        }

        var row = snapshot.Rows[rowIndex];
        if (!row.TryGetValue(columnName, out var value) || value is not string hex)
        {
            throw new InvalidOperationException(
                $"Column '{columnName}' not found or not a byte[]-backed hex string in row {rowIndex} of table '{snapshot.TableName}'.");
        }

        return Convert.FromHexString(hex);
    }

    private JsonNode? DecompressAndNormalizeResourceContent(byte[] compressedBytes)
    {
        var jsonBytes = _compressor.DecompressBytes(compressedBytes);
        var reader = new Utf8JsonReader(jsonBytes.Span);
        var node = JsonNode.Parse(ref reader);

        if (node is JsonObject resourceObject
            && resourceObject.TryGetPropertyValue("meta", out var metaNode)
            && metaNode is JsonObject metaObject)
        {
            metaObject.Remove("lastUpdated");
        }

        return node;
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
        _newSearchCache.Dispose();
        await _legacyRepositoryDbContext.DisposeAsync();
        await _legacyCacheDbContext.DisposeAsync();
        await _legacyDatabase.DisposeAsync();
        await _newDatabase.DisposeAsync();
    }
}
