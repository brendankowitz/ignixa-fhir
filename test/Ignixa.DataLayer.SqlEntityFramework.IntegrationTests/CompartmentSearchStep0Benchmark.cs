// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Step 0 proving-increment three-arm timing comparison for compartment search (see
/// <c>docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md</c> and
/// <c>.superpowers/sdd/task-4-brief.md</c>).
/// <list type="bullet">
/// <item>Arm A - current production <see cref="CompartmentSearchQueryGenerator"/>, unmodified.</item>
/// <item>Arm B - Arm A with <c>SearchParamId</c> also forced to <c>EF.Constant</c> (test-local one-line
/// variant of <c>CompartmentSearchQueryGenerator.cs:181-185</c>, not a fork of the whole file).</item>
/// <item>Arm C - the legacy hand-written SQL shape from <c>CompartmentSearchProblem.txt</c>
/// (CTE-per-<c>SearchParamId</c>, <c>SearchParamId</c> as a SQL literal), built dynamically from the
/// same real <c>searchParamMap</c> Arms A/B use.</item>
/// </list>
/// All three arms compare over the IDENTICAL <c>(SearchParamId, ResourceTypeIds)</c> set: Arms B and C
/// take <see cref="BuildRealSearchParamMapAsync"/>'s output, which reproduces
/// <see cref="CompartmentSearchQueryGenerator.GenerateCompartmentQueryAsync"/>'s own first-pass
/// resolution (real <see cref="ICompartmentDefinitionManager"/> + <see cref="ISearchParameterDefinitionManager"/>
/// + <see cref="SearchIndexReferenceDataCache"/>, not a separately-invented catalog).
/// </summary>
// CA1001 suppressed: _cache (SearchIndexReferenceDataCache) is IDisposable, but its Dispose() disposes
// the FhirDbContext it was constructed with - the caller of this class's constructor still owns and
// needs that context afterward, so this type deliberately does not dispose _cache. Same justification
// CompartmentDataSeeder.cs uses for the identical pattern.
#pragma warning disable CA1001
public sealed class CompartmentSearchStep0Benchmark
#pragma warning restore CA1001
{
    private const string CompartmentId = "step0-patient";

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ILogger<CompartmentSearchQueryGenerator> _logger;

    public CompartmentSearchStep0Benchmark(FhirDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Constructed exactly as production DI does - see SqlEntityFrameworkRepositoryFactory.cs's
        // GetOrCreateDefinitionManagers (compartment/parameter managers) and CreateServiceFactory
        // (searchIndexCache, compartmentQueryGenerator).
        _compartmentDefinitionManager = new CompartmentDefinitionManager(FhirVersion.R4);
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        _searchParameterDefinitionManager = new SearchParameterDefinitionManager(
            schemaProvider, NullLogger<SearchParameterDefinitionManager>.Instance);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);
        _logger = NullLogger<CompartmentSearchQueryGenerator>.Instance;
    }

    /// <summary>
    /// Arm A: calls the real production generator directly, unmodified.
    /// </summary>
    public async Task<List<long>> RunArmAAsync(CancellationToken ct)
    {
        var generator = new CompartmentSearchQueryGenerator(
            _context, _cache, _compartmentDefinitionManager, _searchParameterDefinitionManager, _logger);
        var query = await generator.GenerateCompartmentQueryAsync("Patient", CompartmentId, null, ct);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Arm B: same shape as Arm A, <c>SearchParamId</c> also forced to <c>EF.Constant</c> (the one line
    /// this arm exists to isolate - <c>CompartmentSearchQueryGenerator.cs:182</c>'s
    /// <c>refParam.SearchParamId == searchParamId</c> becomes <c>EF.Constant(searchParamId) == refParam.SearchParamId</c>).
    /// </summary>
    public async Task<List<long>> RunArmBAsync(
        Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)> searchParamMap,
        CancellationToken ct)
    {
        IQueryable<long>? unioned = null;
        foreach (var (_, (searchParamId, resourceTypeIds)) in searchParamMap)
        {
            var paramQuery = from refParam in _context.ReferenceSearchParams
                              where EF.Constant(searchParamId) == refParam.SearchParamId
                                  && refParam.ReferenceResourceId == CompartmentId
                                  && EF.Constant(resourceTypeIds.ToList()).Contains(refParam.ResourceTypeId)
                              select refParam.ResourceSurrogateId;
            unioned = unioned == null ? paramQuery : unioned.Union(paramQuery);
        }

        return unioned == null ? [] : await unioned.ToListAsync(ct);
    }

    /// <summary>
    /// Arm C: raw ADO.NET, CTE-per-<c>SearchParamId</c>, <c>SearchParamId</c> as a SQL literal (not a
    /// parameter) - matches the shape of the legacy hand-written SQL in <c>CompartmentSearchProblem.txt</c>.
    /// </summary>
    public async Task<List<long>> RunArmCAsync(
        Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)> searchParamMap,
        CancellationToken ct)
    {
        var cteParts = new List<string>();
        var i = 0;
        foreach (var (_, (searchParamId, resourceTypeIds)) in searchParamMap)
        {
            var typeList = string.Join(",", resourceTypeIds);
            cteParts.Add($"cte{i} AS (SELECT ResourceSurrogateId FROM dbo.ReferenceSearchParam " +
                          $"WHERE SearchParamId = {searchParamId} AND ReferenceResourceId = @compartmentId " +
                          $"AND ResourceTypeId IN ({typeList}))");
            i++;
        }

        var union = string.Join(" UNION ", Enumerable.Range(0, i).Select(n => $"SELECT ResourceSurrogateId FROM cte{n}"));
        var sql = $";WITH {string.Join(",", cteParts)} {union}";

        await using var connection = new SqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // CA2100 suppressed: this arm's entire purpose is reproducing the legacy hand-written SQL shape,
        // which literalizes SearchParamId (short) and ResourceTypeId (short) values directly rather than
        // parameterizing them (see task-4-brief.md's Arm C). Those values come from internal
        // short-typed collections resolved by BuildRealSearchParamMapAsync, not external/user input, so
        // there's no injection surface despite the literal SQL concatenation. compartmentId remains a
        // real SqlParameter.
#pragma warning disable CA2100
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("@compartmentId", CompartmentId);

        var results = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetInt64(0));
        }

        return results;
    }

    /// <summary>
    /// Reproduces <see cref="CompartmentSearchQueryGenerator.GenerateCompartmentQueryAsync"/>'s own
    /// first-pass resolution (that method's private lines 97-157: real
    /// <see cref="ICompartmentDefinitionManager.TryGetSearchParams"/> +
    /// <see cref="SearchIndexReferenceDataCache.GetSearchParamIdAsync(Ignixa.Search.Models.SearchParameterInfo)"/>),
    /// using the SAME real managers and cache instance Arm A uses, for
    /// <c>resourceTypesToSearch = null</c> (i.e. all resource types in the Patient compartment) - exactly
    /// what Arm A's own call passes. This guarantees Arms B and C compare over the identical
    /// <c>(SearchParamId, ResourceTypeIds)</c> set Arm A resolves internally, not a separately-invented map.
    /// </summary>
    public async Task<Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)>> BuildRealSearchParamMapAsync(CancellationToken ct)
    {
        var searchParamMap = new Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)>();

        if (!_compartmentDefinitionManager.TryGetResourceTypes(CompartmentType.Patient, out var allResourceTypes))
        {
            return searchParamMap;
        }

        var resourceTypeMap = await _context.ResourceTypes
            .AsNoTracking()
            .Where(rt => allResourceTypes.Contains(rt.Name))
            .ToDictionaryAsync(rt => rt.Name, rt => rt.ResourceTypeId, ct);

        foreach (var resourceType in allResourceTypes)
        {
            if (!_compartmentDefinitionManager.TryGetSearchParams(resourceType, CompartmentType.Patient, out var searchParams))
            {
                continue;
            }

            if (!resourceTypeMap.TryGetValue(resourceType, out var resourceTypeId))
            {
                continue;
            }

            foreach (var searchParamCode in searchParams)
            {
                try
                {
                    var searchParamInfo = _searchParameterDefinitionManager.GetSearchParameter(resourceType, searchParamCode);
                    var searchParamId = await _cache.GetSearchParamIdAsync(searchParamInfo, ct);
                    if (!searchParamId.HasValue)
                    {
                        continue;
                    }

                    var searchParamUri = searchParamInfo.Url.ToString();
                    if (!searchParamMap.ContainsKey(searchParamUri))
                    {
                        searchParamMap[searchParamUri] = (searchParamId.Value, new HashSet<short>());
                    }

                    searchParamMap[searchParamUri].resourceTypeIds.Add(resourceTypeId);
                }
                catch (Exception)
                {
                    // Mirrors CompartmentSearchQueryGenerator's own catch-and-skip for pairs that aren't
                    // real, resolvable search parameters (CompartmentSearchQueryGenerator.cs:147-155).
                }
            }
        }

        return searchParamMap;
    }
}

/// <summary>
/// Hosts the manual Step 0 experiment. Kept as a separate xunit test class (parameterless constructor)
/// from <see cref="CompartmentSearchStep0Benchmark"/> itself, which takes a <see cref="FhirDbContext"/>
/// constructor argument and is not xunit-instantiable directly.
/// </summary>
public class CompartmentSearchStep0BenchmarkTests
{
    private const int WarmRunCount = 3;

    [Fact(Skip = "Manual step 0 experiment - run explicitly, not part of CI")]
    public async Task Step0_ThreeArmComparison_RecordsElapsedTimes()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING not set");

        async Task<long> ClearPlanCacheAndTimeAsync(Func<Task> action)
        {
            await using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("DBCC FREEPROCCACHE;", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        async Task<long> TimeAsync(Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        async Task<(long ColdMs, long[] WarmMs)> MeasureArmAsync(Func<Task> action)
        {
            var cold = await ClearPlanCacheAndTimeAsync(action);
            var warm = new long[WarmRunCount];
            for (var i = 0; i < WarmRunCount; i++)
            {
                warm[i] = await TimeAsync(action);
            }

            return (cold, warm);
        }

        var options = new DbContextOptionsBuilder<FhirDbContext>().UseSqlServer(connectionString).Options;
        await using var context = new FhirDbContext(options);
        var benchmark = new CompartmentSearchStep0Benchmark(context);

        // Build the real searchParamMap once, shared by Arms B and C - the same resolution Arm A performs
        // internally on every call (real CompartmentDefinitionManager/SearchParameterDefinitionManager/
        // SearchIndexReferenceDataCache, see BuildRealSearchParamMapAsync's docs).
        var searchParamMap = await benchmark.BuildRealSearchParamMapAsync(CancellationToken.None);
        searchParamMap.Count.ShouldBeGreaterThan(1, "Step 0 acceptance gate: Arm A's real resolution must produce a multi-CTE map.");

        List<long>? armAResult = null;
        var armA = await MeasureArmAsync(async () => armAResult = await benchmark.RunArmAAsync(CancellationToken.None));

        List<long>? armBResult = null;
        var armB = await MeasureArmAsync(async () => armBResult = await benchmark.RunArmBAsync(searchParamMap, CancellationToken.None));

        List<long>? armCResult = null;
        var armC = await MeasureArmAsync(async () => armCResult = await benchmark.RunArmCAsync(searchParamMap, CancellationToken.None));

        // Step 0 acceptance gate: Arm A's exact call must return a non-empty result against the re-seeded
        // real-association data, not throw / not return an empty in-memory queryable.
        armAResult.ShouldNotBeNull();
        armAResult.Count.ShouldBeGreaterThan(0, "Step 0 acceptance gate: Arm A must return a non-empty result.");

        // Sanity: all three arms must agree on the result set size - they're logically equivalent queries
        // over the identical searchParamMap. A mismatch here would mean the arms aren't actually testing
        // the same thing, which is a finding to report, not to explain away.
        armBResult.ShouldNotBeNull();
        armCResult.ShouldNotBeNull();
        armBResult.Count.ShouldBe(armAResult.Count, "Arm B must return the same result count as Arm A.");
        armCResult.Count.ShouldBe(armAResult.Count, "Arm C must return the same result count as Arm A.");

        var report = BuildReport(searchParamMap.Count, armAResult.Count, armA, armB, armC);
        await AppendFindingsAsync(report);
    }

    private static string BuildReport(
        int cteCount,
        int resultCount,
        (long ColdMs, long[] WarmMs) armA,
        (long ColdMs, long[] WarmMs) armB,
        (long ColdMs, long[] WarmMs) armC)
    {
        static double Average(long[] values) => values.Average();
        static string FormatWarm(long[] values) => string.Join(", ", values) + $" (avg {Average(values):F1})";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Task 4: Three-Arm Timing Comparison (real Patient-compartment associations)");
        sb.AppendLine();
        sb.AppendLine($"Ran {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC against `CompartmentStep0`, compartment `step0-patient`.");
        sb.AppendLine($"`searchParamMap` resolved by the real `CompartmentDefinitionManager`/`SearchParameterDefinitionManager` ({cteCount} distinct SearchParamId CTEs); all three arms returned {resultCount} rows.");
        sb.AppendLine();
        sb.AppendLine("| Arm | Cold (ms, DBCC FREEPROCCACHE) | Warm x3 (ms) | Warm avg (ms) |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| A - production `CompartmentSearchQueryGenerator`, unmodified | {armA.ColdMs} | {string.Join(", ", armA.WarmMs)} | {Average(armA.WarmMs):F1} |");
        sb.AppendLine($"| B - Arm A + `SearchParamId` literalized via `EF.Constant` | {armB.ColdMs} | {string.Join(", ", armB.WarmMs)} | {Average(armB.WarmMs):F1} |");
        sb.AppendLine($"| C - legacy SQL shape (raw ADO.NET, `SearchParamId` as SQL literal) | {armC.ColdMs} | {string.Join(", ", armC.WarmMs)} | {Average(armC.WarmMs):F1} |");
        sb.AppendLine();
        sb.AppendLine("Raw warm-run detail:");
        sb.AppendLine($"- Arm A warm: {FormatWarm(armA.WarmMs)}");
        sb.AppendLine($"- Arm B warm: {FormatWarm(armB.WarmMs)}");
        sb.AppendLine($"- Arm C warm: {FormatWarm(armC.WarmMs)}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task AppendFindingsAsync(string report)
    {
        var findingsPath = Path.Combine(
            FindRepoRoot(),
            "docs", "features", "deployment", "investigations", "2026-07-15-compartment-search-step0-findings.md");
        await File.AppendAllTextAsync(findingsPath, report);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Path.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate repo root (.git) starting from {AppContext.BaseDirectory}");
    }
}
