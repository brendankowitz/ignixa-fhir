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

        var trace = await CompileAsync(options, cancellationToken);
        if (trace.Sql is not { } sql)
        {
            throw new RequestNotValidException(trace.Failure?.Message ?? "The search could not be compiled.");
        }

        // trace.CompiledPlan, not trace.Plan (the latter is QueryPlanTrace, a display-only projection with
        // no Includes/Sort structure of its own -- see SearchTrace.CompiledPlan's own remarks).
        await foreach (var result in ExecuteAndMaterializeAsync(sql, trace.CompiledPlan!, cancellationToken))
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

    private async Task<SearchTrace> CompileAsync(SearchOptions options, CancellationToken cancellationToken, bool countOnly = false)
    {
        // resourceType may legitimately be null/empty (a multi-type/system-level search) -- both
        // CompileFromOptionsAsync and the underlying Lower.Run already support this via systemLevelSearch.
        var resourceType = options.ResourceType;

        OffsetSpec? offsetPage = null;
        if (!countOnly)
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
        // Skip/Take), so this adapter matches that by leaving offsetPage null whenever countOnly is true.

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

        var surrogateIds = rows.Select(r => (r.ResourceTypeId, r.SurrogateId)).ToList();

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
