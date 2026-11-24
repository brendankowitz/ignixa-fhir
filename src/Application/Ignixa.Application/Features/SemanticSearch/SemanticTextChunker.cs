// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.ML.Tokenizers;

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Splits semantic search text into token-bounded chunks for embedding, using the BPE tokenizer that
/// matches the configured embedding model. Ports the chunking algorithm from microsoft/fhir-server#5803
/// (<c>TextChunker</c>) onto <see cref="TiktokenTokenizer"/>.
/// </summary>
public sealed class SemanticTextChunker
{
    private readonly TiktokenTokenizer _tokenizer;

    /// <summary>
    /// Creates a chunker backed by the BPE tokenizer for <paramref name="modelName"/>.
    /// </summary>
    /// <param name="modelName">The embedding model name, e.g. "text-embedding-3-small".</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="modelName"/> is not a model known to <see cref="TiktokenTokenizer"/>.
    /// </exception>
    public SemanticTextChunker(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        try
        {
            _tokenizer = TiktokenTokenizer.CreateForModel(modelName);
        }
        catch (NotSupportedException ex)
        {
            // TiktokenTokenizer.CreateForModel throws NotSupportedException for an unrecognized model
            // name. Translated to ArgumentException here so every caller of this constructor -- in
            // particular VectorSearchOptionsValidator, which needs to report a configuration problem
            // rather than let an unrelated exception type escape -- has one exception type to catch.
            throw new ArgumentException($"'{modelName}' is not a model known to the tokenizer.", nameof(modelName), ex);
        }
    }

    /// <summary>
    /// Counts the tokens <paramref name="text"/> would encode to.
    /// </summary>
    public int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return _tokenizer.CountTokens(text);
    }

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of at most <paramref name="chunkSizeTokens"/> tokens,
    /// each (except the last) overlapping the next by <paramref name="chunkOverlapTokens"/> tokens, after
    /// first capping <paramref name="text"/> to its leading <paramref name="maxInputTokens"/> tokens.
    /// </summary>
    /// <param name="text">The text to chunk. An empty string produces no chunks.</param>
    /// <param name="maxInputTokens">The maximum tokens of <paramref name="text"/> to consider; must be positive.</param>
    /// <param name="chunkSizeTokens">The maximum tokens per chunk; must be at least <see cref="VectorSearchOptions.MinimumChunkSizeTokens"/>.</param>
    /// <param name="chunkOverlapTokens">The tokens of overlap between consecutive chunks; must be 0 or greater and less than <paramref name="chunkSizeTokens"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// The tokenizer could not fit any text within the chunk token limit, or the configured overlap did
    /// not advance the chunk window -- both indicate a tokenizer/configuration mismatch, not recoverable
    /// input data.
    /// </exception>
    public IReadOnlyList<string> Chunk(string text, int maxInputTokens, int chunkSizeTokens, int chunkOverlapTokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxInputTokens, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSizeTokens, VectorSearchOptions.MinimumChunkSizeTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlapTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkOverlapTokens, chunkSizeTokens);

        if (text.Length == 0)
        {
            return [];
        }

        var cappedText = GetPrefix(text, maxInputTokens);
        var chunks = new List<string>();
        var start = 0;

        while (start < cappedText.Length)
        {
            var remaining = cappedText[start..];
            var relativeEnd = _tokenizer.GetIndexByTokenCount(remaining, chunkSizeTokens, out var normalizedText, out _);
            var processed = normalizedText ?? remaining;
            relativeEnd = MoveBeforeSplitSurrogate(processed, relativeEnd);

            if (relativeEnd == 0)
            {
                throw new InvalidOperationException("The configured tokenizer could not fit any text within the chunk token limit.");
            }

            var chunk = processed[..relativeEnd];
            chunks.Add(chunk);

            if (relativeEnd == processed.Length)
            {
                break;
            }

            var overlapStart = chunk.Length;
            if (chunkOverlapTokens > 0)
            {
                overlapStart = _tokenizer.GetIndexByTokenCountFromEnd(chunk, chunkOverlapTokens, out var normalizedChunk, out _);
                overlapStart = MoveBeforeSplitSurrogate(normalizedChunk ?? chunk, overlapStart);
                if (overlapStart == 0)
                {
                    overlapStart = chunk.Length;
                }
            }

            var nextStart = start + overlapStart;
            if (nextStart <= start)
            {
                throw new InvalidOperationException("The configured token overlap did not advance the chunk window.");
            }

            start = nextStart;
        }

        return chunks;
    }

    /// <summary>
    /// Returns the leading prefix of <paramref name="text"/> that fits within <paramref name="maxInputTokens"/> tokens.
    /// </summary>
    private string GetPrefix(string text, int maxInputTokens)
    {
        var index = _tokenizer.GetIndexByTokenCount(text, maxInputTokens, out var normalizedText, out _);
        var processed = normalizedText ?? text;
        index = MoveBeforeSplitSurrogate(processed, index);
        return processed[..index];
    }

    /// <summary>
    /// If <paramref name="index"/> falls between a UTF-16 surrogate pair, steps back one char so a chunk
    /// boundary never splits a single Unicode code point in two.
    /// </summary>
    private static int MoveBeforeSplitSurrogate(string text, int index) =>
        index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])
            ? index - 1
            : index;
}
