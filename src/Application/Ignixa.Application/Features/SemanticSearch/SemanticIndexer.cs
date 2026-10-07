// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Microsoft.Extensions.AI;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Embeds the semantic text carried by <c>special</c> search parameters (see
/// <see cref="SearchParameterInfo.IsSemantic"/>) that <see cref="ISearchIndexer"/> already extracted
/// onto a resource's <see cref="ResourceWrapper.SearchIndices"/>, and attaches the resulting
/// <see cref="ResourceWrapper.VectorIndices"/>. Registered only when semantic search is enabled (see
/// <see cref="SemanticSearchServiceRegistration.AddSemanticSearch"/>); callers that only run with the
/// feature on take this type as an optional constructor dependency and skip indexing entirely when it is
/// not resolvable, leaving <see cref="ResourceWrapper.VectorIndices"/> null.
/// </summary>
public sealed class SemanticIndexer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly SemanticTextChunker _chunker;
    private readonly VectorSearchOptions _options;

    public SemanticIndexer(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        SemanticTextChunker chunker,
        VectorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(options);

        _generator = generator;
        _chunker = chunker;
        _options = options;
    }

    /// <summary>
    /// Computes <see cref="ResourceWrapper.VectorIndices"/> for every resource in <paramref name="resources"/>
    /// and returns the equivalent wrappers (same order, same count) with that property populated.
    /// </summary>
    /// <remarks>
    /// Every passage across every resource is embedded with exactly one
    /// <see cref="IEmbeddingGenerator{TInput, TEmbedding}.GenerateAsync"/> call (skipped entirely when no
    /// resource has semantic text), so callers that pass several resources at once -- a transaction's
    /// staged writes, or a batch bundle's micro-batch -- make one provider round trip for the whole group
    /// rather than one per resource.
    /// </remarks>
    /// <exception cref="SemanticSearchDefinitionException">
    /// A semantic search parameter's extracted value is not a string, its effective chunk/overlap
    /// configuration is invalid, or it produced more chunks than fit in <see cref="short"/>. All of these
    /// indicate a SearchParameter/configuration definition fault, not recoverable input data.
    /// </exception>
    /// <exception cref="EmbeddingProviderContractException">
    /// The embedding provider's response did not match the request (wrong count or wrong vector
    /// dimensionality).
    /// </exception>
    /// <exception cref="EmbeddingUnavailableException">
    /// The embedding provider could not be reached, or failed with a transport/SDK-level or
    /// provider-raised timeout failure.
    /// </exception>
    public async Task<IReadOnlyList<ResourceWrapper>> IndexAsync(
        IReadOnlyList<ResourceWrapper> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources.Count == 0)
        {
            return resources;
        }

        var passages = new List<string>();
        var plans = new List<PendingEntry>[resources.Count];

        for (var i = 0; i < resources.Count; i++)
        {
            plans[i] = resources[i].IsDeleted ? [] : BuildPlans(resources[i], passages);
        }

        IReadOnlyList<Embedding<float>>? embeddings = null;
        if (passages.Count > 0)
        {
            embeddings = await GenerateAsync(passages, cancellationToken).ConfigureAwait(false);
        }

        var results = new ResourceWrapper[resources.Count];
        for (var i = 0; i < resources.Count; i++)
        {
            var entries = plans[i].Count == 0
                ? (IReadOnlyList<VectorIndexEntry>)[]
                : plans[i].Select(plan => plan.ToVectorIndexEntry(passages, embeddings!)).ToArray();
            results[i] = resources[i] with { VectorIndices = entries };
        }

        return results;
    }

    /// <summary>
    /// Computes <see cref="ResourceWrapper.VectorIndices"/> for every resource in <paramref name="resources"/>,
    /// isolating each resource's outcome from every other's -- unlike <see cref="IndexAsync"/>'s
    /// all-or-nothing contract, which is correct for a single atomic write but wrong for a batch bundle's
    /// independently-committing entries (see <c>DeferredWriteCoordinator.ProcessBatchAsync</c>). A
    /// resource with no semantic text always succeeds with <c>VectorIndices = []</c>, regardless of what
    /// happens to any other resource in the call. A resource whose own planning fails (a non-string
    /// extracted value, an invalid effective chunk/overlap configuration, or too many chunks) fails only
    /// that resource -- its passages are withdrawn before the shared embedding call, so a planning
    /// failure never taints the batch. Resources that did contribute passages still share exactly one
    /// <see cref="IEmbeddingGenerator{TInput, TEmbedding}.GenerateAsync"/> call; if that call fails, every
    /// contributing resource's result carries that same exception, while non-contributing resources are
    /// unaffected.
    /// </summary>
    /// <remarks>
    /// Never throws for a per-resource planning or embedding failure -- those are reported through each
    /// <see cref="SemanticIndexResult.Error"/> instead, so the caller can complete each queued write
    /// independently. Only the caller's own <paramref name="cancellationToken"/> being cancelled
    /// propagates as a thrown <see cref="OperationCanceledException"/>.
    /// </remarks>
    public async Task<IReadOnlyList<SemanticIndexResult>> IndexIndependentlyAsync(
        IReadOnlyList<ResourceWrapper> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources.Count == 0)
        {
            return [];
        }

        var passages = new List<string>();
        var plans = new List<PendingEntry>?[resources.Count];
        var planErrors = new Exception?[resources.Count];

        for (var i = 0; i < resources.Count; i++)
        {
            if (resources[i].IsDeleted)
            {
                plans[i] = [];
                continue;
            }

            var passageCountBeforePlan = passages.Count;
            try
            {
                plans[i] = BuildPlans(resources[i], passages);
            }
            catch (Exception ex)
            {
                // Withdraw every passage this resource contributed before failing, including partial
                // progress from a group that built successfully before a later group threw -- none of
                // this resource's text should reach the shared embedding call below.
                passages.RemoveRange(passageCountBeforePlan, passages.Count - passageCountBeforePlan);
                planErrors[i] = ex;
            }
        }

        IReadOnlyList<Embedding<float>>? embeddings = null;
        Exception? embeddingError = null;
        if (passages.Count > 0)
        {
            try
            {
                embeddings = await GenerateAsync(passages, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                embeddingError = ex;
            }
        }

        var results = new SemanticIndexResult[resources.Count];
        for (var i = 0; i < resources.Count; i++)
        {
            if (planErrors[i] is { } planError)
            {
                results[i] = SemanticIndexResult.Failed(planError);
                continue;
            }

            var plan = plans[i]!;
            if (plan.Count > 0 && embeddingError is { } sharedError)
            {
                results[i] = SemanticIndexResult.Failed(sharedError);
                continue;
            }

            var entries = plan.Count == 0
                ? (IReadOnlyList<VectorIndexEntry>)[]
                : plan.Select(p => p.ToVectorIndexEntry(passages, embeddings!)).ToArray();
            results[i] = SemanticIndexResult.Succeeded(resources[i] with { VectorIndices = entries });
        }

        return results;
    }

    /// <summary>
    /// Groups <paramref name="resource"/>'s semantic search index entries by search parameter (preserving
    /// extraction order), extracts and chunks each group's text per its <see cref="VectorSearchConfig"/>,
    /// and appends every chunk's text to the shared <paramref name="passages"/> list -- recording where
    /// each chunk landed so <see cref="PendingEntry.ToVectorIndexEntry"/> can look its embedding up after
    /// the one provider call below returns.
    /// </summary>
    private List<PendingEntry> BuildPlans(ResourceWrapper resource, List<string> passages)
    {
        var groups = new List<(SearchParameterInfo Parameter, List<string> Values)>();

        if (resource.SearchIndices is not null)
        {
            foreach (var indexEntry in resource.SearchIndices)
            {
                if (indexEntry is not SearchIndexEntry entry || !entry.SearchParameter.IsSemantic)
                {
                    continue;
                }

                if (entry.Value is not StringSearchValue stringValue)
                {
                    throw new SemanticSearchDefinitionException(
                        $"Semantic search parameter '{entry.SearchParameter.Url}' extracted a non-string value from " +
                        $"{resource.ResourceType}/{resource.ResourceId}; semantic text must be a FHIR string.");
                }

                var group = groups.FirstOrDefault(g => g.Parameter.Url == entry.SearchParameter.Url);
                if (group.Values is null)
                {
                    group = (entry.SearchParameter, []);
                    groups.Add(group);
                }

                group.Values.Add(stringValue.String);
            }
        }

        var plans = new List<PendingEntry>(groups.Count);
        foreach (var (parameter, values) in groups)
        {
            plans.Add(BuildPlan(resource, parameter, values, passages));
        }

        return plans;
    }

    private PendingEntry BuildPlan(ResourceWrapper resource, SearchParameterInfo parameter, List<string> values, List<string> passages)
    {
        var config = parameter.VectorConfig!;
        IReadOnlyList<string> sources = config.ExtractionPolicy switch
        {
            VectorTextExtractionPolicy.FirstValue => [values[0]],
            VectorTextExtractionPolicy.Concatenate => [string.Join("\n", values)],
            VectorTextExtractionPolicy.PerValueRow => values,
            _ => throw new SemanticSearchDefinitionException(
                $"Semantic search parameter '{parameter.Url}' has an unrecognized extraction policy '{config.ExtractionPolicy}'.")
        };

        var chunkSizeTokens = config.ChunkSizeTokens ?? _options.Indexing.ChunkSizeTokens;
        var chunkOverlapTokens = config.ChunkOverlapTokens ?? _options.Indexing.ChunkOverlapTokens;
        ValidateChunkConfig(parameter.Url, chunkSizeTokens, chunkOverlapTokens);

        var chunkRefs = new List<(short Ordinal, int PassageIndex)>();
        var ordinal = 0;
        foreach (var source in sources)
        {
            foreach (var chunkText in _chunker.Chunk(source, config.MaxInputTokens, chunkSizeTokens, chunkOverlapTokens))
            {
                if (ordinal > short.MaxValue)
                {
                    throw new SemanticSearchDefinitionException(
                        $"Semantic search parameter '{parameter.Url}' on {resource.ResourceType}/{resource.ResourceId} " +
                        $"produced more than {short.MaxValue} chunks.");
                }

                passages.Add(chunkText);
                chunkRefs.Add(((short)ordinal, passages.Count - 1));
                ordinal++;
            }
        }

        return new PendingEntry(parameter.Url, _options.EmbeddingModelKey, chunkRefs);
    }

    private static void ValidateChunkConfig(Uri parameterUrl, int chunkSizeTokens, int chunkOverlapTokens)
    {
        if (chunkSizeTokens < VectorSearchOptions.MinimumChunkSizeTokens || chunkSizeTokens > VectorSearchOptions.MaxEmbeddingInputTokens)
        {
            throw new SemanticSearchDefinitionException(
                $"Semantic search parameter '{parameterUrl}' has an effective chunk size of {chunkSizeTokens} tokens; " +
                $"must be between {VectorSearchOptions.MinimumChunkSizeTokens} and {VectorSearchOptions.MaxEmbeddingInputTokens}.");
        }

        if (chunkOverlapTokens < 0 || chunkOverlapTokens >= chunkSizeTokens)
        {
            throw new SemanticSearchDefinitionException(
                $"Semantic search parameter '{parameterUrl}' has an effective chunk overlap of {chunkOverlapTokens} tokens; " +
                $"must be 0 or greater and less than the effective chunk size ({chunkSizeTokens}).");
        }
    }

    private async Task<IReadOnlyList<Embedding<float>>> GenerateAsync(List<string> passages, CancellationToken cancellationToken)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings;
        try
        {
            embeddings = await _generator.GenerateAsync(passages, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (EmbeddingProviderFailureClassifier.IsProviderUnavailable(ex, cancellationToken))
        {
            throw new EmbeddingUnavailableException("The embedding provider is unavailable.", ex);
        }

        if (embeddings.Count != passages.Count)
        {
            throw new EmbeddingProviderContractException(
                $"The embedding provider returned {embeddings.Count} embeddings for {passages.Count} passages.");
        }

        foreach (var embedding in embeddings)
        {
            if (embedding.Vector.Length != VectorSearchOptions.SupportedDimensions)
            {
                throw new EmbeddingProviderContractException(
                    $"The embedding provider returned a vector with {embedding.Vector.Length} dimensions; " +
                    $"expected {VectorSearchOptions.SupportedDimensions}.");
            }
        }

        return embeddings;
    }

    /// <summary>
    /// Everything needed to assemble one <see cref="VectorIndexEntry"/> once embeddings are available:
    /// which shared <paramref name="passages"/>-list indices this search parameter's chunks landed at.
    /// </summary>
    private sealed record PendingEntry(Uri Url, string EmbeddingModelKey, List<(short Ordinal, int PassageIndex)> Chunks)
    {
        public VectorIndexEntry ToVectorIndexEntry(List<string> passages, IReadOnlyList<Embedding<float>> embeddings) =>
            new(
                Url,
                EmbeddingModelKey,
                Chunks.Select(chunk => new VectorChunk(chunk.Ordinal, passages[chunk.PassageIndex], embeddings[chunk.PassageIndex].Vector)).ToArray());
    }
}
