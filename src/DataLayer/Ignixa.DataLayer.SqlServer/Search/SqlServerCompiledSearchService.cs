using System.Data;
using System.Runtime.CompilerServices;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Search;

/// <summary>
/// ISearchService implementation driving Ignixa.Search.Sql's two-phase compiler (CreatePlanFromOptionsAsync
/// then SearchPlan.Compile) directly against the SqlServer-native schema. Mirrors
/// SqlEntityFrameworkSearchService's public contract exactly
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

    // Built once and reused: the compiler is stateless past construction, and this service only ever enters
    // it through the SearchOptions path -- the caller upstream has already built a SearchOptions, so no
    // ISearchOptionsBuilder is needed and none is supplied.
    private readonly ISearchSqlCompiler _compiler = new SearchSqlCompiler(
        symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver)),
        optionsBuilder: null,
        compartmentDefinitionManager ?? throw new ArgumentNullException(nameof(compartmentDefinitionManager)),
        searchParameterDefinitionManager ?? throw new ArgumentNullException(nameof(searchParameterDefinitionManager)));

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

        var compiled = await CompileAsync(options, cancellationToken, countOnly: true);

        // CA2100 suppressed: compiled.Sql is Ignixa.Search.Sql's own compiler output -- every user-controlled
        // value in it is already a named @pN parameter (compiled.Parameters, bound below via BindParameters),
        // never string-concatenated. Same rationale as ExecuteAndMaterializeAsync's identical suppression.
#pragma warning disable CA2100
        using var command = new SqlCommand(compiled.Sql);
#pragma warning restore CA2100
        BindParameters(command, compiled.Parameters);
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
            var compiled = await CompileAsync(options, cancellationToken);

            await foreach (var result in ExecuteAndMaterializeAsync(compiled, cancellationToken))
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

        var valuedCompiled = await CompileAsync(
            options, cancellationToken, sortPhase: SortPhase.Valued,
            offsetPageOverride: new OffsetSpec(requestedOffset, requestedCount));

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
        await foreach (var result in ExecuteAndMaterializeAsync(valuedCompiled, cancellationToken))
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
            var valuedCountCompiled = await CompileAsync(
                options, cancellationToken, countOnly: true, countPhaseScoped: true, sortPhase: SortPhase.Valued);

            // CA2100 suppressed: valuedCountCompiled.Sql is Ignixa.Search.Sql's own compiler output -- every
            // user-controlled value in it is already a named @pN parameter (valuedCountCompiled.Parameters,
            // bound below via BindParameters), never string-concatenated. Same rationale as CountAsync's
            // identical suppression.
#pragma warning disable CA2100
            using var countCommand = new SqlCommand(valuedCountCompiled.Sql);
#pragma warning restore CA2100
            BindParameters(countCommand, valuedCountCompiled.Parameters);
            var countRows = await _sqlExecutionService.ExecuteReaderAsync(
                _tenantId, countCommand, reader => reader.GetInt64(0), cancellationToken);
            var valuedTotal = checked((int)(countRows.Count > 0 ? countRows[0] : 0L));

            missingPrimaryOffset = Math.Max(0, requestedOffset - valuedTotal);
        }

        var missingPrimaryLimit = requestedCount - valuedCount;
        var missingCompiled = await CompileAsync(
            options, cancellationToken, sortPhase: SortPhase.MissingPrimary,
            offsetPageOverride: new OffsetSpec(missingPrimaryOffset, missingPrimaryLimit));

        var missingResults = new List<SearchEntryResult>();
        await foreach (var result in ExecuteAndMaterializeAsync(missingCompiled, cancellationToken))
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
    /// final, duplicate-free page, preserving true global stream order -- a requirement the Application
    /// layer's StreamingBundleSerializer.SerializeWithPaginationAsync's offset-arithmetic pagination
    /// depends on (it trusts stream order alone to know which row sits at the global "+1-for-hasMore"
    /// sentinel position; see this method's only caller for the full cross-phase collision scenario this
    /// resolves). A single in-place "promote this Include entry to Match" mutation
    /// is NOT sufficient: it keeps the promoted entry at whatever position its (wrong-phase) Include
    /// occurrence happened to hold, which can be far earlier than its true position among the OTHER
    /// phase's own matches -- corrupting the Match ordering the serializer relies on.
    ///
    /// Two-pass scan-then-emit instead: pass 1 determines each (ResourceType, ResourceId) identity's FINAL
    /// <see cref="SearchEntryMode"/> by scanning both lists once (Match if either phase records it as a
    /// genuine Match -- the two phases partition on sort-key presence/absence, so an identity can be a
    /// genuine Match in at most one of them). Pass 2 walks both lists again in their original order
    /// (Valued then MissingPrimary, each internally still in its own true sort order) and emits, for a
    /// Match-final identity, ONLY the occurrence that IS that Match (i.e. at the phase position where it
    /// was genuinely matched -- discarding any other phase's stray Include occurrence entirely rather than
    /// mutating it in place); for an Include-only identity, the first occurrence encountered. The result:
    /// merged Match entries appear in exactly the order they would have if only the genuinely-correct
    /// occurrences had been walked in the first place.
    /// </summary>
    private static IEnumerable<SearchEntryResult> MergeCrossPhaseResults(
        IReadOnlyList<SearchEntryResult> valuedResults,
        IReadOnlyList<SearchEntryResult> missingResults)
    {
        var matchIdentities = new HashSet<(string ResourceType, string ResourceId)>();
        foreach (var result in valuedResults)
        {
            if (result.SearchMode == SearchEntryMode.Match)
            {
                matchIdentities.Add((result.ResourceType, result.ResourceId));
            }
        }

        foreach (var result in missingResults)
        {
            if (result.SearchMode == SearchEntryMode.Match)
            {
                matchIdentities.Add((result.ResourceType, result.ResourceId));
            }
        }

        var emittedIdentities = new HashSet<(string ResourceType, string ResourceId)>();
        var merged = new List<SearchEntryResult>(valuedResults.Count + missingResults.Count);

        void EmitCanonicalOccurrence(SearchEntryResult result)
        {
            var identity = (result.ResourceType, result.ResourceId);

            // An identity known to be a genuine Match somewhere: only ITS OWN Match occurrence is
            // canonical -- any Include occurrence of the same identity (the other phase's own include
            // stage independently rediscovering it) is not the row this identity belongs at and is
            // discarded outright, never mutated in place.
            var isCanonicalOccurrence = !matchIdentities.Contains(identity) || result.SearchMode == SearchEntryMode.Match;
            if (isCanonicalOccurrence && emittedIdentities.Add(identity))
            {
                merged.Add(result);
            }
        }

        foreach (var result in valuedResults)
        {
            EmitCanonicalOccurrence(result);
        }

        foreach (var result in missingResults)
        {
            EmitCanonicalOccurrence(result);
        }

        return merged;
    }

    /// <summary>
    /// Runs both compiler phases for one execution shape and hands back the emitted statement. Plan creation
    /// is the async half (it reads storage symbols); <see cref="SearchPlan.Compile"/> is pure. Both halves
    /// report failure as data, and both are turned into the same <see cref="RequestNotValidException"/> the
    /// old single-call trace produced -- the caller sees one compile step, not two.
    /// </summary>
    private async Task<CompiledSearch> CompileAsync(
        SearchOptions options,
        CancellationToken cancellationToken,
        bool countOnly = false,
        bool countPhaseScoped = false,
        SortPhase sortPhase = SortPhase.Valued,
        OffsetSpec? offsetPageOverride = null)
    {
        // resourceType may legitimately be null/empty (a multi-type/system-level search) -- both
        // CreatePlanFromOptionsAsync and the underlying Lower.Run already support this via systemLevelSearch.
        var resourceType = options.ResourceType;

        (long Start, long End)? surrogateIdRange = options.StartSurrogateId.HasValue && options.EndSurrogateId.HasValue
            ? (options.StartSurrogateId.Value, options.EndSurrogateId.Value)
            : null;

        // Legacy has no hard cap on include results at all -- BuildIncludeQuery has no .Take/TOP of its own,
        // so there is no legacy default to literally mirror. Fall back to the primary page size when the
        // caller didn't specify one explicitly, rather than inventing an unrelated magic number.
        var includeLimit = options.IncludesMaxItemCount ?? options.MaxItemCount;

        var planOptions = new SearchPlanOptions
        {
            Shape = BuildResultShape(options, countOnly, countPhaseScoped, offsetPageOverride),
            SortPhase = sortPhase,
            IncludeLimit = includeLimit,
            SurrogateRange = surrogateIdRange,

            // Left at the default None. This is the live search path and nothing here reads a parameter
            // trace, a plan explain or a SQL text range, so paying for them on every request would be pure
            // overhead -- the old tracing entry point had no way to opt out and always ran the explainer and
            // emitted with IncludeTextRanges: true.
            DiagnosticsLevel = SearchDiagnosticsLevel.None,
        };

        var planResult = await _compiler.TryCreatePlanFromOptionsAsync(
            options, resourceType, planOptions, cancellationToken);
        if (!planResult.Succeeded)
        {
            throw new RequestNotValidException(planResult.Failure.Message);
        }

        var compilation = planResult.Plan.TryCompile();
        return compilation.Succeeded
            ? compilation.Compiled
            : throw new RequestNotValidException(compilation.Failure.Message);
    }

    /// <summary>
    /// Picks the terminal shape this compile emits. Count and paging are alternatives in the AST rather than
    /// independent flags, so "a count never pages" -- SqlEntityFrameworkSearchService.CountAsync ignores
    /// ContinuationToken/MaxItemCount entirely, every code path ending in a bare .CountAsync() with no
    /// Skip/Take -- is now structural instead of a conditional this adapter has to remember to honour.
    /// </summary>
    private static ResultShape BuildResultShape(
        SearchOptions options,
        bool countOnly,
        bool countPhaseScoped,
        OffsetSpec? offsetPageOverride)
    {
        if (countOnly)
        {
            return countPhaseScoped
                ? new ResultShape.Count.CurrentSortPhase()
                : new ResultShape.Count.AllMatches();
        }

        return new ResultShape.Matches(new SearchPaging.Offset(offsetPageOverride ?? DefaultOffsetPage(options)));
    }

    /// <summary>
    /// The page a search takes when the two-phase sort loop has not dictated one. Must match
    /// SqlEntityFrameworkSearchService.BuildQueryAsync's exact pagination convention:
    /// options.MaxItemCount arrives from the caller ALREADY "+1'd" for hasMore detection when there is no
    /// continuation token (the handler layer adds that +1 before building SearchOptions at all) -- so the
    /// no-token branch uses it as-is. A decoded continuation token, by contrast, stores the caller's
    /// ORIGINAL (non-+1'd) count, so THIS branch must add the +1 back explicitly, or every page after the
    /// first would come back one row short and the Application layer's hasMore detection would misfire.
    /// </summary>
    private static OffsetSpec DefaultOffsetPage(SearchOptions options)
        => !string.IsNullOrWhiteSpace(options.ContinuationToken)
            && ContinuationToken.TryDecode(options.ContinuationToken, out var tokenOffset, out var tokenCount)
                ? new OffsetSpec(tokenOffset, tokenCount + 1)
                : new OffsetSpec(0, options.MaxItemCount);

    private async IAsyncEnumerable<SearchEntryResult> ExecuteAndMaterializeAsync(
        CompiledSearch compiled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CA2100 suppressed: compiled.Sql is Ignixa.Search.Sql's own compiler output -- every user-controlled
        // value in it is already a named @pN parameter (compiled.Parameters, bound below via BindParameters),
        // never string-concatenated. Same rationale as CountAsync's identical suppression.
#pragma warning disable CA2100
        using var command = new SqlCommand(compiled.Sql);
#pragma warning restore CA2100
        BindParameters(command, compiled.Parameters);

        // The plan the SQL was emitted from, read straight off the compiled result rather than re-derived
        // from the caller's SearchOptions: Lower can drop a degenerate include stage, so options.Include
        // being non-empty does not imply the emitted statement carries an IsMatch column.
        var hasIncludes = compiled.Query.Includes is { Count: > 0 };

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
