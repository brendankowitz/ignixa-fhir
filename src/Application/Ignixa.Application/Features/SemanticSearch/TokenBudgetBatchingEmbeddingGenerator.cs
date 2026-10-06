// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.AI;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Wraps an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> to split a request into ordered
/// provider-sized batches (by input count and by token budget), so a single <see cref="GenerateAsync"/>
/// call can embed more inputs than the provider accepts in one request while preserving order.
/// </summary>
/// <remarks>
/// Also enforces the contract this slice depends on but that <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
/// itself does not: every input stays under the provider's per-input token limit, and the provider
/// returns exactly one <see cref="Embedding{T}"/> per input with the configured dimensionality.
/// A provider that violates either is a configuration or outage problem that must not produce
/// silently-misaligned vectors, so both are reported as <see cref="InvalidOperationException"/> rather
/// than tolerated.
/// </remarks>
public sealed class TokenBudgetBatchingEmbeddingGenerator : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
    private readonly SemanticTextChunker _tokenizer;
    private readonly int _dimensions;
    private readonly int _maxBatchInputs;
    private readonly int _maxBatchTokens;

    /// <param name="inner">The provider-backed generator to batch requests to.</param>
    /// <param name="tokenizer">Used to count tokens per input for both the per-input limit check and token-budget batching.</param>
    /// <param name="dimensions">The expected embedding dimensionality; also the default requested on <see cref="EmbeddingGenerationOptions"/> when the caller does not set one.</param>
    /// <param name="maxBatchInputs">The maximum inputs per provider call.</param>
    /// <param name="maxBatchTokens">The maximum summed input tokens per provider call.</param>
    public TokenBudgetBatchingEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner,
        SemanticTextChunker tokenizer,
        int dimensions,
        int maxBatchInputs = 2048,
        int maxBatchTokens = 300_000)
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimensions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBatchInputs, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBatchTokens, 0);

        _tokenizer = tokenizer;
        _dimensions = dimensions;
        _maxBatchInputs = maxBatchInputs;
        _maxBatchTokens = maxBatchTokens;
    }

    /// <inheritdoc />
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var inputs = values as IReadOnlyList<string> ?? [.. values];
        if (inputs.Count == 0)
        {
            return [];
        }

        var tokenCounts = new int[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            var tokenCount = _tokenizer.CountTokens(inputs[i]);
            if (tokenCount > VectorSearchOptions.MaxEmbeddingInputTokens)
            {
                throw new InvalidOperationException(
                    $"Input at index {i} has {tokenCount} tokens, exceeding the provider's per-input limit " +
                    $"of {VectorSearchOptions.MaxEmbeddingInputTokens} tokens.");
            }

            tokenCounts[i] = tokenCount;
        }

        var effectiveOptions = options?.Clone() ?? new EmbeddingGenerationOptions();
        effectiveOptions.Dimensions ??= _dimensions;

        var results = new GeneratedEmbeddings<Embedding<float>>(inputs.Count);
        foreach (var batch in BuildBatches(inputs, tokenCounts))
        {
            var batchResults = await InnerGenerator.GenerateAsync(batch, effectiveOptions, cancellationToken).ConfigureAwait(false);

            if (batchResults.Count != batch.Count)
            {
                throw new InvalidOperationException(
                    $"The embedding provider returned {batchResults.Count} embeddings for a batch of {batch.Count} inputs.");
            }

            foreach (var embedding in batchResults)
            {
                if (embedding.Vector.Length != _dimensions)
                {
                    throw new InvalidOperationException(
                        $"The embedding provider returned a vector with {embedding.Vector.Length} dimensions; expected {_dimensions}.");
                }
            }

            results.AddRange(batchResults);
        }

        return results;
    }

    /// <summary>
    /// Splits <paramref name="inputs"/> into ordered batches, each capped at <see cref="_maxBatchInputs"/>
    /// inputs and <see cref="_maxBatchTokens"/> summed tokens. A single input over the token budget still
    /// gets its own one-input batch rather than being rejected here -- the per-input provider limit above
    /// is the only hard ceiling; the batch token budget only controls how many inputs share one request.
    /// </summary>
    private List<List<string>> BuildBatches(IReadOnlyList<string> inputs, IReadOnlyList<int> tokenCounts)
    {
        var batches = new List<List<string>>();
        var currentBatch = new List<string>();
        var currentTokens = 0;

        for (var i = 0; i < inputs.Count; i++)
        {
            var tokenCount = tokenCounts[i];
            var wouldExceedCount = currentBatch.Count >= _maxBatchInputs;
            var wouldExceedTokens = currentBatch.Count > 0 && currentTokens + tokenCount > _maxBatchTokens;

            if (wouldExceedCount || wouldExceedTokens)
            {
                batches.Add(currentBatch);
                currentBatch = [];
                currentTokens = 0;
            }

            currentBatch.Add(inputs[i]);
            currentTokens += tokenCount;
        }

        if (currentBatch.Count > 0)
        {
            batches.Add(currentBatch);
        }

        return batches;
    }
}
