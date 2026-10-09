// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.SemanticSearch;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Resource;

/// <summary>
/// Task 4 (semantic-vector-search slice 1): <see cref="CreateOrUpdateResourceHandler"/>'s standalone
/// write path embeds semantic text through an optional <see cref="SemanticIndexer"/> before the
/// resource reaches the repository. These tests construct the handler directly (bypassing Autofac and
/// the Medino pipeline, which add unrelated validation/capability concerns) so the semantic wiring can
/// be pinned in isolation.
/// </summary>
public class CreateOrUpdateResourceHandlerSemanticSearchTests
{
    private const string ModelName = "text-embedding-3-small";
    private static readonly Uri SemanticParamUrl = new("http://example.org/SearchParameter/semantic-text");

    [Fact]
    public async Task GivenFeatureDisabled_WhenCreating_ThenVectorIndicesNull()
    {
        using var harness = new Harness(semanticIndexer: null);

        await harness.SendAsync("""{"resourceType":"Patient"}""");

        harness.LastWrite.ShouldNotBeNull();
        harness.LastWrite!.VectorIndices.ShouldBeNull();
    }

    [Fact]
    public async Task GivenEnabledWithSemanticText_WhenCreating_ThenVectorIndicesPopulatedBeforeWrite()
    {
        var generator = new SpyEmbeddingGenerator();
        using var harness = new Harness(CreateIndexer(generator));

        await harness.SendAsync("""{"resourceType":"Patient"}""");

        generator.Batches.ShouldHaveSingleItem();
        harness.LastWrite.ShouldNotBeNull();
        var entry = harness.LastWrite!.VectorIndices.ShouldHaveSingleItem();
        entry.SearchParameterUrl.ShouldBe(SemanticParamUrl);
        entry.Chunks.ShouldHaveSingleItem().Passage.ShouldBe("semantic text");
    }

    [Fact]
    public async Task GivenGeneratorThrowsHttpRequestException_WhenCreating_ThenEmbeddingUnavailableAndRepositoryNotCalled()
    {
        var generator = new ThrowingEmbeddingGenerator(new HttpRequestException("boom"));
        using var harness = new Harness(CreateIndexer(generator));

        await Should.ThrowAsync<EmbeddingUnavailableException>(() => harness.SendAsync("""{"resourceType":"Patient"}"""));

        await harness.Repository.DidNotReceive().CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>());
        harness.LastWrite.ShouldBeNull();
    }

    /// <summary>
    /// Final-review finding I-1: enabling semantic search must not force every resource type's write
    /// through the embedding/vector pipeline -- only a resource type that actually carries an active
    /// semantic search parameter is evaluated at all. A type without one (Patient, in this harness, once
    /// its definition manager reports no semantic parameter) must get a null
    /// <see cref="ResourceWrapper.VectorIndices"/>, exactly like the feature being disabled, and must
    /// never call the embedding generator.
    /// </summary>
    [Fact]
    public async Task GivenEnabledButResourceTypeHasNoSemanticParameter_WhenCreating_ThenVectorIndicesNullAndGeneratorNotCalled()
    {
        var generator = new SpyEmbeddingGenerator();
        using var harness = new Harness(CreateIndexer(generator), resourceTypeHasSemanticParameter: false);

        await harness.SendAsync("""{"resourceType":"Patient"}""");

        generator.Batches.ShouldBeEmpty();
        harness.LastWrite.ShouldNotBeNull();
        harness.LastWrite!.VectorIndices.ShouldBeNull();
    }

    private static SemanticIndexer CreateIndexer(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        new(generator, new SemanticTextChunker(ModelName), new VectorSearchOptions
        {
            Enabled = true,
            Embedding = new VectorSearchEmbeddingOptions { ModelName = ModelName, ModelVersion = "1" },
        });

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

    private sealed class Harness : IDisposable
    {
        private readonly CreateOrUpdateResourceHandler _handler;

        public IFhirRepository Repository { get; } = Substitute.For<IFhirRepository>();

        public ResourceWrapper? LastWrite { get; private set; }

        public Harness(SemanticIndexer? semanticIndexer, bool resourceTypeHasSemanticParameter = true)
        {
            var partitionStrategy = Substitute.For<IPartitionStrategy>();
            partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
                .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

            var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
            repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(Repository);
            Repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    LastWrite = call.Arg<ResourceWrapper>();
                    return new UpdateResult(
                        new ResourceKey("Patient", "p1", "1"),
                        ReadOnlyMemory<byte>.Empty,
                        DateTimeOffset.UnixEpoch);
                });

            var context = Substitute.For<IFhirRequestContext>();
            context.TenantId.Returns(1);
            context.FhirVersion.Returns(FhirVersion.R4);
            context.TenantConfiguration.Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Test", FhirVersion = "R4" });
            var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            contextAccessor.RequestContext.Returns(context);

            var parameter = new SearchParameterInfo(
                "semantic-text",
                "semantic-text",
                SearchParamType.Special,
                SemanticParamUrl,
                vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, MaxInputTokens: 8000, MinimumScore: 0m, null, null));
            var searchIndexer = Substitute.For<ISearchIndexer>();
            searchIndexer.Extract(Arg.Any<IElement>())
                .Returns(resourceTypeHasSemanticParameter
                    ? new[] { new SearchIndexEntry(parameter, new StringSearchValue("semantic text")) }
                    : []);

            var versionContext = Substitute.For<IFhirVersionContext>();
            versionContext.GetSchemaProvider(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(new R4CoreSchemaProvider());
            versionContext.GetSearchIndexer(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(searchIndexer);

            // "Patient" (the only resource type these tests write) has this semantic parameter active
            // only when resourceTypeHasSemanticParameter is true, matching what the real definition
            // manager would report and what ISearchIndexer would actually extract: see
            // CreateOrUpdateResourceHandler.BuildSemanticParameterPredicate.
            var definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            definitionManager.GetSearchParameters(Arg.Any<string>())
                .Returns(resourceTypeHasSemanticParameter ? [parameter] : []);
            versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(definitionManager);

            var schemaResolverFactory = Substitute.For<Func<FhirVersion, IValidationSchemaResolver>>();

            _handler = new CreateOrUpdateResourceHandler(
                partitionStrategy,
                repositoryFactory,
                contextAccessor,
                versionContext,
                schemaResolverFactory,
                NullLogger<CreateOrUpdateResourceHandler>.Instance,
                semanticIndexer);
        }

        public Task<UpdateResult> SendAsync(string json, string id = "p1") =>
            _handler.HandleAsync(
                new CreateOrUpdateResourceCommand(
                    "Patient", id, JsonSourceNodeFactory.Parse(json), System.Net.Http.HttpMethod.Put),
                CancellationToken.None);

        public void Dispose()
        {
        }
    }
}
