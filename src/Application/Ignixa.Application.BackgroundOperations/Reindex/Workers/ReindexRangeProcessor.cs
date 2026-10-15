using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.BackgroundOperations.Reindex.Workers;

public sealed class ReindexRangeProcessor(
    IReindexStoreFactory reindexStoreFactory,
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirVersionContext fhirVersionContext,
    ConformanceRefresher conformanceRefresher,
    IFhirRequestContextAccessor fhirContextAccessor)
{
    private const int MinimumWriteBatchSize = 10;

    public async Task<ReindexRangeOutput> ProcessAsync(
        ReindexRangeInput input,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(
            input.TenantId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive.");
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
        var previousContext = fhirContextAccessor.RequestContext;
        fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(
            input.TenantId,
            tenant,
            fhirVersion,
            input.ResourceType);
        try
        {
            // Catch up once before giving up on this attempt; the orchestration waits and retries the range.
            var handle = fhirVersionContext.GetDefinitionsHandle(fhirVersion, input.TenantId);
            if (handle.DefinitionsEventId < input.TargetEventId)
            {
                await conformanceRefresher.SynchronizeAsync(cancellationToken);
                handle = fhirVersionContext.GetDefinitionsHandle(fhirVersion, input.TenantId);
                if (handle.DefinitionsEventId < input.TargetEventId)
                {
                    return ReindexRangeOutput.DefinitionsNotReady(handle.DefinitionsEventId);
                }
            }

            var store = await reindexStoreFactory.GetReindexStoreAsync(
                input.TenantId,
                cancellationToken);

            long resourcesReindexed = 0;
            long conflicts = 0;
            long? afterSurrogateId = null;
            var writeBatchSize = input.MaximumNumberOfResourcesPerWrite;
            var failures = new List<ReindexFailedResource>();
            long failedResourceCount = 0;
            while (true)
            {
                var page = await store.ReadRangeAsync(
                    input.ResourceType,
                    input.StartSurrogateId,
                    input.EndSurrogateId,
                    writeBatchSize,
                    afterSurrogateId,
                    cancellationToken);
                if (page.Count == 0)
                {
                    break;
                }

                var extracted = new List<ReindexResource>(page.Count);
                foreach (var resource in page)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        extracted.Add(resource with
                        {
                            Resource = resource.Resource with
                            {
                                SearchIndices = handle.Indexer.Extract(
                                    (IElement)resource.Resource.Resource.ToElement(handle.SchemaProvider)).ToArray(),
                                DefinitionsEventId = handle.DefinitionsEventId,
                                FhirVersion = tenant.FhirVersion,
                                TenantId = input.TenantId
                            }
                        });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failedResourceCount++;
                        if (failures.Count < ReindexTenantProgress.FailedResourceSampleSize)
                        {
                            failures.Add(new ReindexFailedResource(
                                input.ResourceType,
                                resource.Resource.ResourceId,
                                ex.Message));
                        }
                    }
                }

                if (extracted.Count > 0)
                {
                    var offset = 0;
                    while (offset < extracted.Count)
                    {
                        var count = Math.Min(writeBatchSize, extracted.Count - offset);
                        var batch = extracted.GetRange(offset, count);
                        try
                        {
                            var updated = await store.UpdateSearchIndicesAsync(batch, cancellationToken);
                            resourcesReindexed += updated.Updated;
                            conflicts += updated.Conflicts;
                            offset += count;
                        }
                        catch (TimeoutException) when (writeBatchSize > MinimumWriteBatchSize)
                        {
                            writeBatchSize = Math.Max(MinimumWriteBatchSize, writeBatchSize / 2);
                        }
                    }
                }

                afterSurrogateId = page[^1].ResourceSurrogateId;
                if (input.QueryDelayIntervalInMilliseconds > 0)
                {
                    await Task.Delay(
                        input.QueryDelayIntervalInMilliseconds,
                        cancellationToken);
                }
            }

            return new ReindexRangeOutput(resourcesReindexed, conflicts, failures)
            {
                FailedResourceCount = failedResourceCount
            };
        }
        finally
        {
            fhirContextAccessor.RequestContext = previousContext;
        }
    }
}
