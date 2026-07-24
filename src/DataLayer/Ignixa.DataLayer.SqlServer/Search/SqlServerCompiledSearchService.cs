using System.Data;
using System.Runtime.CompilerServices;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tracing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Search;

/// <summary>
/// ISearchService implementation driving Ignixa.Search.Sql's compiler (Resolve->Lower->Emit) directly
/// against the SqlServer-native schema. Mirrors SqlEntityFrameworkSearchService's public contract exactly
/// (both cast TSearchOptions to Ignixa.Search.Models.SearchOptions), but executes the compiled T-SQL via
/// ISqlExecutionService instead of EF Core LINQ. GetExportRangesAsync does not go through the compiler at
/// all -- it is a direct MIN/MAX/COUNT aggregation over dbo.Resource.
/// </summary>
public sealed class SqlServerCompiledSearchService(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    SqlServerSymbolResolver symbolResolver,
    ICompartmentDefinitionManager compartmentDefinitionManager,
    ISearchParameterDefinitionManager searchParameterDefinitionManager,
    GzipResourceCompressor compressor,
    ILogger logger) : ISearchService
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly SqlServerSymbolResolver _symbolResolver =
        symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
    private readonly ICompartmentDefinitionManager _compartmentDefinitionManager =
        compartmentDefinitionManager ?? throw new ArgumentNullException(nameof(compartmentDefinitionManager));
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager =
        searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _tenantId = tenantId;

    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        await foreach (var result in SearchStreamWithPhaseHandlingAsync(options, cancellationToken))
        {
            yield return result;
        }
    }

    public async ValueTask<int> CountAsync<TSearchOptions>(
        TSearchOptions searchOptions,
        CancellationToken cancellationToken = default)
        where TSearchOptions : class
    {
        if (searchOptions is not SearchOptions options)
        {
            throw new ArgumentException($"Search options must be of type {nameof(SearchOptions)}", nameof(searchOptions));
        }

        var trace = await CompileAsync(options, cancellationToken, countOnly: true);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        // CA2100 suppressed: sql.Sql is Ignixa.Search.Sql's own compiler output -- every user-controlled
        // value in it is already a named @pN parameter (sql.Parameters, bound below via BindParameters),
        // never string-concatenated. Same rationale as ExecuteAndMaterializeAsync's identical suppression.
#pragma warning disable CA2100
        using var command = new SqlCommand(sql.Sql);
#pragma warning restore CA2100
        BindParameters(command, sql.Parameters);
        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt64(0), cancellationToken);
        var count = rows.Count > 0 ? rows[0] : 0L;
        return checked((int)count);
    }

    /// <summary>
    /// Partitions a resource type's ResourceSurrogateId span into <paramref name="numberOfRanges"/>
    /// contiguous, exhaustive, non-overlapping ranges for parallel export workers. Mirrors
    /// SqlEntityFrameworkSearchService.GetExportRangesAsync's exact range-generation algorithm (single
    /// min/max/count aggregation, same loop shape), executed as raw T-SQL instead of EF Core LINQ.
    /// </summary>
    public async Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
        string resourceType,
        int numberOfRanges,
        CancellationToken cancellationToken = default)
    {
        var resourceTypeId = await _symbolResolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (resourceTypeId is null)
        {
            _logger.LogWarning("ResourceType not found: {ResourceType}", resourceType);
            return [];
        }

        using var command = new SqlCommand(
            "SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId), COUNT(*) " +
            "FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND IsHistory = 0 AND IsDeleted = 0");
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            reader => (MinId: reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                       MaxId: reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                       Count: reader.GetInt32(2)),
            cancellationToken);

        var stats = rows.Count > 0 ? rows[0] : (MinId: null, MaxId: null, Count: 0);
        if (stats.Count == 0 || stats.MinId is not { } minId || stats.MaxId is not { } maxId)
        {
            return [];
        }

        var rangeSize = (long)Math.Ceiling((double)(maxId - minId + 1) / numberOfRanges);
        var ranges = new List<(long, long)>();
        var currentStart = minId;

        for (var i = 0; i < numberOfRanges && currentStart <= maxId; i++)
        {
            var currentEnd = i == numberOfRanges - 1 ? maxId : Math.Min(currentStart + rangeSize - 1, maxId);
            ranges.Add((currentStart, currentEnd));
            currentStart = currentEnd + 1;
        }

        return ranges;
    }

    /// <summary>
    /// Drives the two-phase Valued/MissingPrimary sort executor loop (design doc §3's corrected formula):
    /// runs Valued at the requested offset/limit; a full page stops there; a short, non-empty Valued page
    /// runs MissingPrimary at offset 0 to fill the rest; a zero-row Valued page runs a countPhaseScoped
    /// CountOnly compile to learn the Valued total, then runs MissingPrimary at the offset that total
    /// implies. Applies to EVERY sorted search, including a token-less first page -- offset 0 is just
    /// OffsetSpec(0, Limit), not a special case that can skip this loop, or page 1 would silently omit
    /// every missing-value resource. Unsorted searches keep the single Valued-only compile (Valued is the
    /// SortPhase default and this loop has no meaning for keyset paging, which the compiler already
    /// handles correctly in one compile via its own boundary mechanism).
    /// </summary>
    private async IAsyncEnumerable<SearchEntryResult> SearchStreamWithPhaseHandlingAsync(
        SearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (options.Sort.Count == 0)
        {
            var trace = await CompileAsync(options, cancellationToken);
            if (trace.Sql is not { } sql)
            {
                throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
            }

            await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.CompiledPlan!, cancellationToken))
            {
                yield return result;
            }

            yield break;
        }

        int requestedOffset;
        int requestedCount;
        if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
            && ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount))
        {
            requestedOffset = tokenOffset;
            requestedCount = tokenCount + 1; // same +1-for-hasMore convention CompileAsync itself uses
        }
        else
        {
            requestedOffset = 0;
            requestedCount = options.MaxItemCount;
        }

        var valuedTrace = await CompileAsync(
            options, cancellationToken, sortPhase: SortPhase.Valued,
            offsetPageOverride: new OffsetSpec(requestedOffset, requestedCount));
        if (valuedTrace.Sql is not { } valuedSql)
        {
            throw new RequestNotValidException(valuedTrace.Failure?.Message ?? "The search could not be compiled.");
        }

        // Buffered, not streamed straight out: both phase compiles share the SAME options.Include/RevInclude
        // list, so each phase's own include stage is seeded ONLY from that phase's own match page (SqlBuilder's
        // "NOT EXISTS (... FROM MatchPage ...)" anti-join has no visibility into the OTHER phase's match set).
        // A resource genuinely matched in one phase can therefore resurface as an Include row in the other
        // phase's independent execution -- the only way to catch and correctly resolve that cross-phase
        // collision (see MergeCrossPhaseResults) is to see both phases' full result sets before committing to
        // what gets yielded, which means neither phase's rows can be handed to the caller until it's known
        // whether a second phase will even run.
        var valuedResults = new List<SearchEntryResult>();

        // Only count Match-mode rows toward the phase-boundary arithmetic below. An includes-bearing plan's
        // match-page CTE yields Match rows AND separately-unioned Include rows through the same reader --
        // the OFFSET/FETCH paging and every offset/limit computed in this method govern the MATCH set only.
        // Counting Include rows here would prematurely satisfy/shrink the page math on any sorted search
        // combined with _include/_revinclude, silently dropping MissingPrimary match rows that should have
        // been returned.
        var valuedCount = 0;
        await foreach (var result in ExecuteAndMaterializeAsync(valuedSql, valuedTrace.CompiledPlan!, cancellationToken))
        {
            if (result.SearchMode == SearchEntryMode.Match)
            {
                valuedCount++;
            }

            valuedResults.Add(result);
        }

        if (valuedCount >= requestedCount)
        {
            // Valued alone filled the whole page -- no MissingPrimary phase will run, so there is no
            // cross-phase collision possible and these rows can be handed out exactly as materialized.
            foreach (var result in valuedResults)
            {
                yield return result;
            }

            yield break;
        }

        // _id/_lastUpdated are resource-column sort keys (ResourceId/ResourceSurrogateId) -- both are
        // non-nullable resource columns, so a value is never "missing" for either, and
        // Lower.BuildSortSpec deliberately throws NotSupportedException if ever asked to compile a
        // MissingPrimary-phase plan for one of these two kinds (see its own doc comment: "neither has a
        // MissingPrimary segment"). A short Valued page for one of these keys means the data has simply
        // run out -- e.g. the last page of an offset-paged _sort=_id search landing exactly at the tail
        // of the result set -- not that a genuine MissingPrimary segment exists to look at. Without this
        // guard, that completely ordinary paging shape would attempt the MissingPrimary compile below,
        // which throws, surfacing as a 400 RequestNotValidException on a request that should just return
        // however many rows remain.
        var primarySortCode = options.Sort[0].Parameter.Code;
        if (primarySortCode is "_id" or "_lastUpdated")
        {
            // No MissingPrimary phase will run -- same "no cross-phase collision possible" reasoning as
            // the valuedCount >= requestedCount branch above, just reached via a different guard.
            foreach (var result in valuedResults)
            {
                yield return result;
            }

            yield break;
        }

        int missingPrimaryOffset;
        if (valuedCount > 0)
        {
            // A short, non-empty Valued page: the phase boundary is unambiguously inside this page.
            missingPrimaryOffset = 0;
        }
        else
        {
            // Valued returned ZERO rows: the offset landed at-or-past the Valued total, and the boundary's
            // exact location is ambiguous without asking -- learn it via a countPhaseScoped CountOnly compile
            // (Task 4/§3's mechanism, purpose-built for exactly this disambiguation).
            var valuedCountTrace = await CompileAsync(
                options, cancellationToken, countOnly: true, countPhaseScoped: true, sortPhase: SortPhase.Valued);
            if (valuedCountTrace.Sql is not { } valuedCountSql)
            {
                throw new RequestNotValidException(valuedCountTrace.Failure?.Message ?? "The search could not be compiled.");
            }

            // CA2100 suppressed: valuedCountSql.Sql is Ignixa.Search.Sql's own compiler output -- every
            // user-controlled value in it is already a named @pN parameter (valuedCountSql.Parameters,
            // bound below via BindParameters), never string-concatenated. Same rationale as CountAsync's
            // identical suppression.
#pragma warning disable CA2100
            using var countCommand = new SqlCommand(valuedCountSql.Sql);
#pragma warning restore CA2100
            BindParameters(countCommand, valuedCountSql.Parameters);
            var countRows = await _sqlExecutionService.ExecuteReaderAsync(
                _tenantId, countCommand, reader => reader.GetInt64(0), cancellationToken);
            var valuedTotal = checked((int)(countRows.Count > 0 ? countRows[0] : 0L));

            missingPrimaryOffset = Math.Max(0, requestedOffset - valuedTotal);
        }

        var missingPrimaryLimit = requestedCount - valuedCount;
        var missingTrace = await CompileAsync(
            options, cancellationToken, sortPhase: SortPhase.MissingPrimary,
            offsetPageOverride: new OffsetSpec(missingPrimaryOffset, missingPrimaryLimit));
        if (missingTrace.Sql is not { } missingSql)
        {
            throw new RequestNotValidException(missingTrace.Failure?.Message ?? "The search could not be compiled.");
        }

        var missingResults = new List<SearchEntryResult>();
        await foreach (var result in ExecuteAndMaterializeAsync(missingSql, missingTrace.CompiledPlan!, cancellationToken))
        {
            missingResults.Add(result);
        }

        foreach (var result in MergeCrossPhaseResults(valuedResults, missingResults))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Combines the Valued and MissingPrimary phases' independently-materialized result lists into the
    /// final, duplicate-free page: each (ResourceType, ResourceId) identity is yielded exactly once, in
    /// first-occurrence order (Valued's rows first, then MissingPrimary's), and if that identity occurs
    /// as a genuine Match in EITHER phase, the merged entry is a Match -- a resource that is a real primary
    /// match in one phase is never demoted to an Include just because the other phase's independent include
    /// stage also happened to pull it in (see this method's only caller for why the two phases' own
    /// per-execution <see cref="SearchEntryMode"/> assignments cannot be trusted to already resolve this
    /// on their own once both lists are combined).
    /// </summary>
    private static IEnumerable<SearchEntryResult> MergeCrossPhaseResults(
        IReadOnlyList<SearchEntryResult> valuedResults,
        IReadOnlyList<SearchEntryResult> missingResults)
    {
        var merged = new List<SearchEntryResult>(valuedResults.Count + missingResults.Count);
        var indexByIdentity = new Dictionary<(string ResourceType, string ResourceId), int>();

        void AddOrPromote(SearchEntryResult result)
        {
            var identity = (result.ResourceType, result.ResourceId);
            if (indexByIdentity.TryGetValue(identity, out var existingIndex))
            {
                if (result.SearchMode == SearchEntryMode.Match && merged[existingIndex].SearchMode != SearchEntryMode.Match)
                {
                    merged[existingIndex] = merged[existingIndex] with { SearchMode = SearchEntryMode.Match };
                }

                return;
            }

            indexByIdentity.Add(identity, merged.Count);
            merged.Add(result);
        }

        foreach (var result in valuedResults)
        {
            AddOrPromote(result);
        }

        foreach (var result in missingResults)
        {
            AddOrPromote(result);
        }

        return merged;
    }

    private async Task<SearchTrace> CompileAsync(
        SearchOptions options,
        CancellationToken cancellationToken,
        bool countOnly = false,
        bool countPhaseScoped = false,
        SortPhase sortPhase = SortPhase.Valued,
        OffsetSpec? offsetPageOverride = null)
    {
        // resourceType may legitimately be null/empty (a multi-type/system-level search) -- both
        // CompileFromOptionsAsync and the underlying Lower.Run already support this via systemLevelSearch.
        var resourceType = options.ResourceType;

        OffsetSpec? offsetPage = offsetPageOverride;
        if (offsetPage is null && !countOnly)
        {
            // Must match SqlEntityFrameworkSearchService.BuildQueryAsync's exact pagination convention:
            // options.MaxItemCount arrives from the caller ALREADY "+1'd" for hasMore detection when there is
            // no continuation token (the handler layer adds that +1 before building SearchOptions at all) --
            // so the no-token branch uses it as-is. A decoded continuation token, by contrast, stores the
            // caller's ORIGINAL (non-+1'd) count, so THIS branch must add the +1 back explicitly, or every
            // page after the first would come back one row short and the Application layer's hasMore
            // detection would misfire.
            if (!string.IsNullOrWhiteSpace(options.ContinuationToken)
                && ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount))
            {
                offsetPage = new OffsetSpec(tokenOffset, tokenCount + 1);
            }
            else
            {
                offsetPage = new OffsetSpec(0, options.MaxItemCount);
            }
        }
        // else: countOnly (CountAsync) never pages -- SqlEntityFrameworkSearchService.CountAsync ignores
        // ContinuationToken/MaxItemCount entirely (every code path ends in a bare .CountAsync() call with no
        // Skip/Take), so this adapter matches that by leaving offsetPage null whenever countOnly is true and
        // no offsetPageOverride (the two-phase loop's own explicit-offset bypass) was supplied.

        (long Start, long End)? surrogateIdRange = options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue
            ? (options.StartSurrogateId.Value, options.EndSurrogateId.Value)
            : null;

        // Legacy has no hard cap on include results at all -- BuildIncludeQuery has no .Take/TOP of its own,
        // so there is no legacy default to literally mirror. Fall back to the primary page size when the
        // caller didn't specify one explicitly, rather than inventing an unrelated magic number.
        var includeLimit = options.IncludesMaxItemCount ?? options.MaxItemCount;

        return await SearchCompiler.CompileFromOptionsAsync(
            options,
            resourceType,
            _symbolResolver,
            _compartmentDefinitionManager,
            _searchParameterDefinitionManager,
            timeProvider: null,
            offsetPage,
            surrogateIdRange,
            countOnly,
            includeLimit,
            countPhaseScoped,
            sortPhase,
            cancellationToken);
    }

    private async IAsyncEnumerable<SearchEntryResult> ExecuteAndMaterializeAsync(
        EmittedSqlTrace sql,
        QueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CA2100 suppressed: sql.Sql is Ignixa.Search.Sql's own compiler output -- every user-controlled
        // value in it is already a named @pN parameter (sql.Parameters, bound below via BindParameters),
        // never string-concatenated. Same rationale as CountAsync's identical suppression.
#pragma warning disable CA2100
        using var command = new SqlCommand(sql.Sql);
#pragma warning restore CA2100
        BindParameters(command, sql.Parameters);

        var hasIncludes = plan.Includes is { Count: > 0 };

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            reader => ReadMatchRow(reader, hasIncludes),
            cancellationToken);

        // Distinct: a resource can legitimately appear more than once in the raw row set when multiple
        // include/iterate stages independently resolve the same (ResourceTypeId, SurrogateId) -- e.g. a
        // multitype array reference matched by more than one join path. Without this, FetchResourcesAsync's
        // VALUES-table-constructor batch would carry duplicate keys, and the fetched.ToDictionary below
        // would throw ArgumentException on the second occurrence. A FHIR bundle should only ever contain
        // one entry per resource anyway, so collapsing to distinct identities here is correct, not just
        // crash-avoidance.
        var surrogateIds = rows.Select(r => (r.ResourceTypeId, r.SurrogateId)).Distinct().ToList();

        foreach (var batch in surrogateIds.Chunk(100))
        {
            var fetched = await FetchResourcesAsync(batch, cancellationToken);
            var fetchedById = fetched.ToDictionary(f => (f.ResourceTypeId, f.SurrogateId));

            foreach (var (resourceTypeId, surrogateId) in batch)
            {
                if (!fetchedById.TryGetValue((resourceTypeId, surrogateId), out var resource))
                {
                    _logger.LogWarning("Resource {ResourceTypeId}/{SurrogateId} matched the search but was not found on batch fetch -- likely deleted concurrently.", resourceTypeId, surrogateId);
                    continue;
                }

                var matchRow = rows.First(r => r.ResourceTypeId == resourceTypeId && r.SurrogateId == surrogateId);

                // Iterator methods cannot yield inside a try block that has a catch clause, so the
                // decompress-and-build step (the one piece of this that can actually throw, on a
                // malformed RawResource) is factored into TryBuildSearchEntryResult -- mirroring
                // SqlServerHistoryQueryExecutor.TryMapHistoryRow's try/catch-and-skip, just called from
                // outside the try rather than inside it.
                if (TryBuildSearchEntryResult(resource, matchRow.IsMatch) is { } result)
                {
                    yield return result;
                }
            }
        }
    }

    private SearchEntryResult? TryBuildSearchEntryResult(FetchedResource resource, bool? isMatch)
    {
        try
        {
            return new SearchEntryResult(
                ResourceType: resource.ResourceTypeName,
                ResourceId: resource.ResourceId,
                VersionId: resource.Version.ToString(),
                LastModified: resource.SurrogateId.ToDate(),
                ResourceBytes: _compressor.DecompressBytes(resource.RawResource))
            {
                IsDeleted = resource.IsDeleted,
                // IsMatch == false -> Include, IsMatch == true or null (a no-includes plan, where every
                // row is implicitly a match and the IsMatch column is absent) -> Match. Never derive this
                // from IsPartial, which is a truncation marker on included rows, not the include/match
                // discriminator.
                SearchMode = isMatch is false ? SearchEntryMode.Include : SearchEntryMode.Match,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize matched resource {ResourceType}/{ResourceId} version {Version}", resource.ResourceTypeName, resource.ResourceId, resource.Version);
            return null;
        }
    }

    /// <summary>
    /// Batch-fetches dbo.Resource rows (joined to dbo.ResourceType for the type name) by their exact
    /// (ResourceTypeId, ResourceSurrogateId) identity, one round trip per <paramref name="batch"/> (the
    /// caller chunks at 100 -- see ExecuteAndMaterializeAsync). Mirrors
    /// SqlServerFhirRepository.GetExistingResourceVersionsAsync's established VALUES-table-constructor
    /// join pattern (SQL Server has no tuple IN); <see cref="SqlDbType"/> binding matches
    /// SqlServerHistoryQueryExecutor's own convention.
    /// </summary>
    private async Task<IReadOnlyList<FetchedResource>> FetchResourcesAsync(
        IReadOnlyList<(short ResourceTypeId, long SurrogateId)> batch,
        CancellationToken cancellationToken)
    {
        var typeParamNames = new string[batch.Count];
        var sidParamNames = new string[batch.Count];
        var valuesParts = new string[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            typeParamNames[i] = $"@Type{i}";
            sidParamNames[i] = $"@Sid{i}";
            valuesParts[i] = $"({typeParamNames[i]}, {sidParamNames[i]})";
        }

        // CA2100 suppressed: the query text is built purely from a fixed sequence of numbered placeholders
        // bounded by this batch's own size (<= 100), with actual values always flowing through parameters --
        // same rationale as SqlServerFhirRepository.GetExistingResourceVersionsAsync's identical pattern.
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"""
            SELECT r.ResourceTypeId, r.ResourceSurrogateId, rt.Name, r.ResourceId, r.Version, r.RawResource, r.IsDeleted
            FROM (VALUES {string.Join(", ", valuesParts)}) AS k(TypeId, SurrogateId)
            INNER JOIN dbo.Resource r ON r.ResourceTypeId = k.TypeId AND r.ResourceSurrogateId = k.SurrogateId
            INNER JOIN dbo.ResourceType rt ON rt.ResourceTypeId = r.ResourceTypeId;
            """);
#pragma warning restore CA2100

        for (var i = 0; i < batch.Count; i++)
        {
            command.Parameters.Add(typeParamNames[i], SqlDbType.SmallInt).Value = batch[i].ResourceTypeId;
            command.Parameters.Add(sidParamNames[i], SqlDbType.BigInt).Value = batch[i].SurrogateId;
        }

        return await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, ReadResourceRow, cancellationToken);
    }

    private static FetchedResource ReadResourceRow(SqlDataReader reader) => new(
        reader.GetInt16(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt32(4),
        (byte[])reader[5],
        reader.GetBoolean(6));

    private static void BindParameters(SqlCommand command, IReadOnlyList<EmittedSqlParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static MatchRow ReadMatchRow(SqlDataReader reader, bool hasIncludes)
    {
        var resourceTypeId = reader.GetInt16(0);
        var surrogateId = reader.GetInt64(1);
        var isMatch = hasIncludes ? (bool?)reader.GetBoolean(2) : null;
        return new MatchRow(resourceTypeId, surrogateId, isMatch);
    }

    private readonly record struct MatchRow(short ResourceTypeId, long SurrogateId, bool? IsMatch);

    private readonly record struct FetchedResource(
        short ResourceTypeId,
        long SurrogateId,
        string ResourceTypeName,
        string ResourceId,
        int Version,
        byte[] RawResource,
        bool IsDeleted);
}
