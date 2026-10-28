// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.SemanticSearch;

public class SemanticQueryPreparerTests
{
    private const string ModelName = "text-embedding-3-small";
    private const string ModelKey = ModelName + "|1";

    private static readonly SearchParameterInfo SemanticText = SemanticParameter("semantic-text", 0m);
    private static readonly SearchParameterInfo SemanticNotes = SemanticParameter("semantic-notes", 0m);

    [Fact]
    public async Task GivenTwoSemanticParams_WhenPrepared_ThenInvalidSearchOperation()
    {
        var generator = new SpyEmbeddingGenerator();
        var preparer = CreatePreparer(generator);
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = Expression.And(
                new VectorSearchExpression(SemanticText, "chest pain"),
                new VectorSearchExpression(SemanticNotes, "follow up")),
        };

        var exception = await Should.ThrowAsync<InvalidSearchOperationException>(() =>
            preparer.PrepareAsync(options, CancellationToken.None));

        exception.Message.ShouldBe("Only one semantic search parameter may be specified per search.");
        generator.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSameTextTwice_WhenPrepared_ThenGeneratorCalledOnce()
    {
        var generator = new SpyEmbeddingGenerator();
        var preparer = CreatePreparer(generator);

        var firstOptions = SemanticOptions("chest pain, nausea");
        var secondOptions = SemanticOptions("chest pain, nausea");

        var firstPrepared = await preparer.PrepareAsync(firstOptions, CancellationToken.None);
        var secondPrepared = await preparer.PrepareAsync(secondOptions, CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["chest pain, nausea"]);

        var firstEmbedding = RequirePrepared(firstPrepared).Embedding;
        var secondEmbedding = RequirePrepared(secondPrepared).Embedding;
        secondEmbedding.ToArray().ShouldBe(firstEmbedding.ToArray());
    }

    [Fact]
    public async Task GivenNoSemanticExpression_WhenPrepared_ThenGeneratorNotCalledAndOptionsUnchanged()
    {
        var generator = new SpyEmbeddingGenerator();
        var preparer = CreatePreparer(generator);
        var statusParam = new SearchParameterInfo(
            "status", "status", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/status"));
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new SearchParameterExpression(
                statusParam,
                new SearchParameterPredicateExpression(
                    statusParam,
                    Ignixa.Specification.ValueSets.Normative.SearchComparator.Eq,
                    modifier: null,
                    new Ignixa.Search.Indexing.SearchValues.TokenSearchValue(system: null, code: "final", text: null))),
        };

        var result = await preparer.PrepareAsync(options, CancellationToken.None);

        result.ShouldBeSameAs(options);
        generator.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenProviderFailure_WhenPrepared_ThenEmbeddingUnavailable()
    {
        var failure = new HttpRequestException("boom");
        var generator = new ThrowingEmbeddingGenerator(failure);
        var preparer = CreatePreparer(generator);
        var options = SemanticOptions("chest pain");

        var exception = await Should.ThrowAsync<EmbeddingUnavailableException>(() =>
            preparer.PrepareAsync(options, CancellationToken.None));

        exception.InnerException.ShouldBeSameAs(failure);
        exception.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task GivenMinimumScore_WhenPrepared_ThenMaxDistanceIsTwiceOneMinusScore()
    {
        var generator = new SpyEmbeddingGenerator();
        var preparer = CreatePreparer(generator);
        var parameter = SemanticParameter("semantic-text", 0.3m);
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new VectorSearchExpression(parameter, "chest pain"),
        };

        var result = await preparer.PrepareAsync(options, CancellationToken.None);

        var prepared = RequirePrepared(result);
        prepared.MaxDistance.ShouldBe(2 * (1 - 0.3), tolerance: 1e-9);
        prepared.EmbeddingModelKey.ShouldBe(ModelKey);
    }

    [Fact]
    public async Task GivenProviderReturnsWrongDimensions_WhenPrepared_ThenEmbeddingProviderContractException()
    {
        var preparer = CreatePreparer(new WrongDimensionEmbeddingGenerator());
        var options = SemanticOptions("chest pain");

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() =>
            preparer.PrepareAsync(options, CancellationToken.None));

        exception.StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// Regression for the final-review finding I-2: <see cref="TokenBudgetBatchingEmbeddingGenerator"/> --
    /// the generator this type actually receives in production, wrapping the real provider (see
    /// <see cref="SemanticSearchServiceRegistration.AddSemanticSearch"/>) -- must itself raise
    /// <see cref="EmbeddingProviderContractException"/> for a dimension mismatch, not the generic
    /// <see cref="InvalidOperationException"/> <c>FhirExceptionMiddleware</c> maps to HTTP 400. Before
    /// that fix, the wrapper's own <see cref="InvalidOperationException"/> propagated straight out of
    /// <see cref="SemanticQueryPreparer.PrepareAsync"/> before this type's own, correctly-typed check
    /// (pinned above) ever ran.
    /// </summary>
    [Fact]
    public async Task GivenWrapperAndProviderReturnsWrongDimensions_WhenPrepared_ThenEmbeddingProviderContractException()
    {
        var wrapped = new TokenBudgetBatchingEmbeddingGenerator(
            new WrongDimensionEmbeddingGenerator(), new SemanticTextChunker(ModelName), VectorSearchOptions.SupportedDimensions);
        var preparer = new SemanticQueryPreparer(
            wrapped, new SemanticTextChunker(ModelName), new MemoryCache(new MemoryCacheOptions()), Options());
        var options = SemanticOptions("chest pain");

        var exception = await Should.ThrowAsync<EmbeddingProviderContractException>(() =>
            preparer.PrepareAsync(options, CancellationToken.None));

        exception.StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// Pins the oversized-query-text half of I-2: a query text over the provider's 8192-token per-input
    /// limit is rejected as a client error before any provider call, not left to
    /// <see cref="TokenBudgetBatchingEmbeddingGenerator"/>'s internal (and differently-typed) guard.
    /// </summary>
    [Fact]
    public async Task GivenQueryTextExceedsPerInputTokenLimit_WhenPrepared_ThenInvalidSearchOperationException()
    {
        var generator = new SpyEmbeddingGenerator();
        var preparer = CreatePreparer(generator);
        var oversizedText = string.Join(' ', Enumerable.Range(0, 20_000).Select(i => $"w{i}"));
        var options = SemanticOptions(oversizedText);

        var exception = await Should.ThrowAsync<InvalidSearchOperationException>(() =>
            preparer.PrepareAsync(options, CancellationToken.None));

        exception.Message.ShouldBe("Semantic query text exceeds 8192 tokens.");
        exception.StatusCode.ShouldBe(400);
        generator.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCallerCancellationRequested_WhenPrepared_ThenOperationCanceledExceptionPropagatesUnwrapped()
    {
        using var cancellation = new CancellationTokenSource();
        var preparer = CreatePreparer(new CancelingEmbeddingGenerator(cancellation));
        var options = SemanticOptions("chest pain");
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            preparer.PrepareAsync(options, cancellation.Token));
    }

    [Fact]
    public async Task GivenCacheLimitReached_WhenNewTextPrepared_ThenStillReturnsEmbedding()
    {
        // The regression this pins: MemoryCache.Set throws InvalidOperationException if an entry is
        // written without a Size once the cache has a SizeLimit -- a cache entry past the limit does NOT
        // throw, the cache instead evicts to make room. SizeLimit = 2 here is small enough that the third
        // distinct text's Set is written while the cache is already at (or over) capacity.
        var generator = new SpyEmbeddingGenerator();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 2 });
        var options = Options();
        options.Query.EmbeddingCacheMaxEntries = 2;
        var preparer = new SemanticQueryPreparer(generator, new SemanticTextChunker(ModelName), cache, options);

        var first = await preparer.PrepareAsync(SemanticOptions("chest pain"), CancellationToken.None);
        var second = await preparer.PrepareAsync(SemanticOptions("shortness of breath"), CancellationToken.None);
        var third = await preparer.PrepareAsync(SemanticOptions("nausea"), CancellationToken.None);

        generator.Batches.Count.ShouldBe(3);
        RequirePrepared(first).Embedding.Length.ShouldBe(VectorSearchOptions.SupportedDimensions);
        RequirePrepared(second).Embedding.Length.ShouldBe(VectorSearchOptions.SupportedDimensions);
        RequirePrepared(third).Embedding.Length.ShouldBe(VectorSearchOptions.SupportedDimensions);
    }

    private static SearchOptions SemanticOptions(string queryText) => new()
    {
        ResourceType = "Observation",
        Expression = new VectorSearchExpression(SemanticText, queryText),
    };

    private static PreparedVectorQuery RequirePrepared(SearchOptions options)
    {
        var found = VectorSearchExpressionLocator.FindAll(options.Expression);
        return found.ShouldHaveSingleItem().Prepared.ShouldNotBeNull();
    }

    private static SemanticQueryPreparer CreatePreparer(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        new(generator, new SemanticTextChunker(ModelName), new MemoryCache(new MemoryCacheOptions()), Options());

    private static VectorSearchOptions Options() => new()
    {
        Enabled = true,
        Embedding = new VectorSearchEmbeddingOptions { ModelName = ModelName, ModelVersion = "1" },
        Query = new VectorSearchQueryOptions { EmbeddingCacheMinutes = 10 },
    };

    private static SearchParameterInfo SemanticParameter(string code, decimal minimumScore) => new(
        code,
        code,
        SearchParamType.Special,
        new Uri($"http://example.org/SearchParameter/{code}"),
        vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, 8000, minimumScore, null, null));

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
}
