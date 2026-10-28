// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.SemanticSearch;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Bundle;

/// <summary>
/// Task 4 (semantic-vector-search slice 1): <see cref="DeferredWriteCoordinator"/> embeds every staged
/// write in one <see cref="SemanticIndexer.IndexAsync"/> call during <see cref="DeferredWriteCoordinator.CommitAtomicAsync"/>,
/// after <see cref="DeferredWriteCoordinator.ResolveReferenceAliases"/> has rewritten references and
/// re-extracted search indices, and before the repository ever sees the resource.
/// </summary>
public class DeferredWriteCoordinatorSemanticSearchTests
{
    private const string ModelName = "text-embedding-3-small";
    private static readonly Uri SemanticParamUrl = new("http://example.org/SearchParameter/semantic-text");

    [Fact]
    public async Task GivenBundleWithAliasRewrite_WhenCommitted_ThenVectorsComputedFromRewrittenResource()
    {
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);

        var repository = Substitute.For<IFhirRepository, IAtomicFhirRepository>();
        ((IFhirRepository)repository).GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns((SearchEntryResult?)null);
        IReadOnlyList<ResourceWrapper>? writtenResources = null;
        ((IAtomicFhirRepository)repository).WriteTransactionAsync(Arg.Do<IReadOnlyList<ResourceWrapper>>(r => writtenResources = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns((IFhirRepository)repository);

        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.FhirVersion.Returns(FhirVersion.R4);
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(context);

        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 10,
            repositoryFactory: repositoryFactory,
            partitionStrategy: partitionStrategy,
            contextAccessor: contextAccessor,
            logger: NullLogger<DeferredWriteCoordinator>.Instance,
            atomic: true,
            semanticIndexer: indexer,
            cancellationToken: CancellationToken.None);

        var observationJson = """{"resourceType":"Observation","id":"obs1","subject":{"reference":"Patient/temp-id"}}""";
        var wrapper = new ResourceWrapper(
            "Observation",
            "obs1",
            "1",
            DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse(observationJson),
            new ResourceRequest("POST", "Observation"));

        await coordinator.QueueWriteAsync(wrapper, entryIndex: 0, CancellationToken.None);

        // Simulate a bundle reference alias: "Patient/temp-id" was a conditional-create placeholder,
        // resolved to the real "Patient/final-id" only after that create executed.
        var versionContext = BuildVersionContextThatEmbedsResolvedReference();
        coordinator.ResolveReferenceAliases(
            new Dictionary<string, string> { ["Patient/temp-id"] = "Patient/final-id" },
            versionContext,
            CancellationToken.None);

        await coordinator.CommitAtomicAsync(versionContext, CancellationToken.None);

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["resolved"]);
        writtenResources.ShouldNotBeNull();
        var written = writtenResources!.ShouldHaveSingleItem();
        written.VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("resolved");
    }

    [Fact]
    public async Task GivenEmbeddingFailsDuringCommit_WhenCommitted_ThenRepositoryNeverReceivesTheTransaction()
    {
        var failure = new HttpRequestException("boom");
        var indexer = CreateIndexer(new ThrowingEmbeddingGenerator(failure));

        var repository = Substitute.For<IFhirRepository, IAtomicFhirRepository>();
        ((IFhirRepository)repository).GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns((SearchEntryResult?)null);

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns((IFhirRepository)repository);

        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(context);

        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 10,
            repositoryFactory: repositoryFactory,
            partitionStrategy: partitionStrategy,
            contextAccessor: contextAccessor,
            logger: NullLogger<DeferredWriteCoordinator>.Instance,
            atomic: true,
            semanticIndexer: indexer,
            cancellationToken: CancellationToken.None);

        var parameter = SemanticParameter();
        var wrapper = new ResourceWrapper(
            "Patient",
            "p1",
            "1",
            DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"p1"}"""),
            new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new StringSearchValue("alpha"))],
        };
        await coordinator.QueueWriteAsync(wrapper, entryIndex: 0, CancellationToken.None);

        await Should.ThrowAsync<EmbeddingUnavailableException>(() => coordinator.CommitAtomicAsync(BuildVersionContextWithSemanticParameter(), CancellationToken.None));

        await ((IAtomicFhirRepository)repository).DidNotReceive().WriteTransactionAsync(
            Arg.Any<IReadOnlyList<ResourceWrapper>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenBatchBundleWithSemanticText_WhenProcessed_ThenAllOperationsInTheMicroBatchEmbedInOneCall()
    {
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);

        var writtenWrappers = new List<ResourceWrapper>();
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Do<ResourceWrapper>(w => writtenWrappers.Add(w)), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var w = call.Arg<ResourceWrapper>();
                return new UpdateResult(new ResourceKey(w.ResourceType, w.ResourceId, "1"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch);
            });

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);

        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(context);

        // atomic defaults to false: a batch bundle's writes stream through the channel and commit
        // independently, rather than staging into one transaction like ProcessAtomicTransactionAsync.
        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 10,
            repositoryFactory: repositoryFactory,
            partitionStrategy: partitionStrategy,
            contextAccessor: contextAccessor,
            logger: NullLogger<DeferredWriteCoordinator>.Instance,
            semanticIndexer: indexer,
            cancellationToken: CancellationToken.None);

        var parameter = SemanticParameter();
        var wrapperA = new ResourceWrapper(
            "Patient", "p1", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"p1"}"""), new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new StringSearchValue("alpha"))],
        };
        var wrapperB = new ResourceWrapper(
            "Patient", "p2", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"p2"}"""), new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new StringSearchValue("beta"))],
        };

        var writeA = coordinator.QueueWriteAsync(wrapperA, entryIndex: 0, CancellationToken.None);
        while (coordinator.PendingOperationCount < 1)
        {
            await Task.Yield();
        }
        var writeB = coordinator.QueueWriteAsync(wrapperB, entryIndex: 1, CancellationToken.None);
        while (coordinator.PendingOperationCount < 2)
        {
            await Task.Yield();
        }

        var errors = await coordinator.ProcessBatchAsync(batchSize: 50, BuildVersionContextWithSemanticParameter(), CancellationToken.None);

        errors.ShouldBeEmpty();
        await writeA;
        await writeB;

        generator.Batches.ShouldHaveSingleItem();
        generator.Batches[0].ShouldBe(["alpha", "beta"]);
        writtenWrappers.Count.ShouldBe(2);
        writtenWrappers[0].VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("alpha");
        writtenWrappers[1].VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("beta");
    }

    /// <summary>
    /// Task 4 fix round 1 (CRITICAL): a batch micro-batch's entries still share one embedding call, but an
    /// embedding failure must only fail the entries that actually contributed semantic text -- an entry
    /// with no semantic text must commit to the repository regardless of what happens to its neighbours.
    /// </summary>
    [Fact]
    public async Task GivenBatchWithSemanticAndPlainEntries_WhenEmbeddingFails_ThenOnlySemanticEntriesFail()
    {
        var failure = new HttpRequestException("boom");
        var indexer = CreateIndexer(new ThrowingEmbeddingGenerator(failure));

        var writtenWrappers = new List<ResourceWrapper>();
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Do<ResourceWrapper>(w => writtenWrappers.Add(w)), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var w = call.Arg<ResourceWrapper>();
                return new UpdateResult(new ResourceKey(w.ResourceType, w.ResourceId, "1"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch);
            });

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);

        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(context);

        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 10,
            repositoryFactory: repositoryFactory,
            partitionStrategy: partitionStrategy,
            contextAccessor: contextAccessor,
            logger: NullLogger<DeferredWriteCoordinator>.Instance,
            semanticIndexer: indexer,
            cancellationToken: CancellationToken.None);

        var parameter = SemanticParameter();
        var plainWrapper = new ResourceWrapper(
            "Patient", "plain", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"plain"}"""), new ResourceRequest("POST", "Patient"));
        var semanticWrapper = new ResourceWrapper(
            "Patient", "semantic", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"semantic"}"""), new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new StringSearchValue("alpha"))],
        };

        var writePlain = coordinator.QueueWriteAsync(plainWrapper, entryIndex: 0, CancellationToken.None);
        while (coordinator.PendingOperationCount < 1)
        {
            await Task.Yield();
        }
        var writeSemantic = coordinator.QueueWriteAsync(semanticWrapper, entryIndex: 1, CancellationToken.None);
        while (coordinator.PendingOperationCount < 2)
        {
            await Task.Yield();
        }

        var errors = await coordinator.ProcessBatchAsync(batchSize: 50, BuildVersionContextWithSemanticParameter(), CancellationToken.None);

        errors.ShouldHaveSingleItem().ShouldBeOfType<EmbeddingUnavailableException>();
        (await writePlain).Id.ShouldBe("plain");
        await Should.ThrowAsync<EmbeddingUnavailableException>(() => writeSemantic);

        writtenWrappers.ShouldHaveSingleItem().ResourceId.ShouldBe("plain");
    }

    /// <summary>
    /// Task 4 fix round 1 (CRITICAL): a per-resource planning failure (not a string semantic value) must
    /// fail only that entry; it must never reach the shared embedding call, and it must never fail a
    /// sibling entry that has no planning problem of its own.
    /// </summary>
    [Fact]
    public async Task GivenBatchEntryWithNonStringSemanticValue_WhenProcessed_ThenOnlyThatEntryFails()
    {
        var generator = new SpyEmbeddingGenerator();
        var indexer = CreateIndexer(generator);

        var writtenWrappers = new List<ResourceWrapper>();
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Do<ResourceWrapper>(w => writtenWrappers.Add(w)), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var w = call.Arg<ResourceWrapper>();
                return new UpdateResult(new ResourceKey(w.ResourceType, w.ResourceId, "1"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch);
            });

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);

        var partitionStrategy = Substitute.For<IPartitionStrategy>();
        partitionStrategy.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });

        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns(context);

        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 10,
            repositoryFactory: repositoryFactory,
            partitionStrategy: partitionStrategy,
            contextAccessor: contextAccessor,
            logger: NullLogger<DeferredWriteCoordinator>.Instance,
            semanticIndexer: indexer,
            cancellationToken: CancellationToken.None);

        var parameter = SemanticParameter();
        var goodWrapper = new ResourceWrapper(
            "Patient", "good", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"good"}"""), new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new StringSearchValue("alpha"))],
        };
        var badWrapper = new ResourceWrapper(
            "Patient", "bad", "1", DateTimeOffset.UnixEpoch,
            JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"bad"}"""), new ResourceRequest("POST", "Patient"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new NumberSearchValue(1))],
        };

        var writeGood = coordinator.QueueWriteAsync(goodWrapper, entryIndex: 0, CancellationToken.None);
        while (coordinator.PendingOperationCount < 1)
        {
            await Task.Yield();
        }
        var writeBad = coordinator.QueueWriteAsync(badWrapper, entryIndex: 1, CancellationToken.None);
        while (coordinator.PendingOperationCount < 2)
        {
            await Task.Yield();
        }

        var errors = await coordinator.ProcessBatchAsync(batchSize: 50, BuildVersionContextWithSemanticParameter(), CancellationToken.None);

        errors.ShouldHaveSingleItem().ShouldBeOfType<SemanticSearchDefinitionException>();
        (await writeGood).Id.ShouldBe("good");
        await Should.ThrowAsync<SemanticSearchDefinitionException>(() => writeBad);

        writtenWrappers.ShouldHaveSingleItem().ResourceId.ShouldBe("good");
        writtenWrappers[0].VectorIndices.ShouldHaveSingleItem().Chunks.ShouldHaveSingleItem().Passage.ShouldBe("alpha");
    }

    private static SemanticIndexer CreateIndexer(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        new(generator, new SemanticTextChunker(ModelName), new VectorSearchOptions
        {
            Enabled = true,
            Embedding = new VectorSearchEmbeddingOptions { ModelName = ModelName, ModelVersion = "1" },
        });

    private static SearchParameterInfo SemanticParameter() =>
        new(
            "semantic-text",
            "semantic-text",
            SearchParamType.Special,
            SemanticParamUrl,
            vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, MaxInputTokens: 8000, MinimumScore: 0m, null, null));

    /// <summary>
    /// An <see cref="IFhirVersionContext"/> whose search indexer reads the resource's live
    /// <c>subject.reference</c>, embedding "resolved" once it points at the alias's final identity and
    /// "unresolved" while it still carries the pre-rewrite placeholder -- so a test asserting the
    /// embedded passage is "resolved" is asserting re-extraction ran before embedding, not merely that
    /// embedding ran at all.
    /// </summary>
    private IFhirVersionContext BuildVersionContextThatEmbedsResolvedReference()
    {
        var parameter = SemanticParameter();
        var searchIndexer = Substitute.For<ISearchIndexer>();
        searchIndexer.Extract(Arg.Any<IElement>()).Returns(call =>
        {
            var element = call.Arg<IElement>();
            var reference = element.Scalar("subject.reference") as string;
            var text = reference == "Patient/final-id" ? "resolved" : "unresolved";
            return new[] { new SearchIndexEntry(parameter, new StringSearchValue(text)) };
        });

        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetBaseSchemaProvider(Arg.Any<FhirVersion>()).Returns(new R4CoreSchemaProvider());
        versionContext.GetSearchIndexer(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(searchIndexer);
        ConfigureSemanticParameterDefinitionManager(versionContext);
        return versionContext;
    }

    /// <summary>
    /// An <see cref="IFhirVersionContext"/> whose definition manager reports <see cref="SemanticParameter"/>
    /// as an active semantic parameter for every resource type -- enough for the tests below, which only
    /// exercise <see cref="DeferredWriteCoordinator.CommitAtomicAsync"/> / <see cref="DeferredWriteCoordinator.ProcessBatchAsync"/>
    /// directly and never call <see cref="DeferredWriteCoordinator.ResolveReferenceAliases"/>, so no
    /// search indexer or schema provider needs configuring.
    /// </summary>
    private static IFhirVersionContext BuildVersionContextWithSemanticParameter()
    {
        var versionContext = Substitute.For<IFhirVersionContext>();
        ConfigureSemanticParameterDefinitionManager(versionContext);
        return versionContext;
    }

    private static void ConfigureSemanticParameterDefinitionManager(IFhirVersionContext versionContext)
    {
        var definitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        definitionManager.GetSearchParameters(Arg.Any<string>()).Returns([SemanticParameter()]);
        versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(definitionManager);
    }

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
}
