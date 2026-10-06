// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.SemanticSearch;

public class SemanticIndexerTests
{
    private const string ModelName = "text-embedding-3-small";
    private static readonly Uri SemanticParamUrl = new("http://example.org/SearchParameter/semantic-text");

    [Fact]
    public async Task GivenFirstValuePolicy_WhenIndexed_ThenOnlyFirstValueEmbedded()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.FirstValue);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "first value"), SemanticEntry(parameter, "second value"));

        var result = await indexer.IndexAsync([wrapper], CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["first value"]);
        var entry = result[0].VectorIndices.ShouldHaveSingleItem();
        entry.Chunks.ShouldHaveSingleItem().Passage.ShouldBe("first value");
    }

    [Fact]
    public async Task GivenConcatenatePolicy_WhenIndexed_ThenValuesJoinedWithNewline()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"), SemanticEntry(parameter, "beta"));

        var result = await indexer.IndexAsync([wrapper], CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["alpha\nbeta"]);
        var entry = result[0].VectorIndices.ShouldHaveSingleItem();
        entry.Chunks.ShouldHaveSingleItem().Passage.ShouldBe("alpha\nbeta");
    }

    [Fact]
    public async Task GivenPerValueRowPolicy_WhenIndexed_ThenChunkOrdinalsContinueAcrossValues()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.PerValueRow);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "row one"), SemanticEntry(parameter, "row two"));

        var result = await indexer.IndexAsync([wrapper], CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["row one", "row two"]);
        var entry = result[0].VectorIndices.ShouldHaveSingleItem();
        entry.Chunks.Count.ShouldBe(2);
        entry.Chunks[0].Ordinal.ShouldBe((short)0);
        entry.Chunks[0].Passage.ShouldBe("row one");
        entry.Chunks[1].Ordinal.ShouldBe((short)1);
        entry.Chunks[1].Passage.ShouldBe("row two");
    }

    [Fact]
    public async Task GivenNoSemanticText_WhenIndexed_ThenVectorIndicesEmptyNotNull()
    {
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1");

        var result = await indexer.IndexAsync([wrapper], CancellationToken.None);

        result[0].VectorIndices.ShouldNotBeNull();
        result[0].VectorIndices.ShouldBeEmpty();
        generator.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenDeletedResource_WhenIndexed_ThenVectorIndicesEmptyNotNull()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "ignored because deleted")) with { IsDeleted = true };

        var result = await indexer.IndexAsync([wrapper], CancellationToken.None);

        result[0].VectorIndices.ShouldNotBeNull();
        result[0].VectorIndices.ShouldBeEmpty();
        generator.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTwoResources_WhenIndexed_ThenGeneratorCalledOnceInResourceOrder()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapperA = Wrapper("p1", SemanticEntry(parameter, "alpha"));
        var wrapperB = Wrapper("p2", SemanticEntry(parameter, "beta"));

        var result = await indexer.IndexAsync([wrapperA, wrapperB], CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["alpha", "beta"]);
        result[0].VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("alpha");
        result[1].VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("beta");
    }

    [Fact]
    public async Task GivenGeneratorThrowsHttpRequestException_WhenIndexed_ThenEmbeddingUnavailableException()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var failure = new HttpRequestException("boom");
        var generator = new ThrowingEmbeddingGenerator(failure);
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"));

        var exception = await Should.ThrowAsync<EmbeddingUnavailableException>(() =>
            indexer.IndexAsync([wrapper], CancellationToken.None));

        exception.InnerException.ShouldBeSameAs(failure);
        exception.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task GivenCallerCancellationRequested_WhenIndexed_ThenOperationCanceledExceptionPropagatesUnwrapped()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        using var cancellation = new CancellationTokenSource();
        var generator = new CancelingEmbeddingGenerator(cancellation);
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"));
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            indexer.IndexAsync([wrapper], cancellation.Token));
    }

    [Fact]
    public async Task GivenNonStringSemanticValue_WhenIndexed_ThenSemanticSearchDefinitionExceptionNamesParameter()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", new SearchIndexEntry(parameter, new NumberSearchValue(1)));

        var exception = await Should.ThrowAsync<SemanticSearchDefinitionException>(() =>
            indexer.IndexAsync([wrapper], CancellationToken.None));

        exception.Message.ShouldContain(SemanticParamUrl.ToString());
        exception.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GivenInvalidEffectiveChunkOverlap_WhenIndexed_ThenSemanticSearchDefinitionExceptionNamesParameter()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate, chunkSizeTokens: 20, chunkOverlapTokens: 20);
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"));

        var exception = await Should.ThrowAsync<SemanticSearchDefinitionException>(() =>
            indexer.IndexAsync([wrapper], CancellationToken.None));

        exception.Message.ShouldContain(SemanticParamUrl.ToString());
        exception.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GivenProviderReturnsWrongDimensions_WhenIndexed_ThenEmbeddingProviderContractException()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.Concatenate);
        var generator = new WrongDimensionEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"));

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() => indexer.IndexAsync([wrapper], CancellationToken.None));

        exception.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GivenProviderReturnsWrongCount_WhenIndexed_ThenEmbeddingProviderContractException()
    {
        var parameter = SemanticParameter(VectorTextExtractionPolicy.PerValueRow);
        var generator = new WrongCountEmbeddingGenerator();
        var indexer = CreateIndexer(generator);
        var wrapper = Wrapper("p1", SemanticEntry(parameter, "alpha"), SemanticEntry(parameter, "beta"));

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() => indexer.IndexAsync([wrapper], CancellationToken.None));

        exception.StatusCode.ShouldBe(500);
    }

    private static SemanticIndexer CreateIndexer(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        new(generator, new SemanticTextChunker(ModelName), Options());

    private static VectorSearchOptions Options() => new()
    {
        Enabled = true,
        Embedding = new VectorSearchEmbeddingOptions { ModelName = ModelName, ModelVersion = "1" },
        Indexing = new VectorSearchIndexingOptions { ChunkSizeTokens = 800, ChunkOverlapTokens = 100 },
    };

    private static SearchParameterInfo SemanticParameter(
        VectorTextExtractionPolicy policy,
        int? chunkSizeTokens = null,
        int? chunkOverlapTokens = null) =>
        new(
            "semantic-text",
            "semantic-text",
            SearchParamType.Special,
            SemanticParamUrl,
            vectorConfig: new VectorSearchConfig(policy, MaxInputTokens: 8000, MinimumScore: 0m, chunkSizeTokens, chunkOverlapTokens));

    private static SearchIndexEntry SemanticEntry(SearchParameterInfo parameter, string text) =>
        new(parameter, new StringSearchValue(text));

    private static ResourceWrapper Wrapper(string id, params SearchIndexEntry[] entries) =>
        new(
            "Patient",
            id,
            "1",
            DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"Patient/{id}"))
        {
            SearchIndices = entries.Length == 0 ? null : entries,
        };

    /// <summary>Records every batch it is asked to embed, delegating to <see cref="DeterministicEmbeddingGenerator"/>.</summary>
    private sealed class SpyEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<IReadOnlyList<string>> Batches { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var inputs = values.ToList();
            Batches.Add(inputs);

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

    /// <summary>Always fails with a fixed exception, to pin the provider-failure-to-503 mapping.</summary>
    private sealed class ThrowingEmbeddingGenerator(Exception failure) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw failure;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Throws <see cref="OperationCanceledException"/> carrying the caller's own token, simulating a
    /// provider call that observes the caller's cancellation rather than timing out on its own.
    /// </summary>
    private sealed class CancelingEmbeddingGenerator(CancellationTokenSource callerSource) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException(callerSource.Token);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Returns a vector with the wrong dimensionality, to pin the provider dimension-mismatch guard.</summary>
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
                results.Add(new Embedding<float>(new float[16]));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Returns fewer embeddings than requested passages, to pin the provider count-mismatch guard.</summary>
    private sealed class WrongCountEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new GeneratedEmbeddings<Embedding<float>>();
            var first = values.FirstOrDefault();
            if (first is not null)
            {
                results.Add(new Embedding<float>(DeterministicEmbeddingGenerator.Generate(first)));
            }

            return Task.FromResult(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
