// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.SemanticSearch;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.SemanticSearch;

public class TokenBudgetBatchingEmbeddingGeneratorTests
{
    private const string ModelName = "text-embedding-3-small";

    [Fact]
    public async Task GivenEmptyInputs_WhenGenerated_ThenEmpty()
    {
        var inner = new SpyEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        var results = await generator.GenerateAsync([]);

        results.ShouldBeEmpty();
        inner.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task Given2049Inputs_WhenGenerated_ThenTwoInnerCallsAndOrderPreserved()
    {
        var inner = new SpyEmbeddingGenerator();
        var generator = CreateGenerator(inner);
        var inputs = Enumerable.Range(0, 2049).Select(i => $"input-{i}").ToList();

        var results = await generator.GenerateAsync(inputs);

        inner.Batches.Count.ShouldBe(2);
        inner.Batches[0].Count.ShouldBe(2048);
        inner.Batches[1].Count.ShouldBe(1);
        results.Count.ShouldBe(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            results[i].Vector.ToArray().ShouldBe(DeterministicEmbeddingGenerator.Generate(inputs[i]).ToArray());
        }
    }

    /// <summary>
    /// M-4: pins the token-budget half of batching independently of the 2048-input-count limit above --
    /// a small <c>maxBatchTokens</c> forces a split well before 2048 inputs are reached, and the split
    /// must still preserve order across the resulting inner calls. Derives the expected grouping from the
    /// tokenizer's own count rather than assuming a fixed tokens-per-input, so the test does not depend on
    /// tokenizer-specific tokenization of these particular strings.
    /// </summary>
    [Fact]
    public async Task GivenSmallMaxBatchTokens_WhenGenerated_ThenSplitsOnTokenBudgetAndOrderPreserved()
    {
        var inner = new SpyEmbeddingGenerator();
        var chunker = new SemanticTextChunker(ModelName);
        var inputs = Enumerable.Range(0, 7).Select(i => $"w{i}").ToList();
        var tokensPerInput = chunker.CountTokens(inputs[0]);
        foreach (var input in inputs)
        {
            chunker.CountTokens(input).ShouldBe(tokensPerInput, "uniform per-input token cost is what makes the batch grouping below predictable");
        }

        // Budget for exactly 3 inputs per inner call, well under the 2048-input-count limit.
        var generator = new TokenBudgetBatchingEmbeddingGenerator(
            inner, chunker, DeterministicEmbeddingGenerator.Dimensions, maxBatchInputs: 2048, maxBatchTokens: tokensPerInput * 3);

        var results = await generator.GenerateAsync(inputs);

        inner.Batches.Count.ShouldBe(3);
        inner.Batches[0].ShouldBe(inputs.Take(3));
        inner.Batches[1].ShouldBe(inputs.Skip(3).Take(3));
        inner.Batches[2].ShouldBe(inputs.Skip(6));
        results.Count.ShouldBe(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            results[i].Vector.ToArray().ShouldBe(DeterministicEmbeddingGenerator.Generate(inputs[i]).ToArray());
        }
    }

    [Fact]
    public async Task GivenInputExceedsPerInputTokenLimit_WhenGenerated_ThenInvalidOperationException()
    {
        var inner = new SpyEmbeddingGenerator();
        var generator = CreateGenerator(inner);
        var oversizedInput = string.Join(' ', Enumerable.Range(0, 20_000).Select(i => $"w{i}"));

        await Should.ThrowAsync<InvalidOperationException>(() => generator.GenerateAsync([oversizedInput]));
        inner.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenInnerReturnsFewerVectors_WhenGenerated_ThenEmbeddingProviderContractException()
    {
        var inner = new ShortChangingEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() => generator.GenerateAsync(["a", "b", "c"]));

        exception.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GivenInnerReturnsWrongDimensions_WhenGenerated_ThenEmbeddingProviderContractException()
    {
        var inner = new WrongDimensionEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() => generator.GenerateAsync(["a"]));

        exception.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GivenOptionsWithoutDimensions_WhenGenerated_ThenInnerReceivesConfiguredDimensions()
    {
        var inner = new SpyEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        await generator.GenerateAsync(["hello"]);

        inner.ObservedOptions.ShouldHaveSingleItem();
        inner.ObservedOptions[0]!.Dimensions.ShouldBe(DeterministicEmbeddingGenerator.Dimensions);
    }

    [Fact]
    public async Task GivenOptionsWithExplicitDimensions_WhenGenerated_ThenInnerReceivesCallersDimensions()
    {
        var inner = new SpyEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        await generator.GenerateAsync(["hello"], new EmbeddingGenerationOptions { Dimensions = 256 });

        inner.ObservedOptions[0]!.Dimensions.ShouldBe(256);
    }

    private static TokenBudgetBatchingEmbeddingGenerator CreateGenerator(IEmbeddingGenerator<string, Embedding<float>> inner) =>
        new(inner, new SemanticTextChunker(ModelName), DeterministicEmbeddingGenerator.Dimensions);

    /// <summary>Records every batch and options instance it is asked to embed, delegating to <see cref="DeterministicEmbeddingGenerator"/>.</summary>
    private sealed class SpyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<IReadOnlyList<string>> Batches { get; } = [];

        public List<EmbeddingGenerationOptions?> ObservedOptions { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = values.ToList();
            Batches.Add(inputs);
            ObservedOptions.Add(options);

            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in inputs)
            {
                results.Add(new Embedding<float>(DeterministicEmbeddingGenerator.Generate(value)));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Returns one fewer embedding than requested, to pin the provider count-mismatch guard.</summary>
    private sealed class ShortChangingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = values.ToList();
            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in inputs.Take(Math.Max(0, inputs.Count - 1)))
            {
                results.Add(new Embedding<float>(DeterministicEmbeddingGenerator.Generate(value)));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Returns embeddings with the wrong dimensionality, to pin the provider dimension guard.</summary>
    private sealed class WrongDimensionEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values)
            {
                results.Add(new Embedding<float>(new float[10]));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
