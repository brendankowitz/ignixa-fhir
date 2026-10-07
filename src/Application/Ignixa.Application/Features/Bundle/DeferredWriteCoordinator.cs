using System.Globalization;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Transactions stage their complete write set before one atomic core merge.
/// Batch entries commit independently, including each entry's own version precondition.
/// </summary>
public class DeferredWriteCoordinator
{
    private readonly Channel<DeferredWriteOperation> _writeChannel;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<DeferredWriteCoordinator> _logger;
    private readonly SemanticIndexer? _semanticIndexer;
    private readonly List<(int EntryIndex, ResourceWrapper Resource)> _stagedWrites = [];
    private readonly ConcurrentDictionary<int, bool> _createdEntries = new();

    private DeferredWriteCoordinator(
        int channelCapacity,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DeferredWriteCoordinator> logger,
        bool atomic,
        SemanticIndexer? semanticIndexer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCapacity);
        _repositoryFactory = repositoryFactory;
        _partitionStrategy = partitionStrategy;
        _contextAccessor = contextAccessor;
        _logger = logger;
        IsAtomic = atomic;
        _semanticIndexer = semanticIndexer;
        _writeChannel = Channel.CreateBounded<DeferredWriteOperation>(channelCapacity);
    }

    public bool IsAtomic { get; }
    public int PendingOperationCount => _writeChannel.Reader.Count;
    public bool IsCompleted => _writeChannel.Reader.Completion.IsCompleted;

    public static async Task<DeferredWriteCoordinator> CreateAsync(
        int channelCapacity,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DeferredWriteCoordinator> logger,
        bool atomic = false,
        SemanticIndexer? semanticIndexer = null,
        CancellationToken cancellationToken = default)
    {
        var context = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        var repository = await repositoryFactory.GetRepositoryAsync(context.TenantId, cancellationToken);
        if (atomic && repository is not IAtomicFhirRepository)
        {
            throw new Domain.Exceptions.NotImplementedException("This storage provider does not support atomic transactions.");
        }
        return new DeferredWriteCoordinator(channelCapacity, repositoryFactory, partitionStrategy,
            contextAccessor, logger, atomic, semanticIndexer);
    }

    public async Task<ResourceKey> QueueWriteAsync(
        ResourceWrapper wrapper,
        int entryIndex = 0,
        CancellationToken cancellationToken = default)
    {
        if (IsAtomic)
        {
            return await StageWriteAsync(wrapper, entryIndex, cancellationToken);
        }
        var completion = new TaskCompletionSource<ResourceKey>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _writeChannel.Writer.WriteAsync(new DeferredWriteOperation
        {
            Wrapper = wrapper,
            EntryIndex = entryIndex,
            CompletionSource = completion
        }, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Reads up to <paramref name="batchSize"/> queued writes and commits each independently (a batch
    /// bundle's entries do not share a transaction, so one entry's failure must not affect another's).
    /// When semantic search is enabled, every wrapper read in this call is still embedded together in
    /// one <see cref="SemanticIndexer.IndexIndependentlyAsync"/> call -- one provider round trip for the
    /// whole micro-batch, not one per entry -- but that call isolates each resource's outcome: an entry
    /// with no semantic text is unaffected by any other entry's embedding failure, an entry whose own
    /// planning fails (see <see cref="SemanticIndexer.IndexIndependentlyAsync"/>) fails only that entry,
    /// and only entries that actually contributed passages to a failed shared embedding call fail
    /// together. A failed entry here never reaches the repository below, exactly like a repository
    /// failure caught in that loop.
    /// </summary>
    /// <param name="batchSize">The maximum number of queued writes to read and commit in this call.</param>
    /// <param name="versionContext">
    /// Resolves the tenant/version-scoped <c>ISearchParameterDefinitionManager</c> this call uses to
    /// decide, per resource type, whether <see cref="SemanticIndexer.IndexIndependentlyAsync"/> should
    /// evaluate it at all (see <see cref="SemanticIndexer.IndexAsync"/>'s remarks on its own predicate
    /// parameter). Unused when semantic search is disabled.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<List<Exception>> ProcessBatchAsync(int batchSize, IFhirVersionContext versionContext, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentNullException.ThrowIfNull(versionContext);
        var errors = new List<Exception>();
        if (!await _writeChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            return errors;
        }
        var operations = new List<DeferredWriteOperation>(batchSize);
        for (var i = 0; i < batchSize && _writeChannel.Reader.TryRead(out var operation); i++)
        {
            operations.Add(operation);
        }

        if (_semanticIndexer is not null)
        {
            var context = _contextAccessor.RequestContext
                ?? throw new InvalidOperationException("FHIR request context not available");
            var hasSemanticSearchParameter = BuildSemanticParameterPredicate(versionContext, context.FhirVersion, context.TenantId);

            IReadOnlyList<SemanticIndexResult> indexed;
            try
            {
                indexed = await _semanticIndexer.IndexIndependentlyAsync(
                    operations.ConvertAll(op => op.Wrapper), hasSemanticSearchParameter, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                foreach (var operation in operations)
                {
                    operation.CompletionSource.TrySetCanceled(cancellationToken);
                }
                throw;
            }

            var indexedOperations = new List<DeferredWriteOperation>(operations.Count);
            for (var i = 0; i < operations.Count; i++)
            {
                var result = indexed[i];
                if (result.Error is { } error)
                {
                    _logger.LogWarning(error, "Batch entry {EntryIndex} failed independently during semantic indexing", operations[i].EntryIndex);
                    operations[i].CompletionSource.TrySetException(error);
                    errors.Add(error);
                    continue;
                }

                indexedOperations.Add(new DeferredWriteOperation
                {
                    Wrapper = result.Resource!,
                    EntryIndex = operations[i].EntryIndex,
                    CompletionSource = operations[i].CompletionSource
                });
            }
            operations = indexedOperations;
        }

        foreach (var operation in operations)
        {
            try
            {
                var partitionId = ResolvePartition(operation.Wrapper);
                var repository = await _repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);
                var result = await repository.CreateOrUpdateAsync(operation.Wrapper, cancellationToken);
                _createdEntries[operation.EntryIndex] = result.IsCreated ?? result.Key.VersionId == "1";
                operation.CompletionSource.TrySetResult(result.Key);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                operation.CompletionSource.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch entry {EntryIndex} failed independently", operation.EntryIndex);
                operation.CompletionSource.TrySetException(ex);
                errors.Add(ex);
            }
        }
        return errors;
    }

    private int ResolvePartition(ResourceWrapper wrapper)
    {
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        var partition = _partitionStrategy.DetermineWritePartition(new PartitionResolutionContext
        {
            TenantId = context.TenantId,
            TenantConfiguration = context.TenantConfiguration
        }, wrapper.Resource);
        if (partition.PartitionIds.Count != 1 || (IsAtomic && partition.PartitionIds[0] != context.TenantId))
        {
            throw new BadRequestException("A transaction must target exactly one tenant partition.");
        }
        return partition.PartitionIds[0];
    }

    private async Task<ResourceKey> StageWriteAsync(ResourceWrapper wrapper, int entryIndex, CancellationToken cancellationToken)
    {
        var partitionId = ResolvePartition(wrapper);
        if (_stagedWrites.Any(write =>
            write.Resource.ResourceType == wrapper.ResourceType && write.Resource.ResourceId == wrapper.ResourceId))
        {
            throw new BadRequestException("A transaction cannot write the same resource more than once.");
        }
        var repository = await _repositoryFactory.GetRepositoryAsync(partitionId, cancellationToken);
        var existing = await repository.GetAsync(new ResourceKey(wrapper.ResourceType, wrapper.ResourceId), cancellationToken);
        if (wrapper.ExpectedVersionId != null && wrapper.ExpectedVersionId != existing?.VersionId)
        {
            throw new PreconditionFailedException("The supplied If-Match version is not current.");
        }
        var version = checked(int.Parse(existing?.VersionId ?? "0", CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        wrapper.Resource.Meta.VersionId = version;
        wrapper.Resource.Meta.LastUpdatedOffset = DateTimeOffset.UtcNow;
        _stagedWrites.Add((entryIndex, wrapper with { ExpectedVersionId = existing?.VersionId ?? "0", VersionId = version }));
        _createdEntries[entryIndex] = existing == null || existing.IsDeleted;
        return new ResourceKey(wrapper.ResourceType, wrapper.ResourceId, version, partitionId);
    }

    public bool IsCreated(int entryIndex) => _createdEntries[entryIndex];

    public ResourceWrapper? FindStagedResource(string resourceType, string resourceId) =>
        _stagedWrites.FirstOrDefault(write =>
            write.Resource.ResourceType == resourceType && write.Resource.ResourceId == resourceId).Resource;

    /// <summary>
    /// Commits every staged write in one atomic core merge. When semantic search is enabled, all staged
    /// resources are embedded together in one <see cref="SemanticIndexer.IndexAsync"/> call first --
    /// after <see cref="ResolveReferenceAliases"/> has already rewritten references and re-extracted
    /// search indices, so semantic text reflects each resource's final, committed identity -- and before
    /// <see cref="IAtomicFhirRepository.WriteTransactionAsync"/>, so an embedding failure propagates with
    /// nothing written, exactly like any other pre-commit validation failure in this transaction.
    /// </summary>
    /// <param name="versionContext">
    /// Resolves the tenant/version-scoped <c>ISearchParameterDefinitionManager</c> this call uses to
    /// decide, per resource type, whether <see cref="SemanticIndexer.IndexAsync"/> should evaluate it at
    /// all (see that method's remarks on its own predicate parameter). Unused when semantic search is
    /// disabled.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CommitAtomicAsync(IFhirVersionContext versionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(versionContext);
        cancellationToken.ThrowIfCancellationRequested();
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        var repository = await _repositoryFactory.GetRepositoryAsync(context.TenantId, cancellationToken);

        if (_semanticIndexer is not null && _stagedWrites.Count > 0)
        {
            var hasSemanticSearchParameter = BuildSemanticParameterPredicate(versionContext, context.FhirVersion, context.TenantId);
            var indexed = await _semanticIndexer.IndexAsync(
                _stagedWrites.ConvertAll(write => write.Resource), hasSemanticSearchParameter, cancellationToken);
            for (var i = 0; i < _stagedWrites.Count; i++)
            {
                _stagedWrites[i] = (_stagedWrites[i].EntryIndex, indexed[i]);
            }
        }

        await ((IAtomicFhirRepository)repository).WriteTransactionAsync(
            _stagedWrites.Select(write => write.Resource).ToArray(), cancellationToken);
    }

    /// <summary>
    /// Builds the per-resource-type predicate <see cref="SemanticIndexer.IndexAsync"/> and
    /// <see cref="SemanticIndexer.IndexIndependentlyAsync"/> use to decide null vs. <c>[]</c> for a
    /// resource with no extracted semantic text (see those methods' remarks): true when
    /// <paramref name="resourceType"/> carries at least one active semantic search parameter in
    /// <paramref name="versionContext"/>'s definition manager for <paramref name="fhirVersion"/> and
    /// <paramref name="tenantId"/> -- the same manager <see cref="ResolveReferenceAliases"/> already
    /// resolves its indexer from, so this call sees the same tenant's custom package parameters.
    /// </summary>
    private static Func<string, bool> BuildSemanticParameterPredicate(
        IFhirVersionContext versionContext, FhirVersion fhirVersion, int? tenantId)
    {
        var definitionManager = versionContext.GetSearchParameterDefinitionManager(fhirVersion, tenantId);
        return resourceType => definitionManager.GetSearchParameters(resourceType).Any(p => p.IsSemantic && p.IsSupported);
    }

    public void ResolveReferenceAliases(
        IReadOnlyDictionary<string, string> aliases,
        IFhirVersionContext versionContext,
        CancellationToken cancellationToken)
    {
        if (aliases.Count == 0)
        {
            return;
        }
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        var schemaProvider = versionContext.GetBaseSchemaProvider(context.FhirVersion);
        var indexer = versionContext.GetSearchIndexer(context.FhirVersion, context.TenantId);
        for (var index = 0; index < _stagedWrites.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (entryIndex, wrapper) = _stagedWrites[index];
            if (BundleReferencePreProcessor.RewriteReferenceAliases(wrapper.Resource.MutableNode, aliases))
            {
                // Conditional creates may select existing IDs. JSON and reference indexes must both
                // reflect those final identities before the single core write.
                wrapper.Resource.InvalidateCaches();
                _stagedWrites[index] = (entryIndex, wrapper with
                {
                    SearchIndices = indexer.Extract((IElement)wrapper.Resource.ToElement(schemaProvider)).ToArray()
                });
            }
        }
    }

    public BundleEntryResponse CompleteResponse(
        int entryIndex,
        BundleEntryResponse response,
        IReadOnlyDictionary<string, string> aliases)
    {
        var writes = _stagedWrites.Where(write => write.EntryIndex == entryIndex).Take(2).ToArray();
        if (writes.Length == 1)
        {
            var resource = writes[0].Resource;
            response = response with
            {
                ResourceJson = response.ResourceJson == null || resource.IsDeleted ? response.ResourceJson : resource.Resource.SerializeToString(),
                LastModified = resource.Resource.Meta.LastUpdatedOffset
            };
        }
        if (aliases.Count > 0 && response.ResourceJson != null)
        {
            var json = JsonNode.Parse(response.ResourceJson);
            if (BundleReferencePreProcessor.RewriteReferenceAliases(json, aliases))
            {
                response = response with { ResourceJson = json!.ToJsonString() };
            }
        }
        return response;
    }

    public void CompleteWrites() => _writeChannel.Writer.TryComplete();
    public void CompleteWrites(Exception exception) => _writeChannel.Writer.TryComplete(exception);
    public async Task<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
        await _writeChannel.Reader.WaitToReadAsync(cancellationToken);

    public Task CommitAsync(IFhirVersionContext versionContext, CancellationToken cancellationToken = default) =>
        IsAtomic ? CommitAtomicAsync(versionContext, cancellationToken) : Task.CompletedTask;
}
