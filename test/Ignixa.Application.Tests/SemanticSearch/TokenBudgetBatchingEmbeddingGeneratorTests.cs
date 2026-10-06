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
    public async Task GivenInnerReturnsFewerVectors_WhenGenerated_ThenInvalidOperationException()
    {
        var inner = new ShortChangingEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        await Should.ThrowAsync<InvalidOperationException>(() => generator.GenerateAsync(["a", "b", "c"]));
    }

    [Fact]
    public async Task GivenInnerReturnsWrongDimensions_WhenGenerated_ThenInvalidOperationException()
    {
        var inner = new WrongDimensionEmbeddingGenerator();
        var generator = CreateGenerator(inner);

        await Should.ThrowAsync<InvalidOperationException>(() => generator.GenerateAsync(["a"]));
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
