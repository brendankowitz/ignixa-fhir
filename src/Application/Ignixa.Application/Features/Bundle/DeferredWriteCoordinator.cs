using System.Globalization;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
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
    private readonly List<(int EntryIndex, ResourceWrapper Resource)> _stagedWrites = [];
    private readonly ConcurrentDictionary<int, bool> _createdEntries = new();

    private DeferredWriteCoordinator(
        int channelCapacity,
        IFhirRepositoryFactory repositoryFactory,
        IPartitionStrategy partitionStrategy,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DeferredWriteCoordinator> logger,
        bool atomic)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCapacity);
        _repositoryFactory = repositoryFactory;
        _partitionStrategy = partitionStrategy;
        _contextAccessor = contextAccessor;
        _logger = logger;
        IsAtomic = atomic;
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
            contextAccessor, logger, atomic);
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

    public async Task<List<Exception>> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var errors = new List<Exception>();
        if (!await _writeChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            return errors;
        }
        for (var i = 0; i < batchSize && _writeChannel.Reader.TryRead(out var operation); i++)
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

    public async Task CommitAtomicAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");
        var repository = await _repositoryFactory.GetRepositoryAsync(context.TenantId, cancellationToken);
        await ((IAtomicFhirRepository)repository).WriteTransactionAsync(
            _stagedWrites.Select(write => write.Resource).ToArray(), cancellationToken);
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

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        IsAtomic ? CommitAtomicAsync(cancellationToken) : Task.CompletedTask;
}
