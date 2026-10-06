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
    /// <exception cref="InvalidOperationException">
    /// A semantic search parameter's extracted value is not a string, its effective chunk/overlap
    /// configuration is invalid, it produced more chunks than fit in <see cref="short"/>, or the provider's
    /// response did not match the request (wrong count or wrong vector dimensionality). All of these
    /// indicate a configuration or provider contract violation, not recoverable input data.
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
                    throw new InvalidOperationException(
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
            _ => throw new InvalidOperationException(
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
                    throw new InvalidOperationException(
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
            throw new InvalidOperationException(
                $"Semantic search parameter '{parameterUrl}' has an effective chunk size of {chunkSizeTokens} tokens; " +
                $"must be between {VectorSearchOptions.MinimumChunkSizeTokens} and {VectorSearchOptions.MaxEmbeddingInputTokens}.");
        }

        if (chunkOverlapTokens < 0 || chunkOverlapTokens >= chunkSizeTokens)
        {
            throw new InvalidOperationException(
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
        catch (Exception ex) when (IsProviderUnavailable(ex, cancellationToken))
        {
            throw new EmbeddingUnavailableException("The embedding provider is unavailable.", ex);
        }

        if (embeddings.Count != passages.Count)
        {
            throw new InvalidOperationException(
                $"The embedding provider returned {embeddings.Count} embeddings for {passages.Count} passages.");
        }

        foreach (var embedding in embeddings)
        {
            if (embedding.Vector.Length != VectorSearchOptions.SupportedDimensions)
            {
                throw new InvalidOperationException(
                    $"The embedding provider returned a vector with {embedding.Vector.Length} dimensions; " +
                    $"expected {VectorSearchOptions.SupportedDimensions}.");
            }
        }

        return embeddings;
    }

    /// <summary>
    /// True for a provider failure that should be reported as <see cref="EmbeddingUnavailableException"/>:
    /// a transport or SDK-level failure (<see cref="HttpRequestException"/>,
    /// <see cref="System.ClientModel.ClientResultException"/>, <see cref="Azure.RequestFailedException"/>),
    /// or a cancellation the provider itself raised (a request timeout) rather than one caused by
    /// <paramref name="cancellationToken"/>. The caller's own cancellation must propagate unchanged, not be
    /// reported as a provider outage.
    /// </summary>
    private static bool IsProviderUnavailable(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException => true,
        System.ClientModel.ClientResultException => true,
        Azure.RequestFailedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };

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
