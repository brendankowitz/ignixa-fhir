// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Embeds a search's one semantic query term and rewrites the matching <see cref="VectorSearchExpression"/>
/// with the result, so the SQL compiler (Task 7) can lower it to a gated, distance-ranked plan. Registered
/// only when semantic search is enabled (see <see cref="SemanticSearchServiceRegistration.AddSemanticSearch"/>);
/// callers that only run with the feature on take this type as an optional constructor dependency, the
/// same pattern <see cref="SemanticIndexer"/> uses on the write path -- see
/// <see cref="Resource.SearchResourcesHandler"/>.
/// </summary>
/// <remarks>
/// The query text's embedding is cached in a dedicated <see cref="IMemoryCache"/> instance -- not the
/// shared process-wide cache -- owned by and disposed with
/// <see cref="SemanticSearchServiceRegistration.AddSemanticSearch"/>'s service registration. It is keyed
/// by model and verbatim text, not by tenant: an embedding is a pure function of the configured model and
/// the text, so it is safe -- and valuable -- to share across every tenant in the process. The cache is
/// this type's load-bearing answer to Review Focus #2 in the slice-1 plan: a non-deterministic provider
/// that returned a slightly different vector on every call would rank page 2 of a paged search by a
/// different query vector than page 1, silently skipping or repeating rows at the page seam. Caching for
/// <see cref="VectorSearchQueryOptions.EmbeddingCacheMinutes"/> (0 disables caching) means every page of
/// the same query, embedded within that window, reuses the exact same vector. Every entry is written with
/// <see cref="MemoryCacheEntryOptions.Size"/> = 1 so the cache's dedicated
/// <see cref="MemoryCacheOptions.SizeLimit"/> (<see cref="VectorSearchQueryOptions.EmbeddingCacheMaxEntries"/>,
/// 0 also disables caching) bounds it to that many distinct (model, text) embeddings regardless of how
/// long <see cref="VectorSearchQueryOptions.EmbeddingCacheMinutes"/> keeps entries alive, evicting the
/// least-recently-used entry rather than growing without bound or throwing once the limit is reached.
/// </remarks>
public sealed class SemanticQueryPreparer
{
    private const string CacheKeyPrefix = "semantic-query";

    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly SemanticTextChunker _chunker;
    private readonly IMemoryCache _cache;
    private readonly VectorSearchOptions _options;

    public SemanticQueryPreparer(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        SemanticTextChunker chunker,
        IMemoryCache cache,
        VectorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _generator = generator;
        _chunker = chunker;
        _cache = cache;
        _options = options;
    }

    /// <summary>
    /// Returns <paramref name="options"/> unchanged when its expression carries no
    /// <see cref="VectorSearchExpression"/> (no generator call). Otherwise embeds the one semantic query's
    /// text and returns a copy of <paramref name="options"/> (see the <see cref="SearchOptions"/> copy
    /// constructor) whose expression has that node replaced via <see cref="VectorSearchExpression.WithPrepared"/>.
    /// </summary>
    /// <exception cref="InvalidSearchOperationException">
    /// More than one <see cref="VectorSearchExpression"/> is present: the result is ranked by one distance,
    /// so two semantic conditions have no single order to rank by. Also thrown when the one semantic
    /// query's text exceeds the embedding provider's 8192-token per-input limit: unlike the write path,
    /// which chunks text before embedding, a query is embedded verbatim and cannot be chunked without
    /// changing what it ranks by, so an oversized query is rejected as a client error rather than
    /// truncated or sent to the provider to fail there.
    /// </exception>
    /// <exception cref="EmbeddingProviderContractException">
    /// The embedding provider's response did not match the request (wrong count or wrong vector
    /// dimensionality).
    /// </exception>
    /// <exception cref="EmbeddingUnavailableException">
    /// The embedding provider could not be reached, or failed with a transport/SDK-level or
    /// provider-raised timeout failure.
    /// </exception>
    public async Task<SearchOptions> PrepareAsync(SearchOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var found = VectorSearchExpressionLocator.FindAll(options.Expression);
        if (found.Count == 0)
        {
            return options;
        }

        if (found.Count > 1)
        {
            throw new InvalidSearchOperationException(
                "Only one semantic search parameter may be specified per search.");
        }

        var target = found[0];

        // Checked here, before the one provider call below, rather than left to
        // TokenBudgetBatchingEmbeddingGenerator's own per-input guard: that guard reports
        // InvalidOperationException (an internal invariant failure -- see its remarks), which is the
        // wrong shape for a query text the caller supplied. A query cannot be chunked like write-path
        // text (chunking would rank by a fragment of what the caller asked for), so "too long" is this
        // request's fault, not the provider's or this server's.
        var tokenCount = _chunker.CountTokens(target.QueryText);
        if (tokenCount > VectorSearchOptions.MaxEmbeddingInputTokens)
        {
            throw new InvalidSearchOperationException(
                $"Semantic query text exceeds {VectorSearchOptions.MaxEmbeddingInputTokens} tokens.");
        }

        var embedding = await GetEmbeddingAsync(target.QueryText, cancellationToken).ConfigureAwait(false);

        // VectorConfig is non-null by construction: VectorSearchExpression is only ever created (by
        // SearchExpressionBinder's semantic parsing path) for a SearchParameterInfo.IsSemantic parameter,
        // which requires VectorConfig to be set.
        var minimumScore = (double)target.Parameter.VectorConfig!.MinimumScore;
        var maxDistance = 2 * (1 - minimumScore);
        var prepared = new PreparedVectorQuery(embedding, _options.EmbeddingModelKey, maxDistance);

        var rewriter = new PreparedReplacingRewriter(target, prepared);
        var rewritten = options.Expression!.AcceptVisitor(rewriter, null);

        return new SearchOptions(options) { Expression = rewritten };
    }

    private async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string queryText, CancellationToken cancellationToken)
    {
        var cacheKey = (CacheKeyPrefix, _options.EmbeddingModelKey, queryText);
        if (_cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
        {
            return cached;
        }

        var embedding = await GenerateAsync(queryText, cancellationToken).ConfigureAwait(false);

        // EmbeddingCacheMaxEntries = 0 disables caching, same as EmbeddingCacheMinutes = 0: skip the Set
        // entirely rather than writing an entry with Size = 1 against a SizeLimit = 0 cache, which would
        // never be retrievable anyway. Size = 1 on every entry is what lets the dedicated cache's
        // SizeLimit (see this type's remarks) bound entry count without throwing: a cache entry written
        // without a Size against a cache that has SizeLimit set throws InvalidOperationException.
        if (_options.Query.EmbeddingCacheMinutes > 0 && _options.Query.EmbeddingCacheMaxEntries > 0)
        {
            _cache.Set(
                cacheKey,
                embedding,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.Query.EmbeddingCacheMinutes),
                });
        }

        return embedding;
    }

    private async Task<ReadOnlyMemory<float>> GenerateAsync(string queryText, CancellationToken cancellationToken)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings;
        try
        {
            embeddings = await _generator.GenerateAsync([queryText], cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (EmbeddingProviderFailureClassifier.IsProviderUnavailable(ex, cancellationToken))
        {
            throw new EmbeddingUnavailableException("The embedding provider is unavailable.", ex);
        }

        if (embeddings.Count != 1)
        {
            throw new EmbeddingProviderContractException(
                $"The embedding provider returned {embeddings.Count} embeddings for 1 query text.");
        }

        var vector = embeddings[0].Vector;
        if (vector.Length != VectorSearchOptions.SupportedDimensions)
        {
            throw new EmbeddingProviderContractException(
                $"The embedding provider returned a vector with {vector.Length} dimensions; " +
                $"expected {VectorSearchOptions.SupportedDimensions}.");
        }

        return vector;
    }

    /// <summary>
    /// Rewrites exactly the one <see cref="VectorSearchExpression"/> instance <see cref="PrepareAsync"/>
    /// already validated is the search's only one, leaving every other node untouched. Matches by
    /// reference, not by value: <see cref="VectorSearchExpression.ValueInsensitiveEquals"/> treats two
    /// nodes for the same parameter as equal regardless of query text, which is the wrong comparison here.
    /// </summary>
    private sealed class PreparedReplacingRewriter(VectorSearchExpression target, PreparedVectorQuery prepared)
        : ExpressionRewriter<object?>
    {
        public override Expression VisitVectorSearch(VectorSearchExpression expression, object? context)
            => ReferenceEquals(expression, target) ? expression.WithPrepared(prepared) : expression;
    }
}
