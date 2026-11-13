// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Application.Features.SemanticSearch;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.SemanticSearch;

public class SemanticTextChunkerTests
{
    private const string ModelName = "text-embedding-3-small";

    [Fact]
    public void GivenUnknownModelName_WhenConstructed_ThenArgumentException()
    {
        Should.Throw<ArgumentException>(() => new SemanticTextChunker("not-a-real-model-xyz"));
    }

    [Fact]
    public void GivenEmptyText_WhenChunked_ThenEmpty()
    {
        var chunker = new SemanticTextChunker(ModelName);

        var chunks = chunker.Chunk(string.Empty, maxInputTokens: 8192, chunkSizeTokens: 800, chunkOverlapTokens: 100);

        chunks.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTextUnderChunkSize_WhenChunked_ThenSingleChunkEqualsText()
    {
        var chunker = new SemanticTextChunker(ModelName);
        const string text = "The patient presented with chest pain and shortness of breath.";

        var chunks = chunker.Chunk(text, maxInputTokens: 8192, chunkSizeTokens: 800, chunkOverlapTokens: 100);

        chunks.Count.ShouldBe(1);
        chunks[0].ShouldBe(text);
    }

    [Fact]
    public void GivenLongText_WhenChunkedWithOverlap_ThenConsecutiveChunksShareOverlapTokensAndEachIsAtMostChunkSize()
    {
        var chunker = new SemanticTextChunker(ModelName);
        var text = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"token{i:D4}"));

        var chunks = chunker.Chunk(text, maxInputTokens: 100_000, chunkSizeTokens: 50, chunkOverlapTokens: 10);

        chunks.Count.ShouldBeGreaterThan(1);

        foreach (var chunk in chunks)
        {
            chunker.CountTokens(chunk).ShouldBeLessThanOrEqualTo(50);
        }

        for (var i = 0; i < chunks.Count - 1; i++)
        {
            LongestSuffixPrefixOverlap(chunks[i], chunks[i + 1]).ShouldBeGreaterThan(
                0, $"chunk {i} and chunk {i + 1} should share overlapping text at their boundary");
        }
    }

    [Fact]
    public void GivenMaxInputTokens_WhenChunked_ThenTotalCoversOnlyPrefix()
    {
        var chunker = new SemanticTextChunker(ModelName);
        var text = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"token{i:D4}"));

        var chunks = chunker.Chunk(text, maxInputTokens: 50, chunkSizeTokens: 800, chunkOverlapTokens: 0);

        chunks.Count.ShouldBe(1);
        chunks[0].Length.ShouldBeLessThan(text.Length);
        text.ShouldStartWith(chunks[0]);
        chunker.CountTokens(chunks[0]).ShouldBeLessThanOrEqualTo(50);
    }

    [Fact]
    public void GivenEmojiAtWindowEdge_WhenChunked_ThenNoChunkEndsWithHighSurrogate()
    {
        var chunker = new SemanticTextChunker(ModelName);

        // Interleaving an astral-plane emoji (a UTF-16 surrogate pair) with filler words across a long
        // text, chunked with a small window, sweeps chunk boundaries across many character offsets. This
        // pins the public, observable contract that Chunk never emits a chunk ending mid-surrogate;
        // GivenIndexAtSurrogateBoundary_WhenAdjusted_ThenMovesBeforeSplit below pins the private
        // MoveBeforeSplitSurrogate helper directly and is the one that actually fails if the guard is
        // removed -- empirically, Microsoft.ML.Tokenizers' GetIndexByTokenCount never itself returns an
        // index strictly inside a surrogate pair for Cl100kBase text, so this property test alone would
        // pass whether or not the guard runs.
        var text = string.Join(' ', Enumerable.Range(0, 300).Select(i => $"word{i:D3}\U0001F600"));

        var chunks = chunker.Chunk(text, maxInputTokens: 100_000, chunkSizeTokens: 20, chunkOverlapTokens: 5);

        chunks.ShouldAllBe(chunk => chunk.Length == 0 || !char.IsHighSurrogate(chunk[chunk.Length - 1]));
    }

    [Theory]
    [InlineData("x\uD83D\uDE00y", 2, 1)]
    [InlineData("x\uD83D\uDE00y", 0, 0)]
    [InlineData("x\uD83D\uDE00y", 4, 4)]
    [InlineData("abc", 2, 2)]
    public void GivenIndexAtSurrogateBoundary_WhenAdjusted_ThenMovesBeforeSplit(string text, int index, int expected)
    {
        var method = typeof(SemanticTextChunker).GetMethod(
            "MoveBeforeSplitSurrogate", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (int)method.Invoke(null, [text, index])!;

        result.ShouldBe(expected);
    }

    /// <summary>
    /// Returns the length of the longest suffix of <paramref name="first"/> that is also a prefix of
    /// <paramref name="second"/>, or 0 if there is none.
    /// </summary>
    private static int LongestSuffixPrefixOverlap(string first, string second)
    {
        var maxLength = Math.Min(first.Length, second.Length);
        for (var length = maxLength; length > 0; length--)
        {
            if (first.AsSpan(first.Length - length).SequenceEqual(second.AsSpan(0, length)))
            {
                return length;
            }
        }

        return 0;
    }
}

