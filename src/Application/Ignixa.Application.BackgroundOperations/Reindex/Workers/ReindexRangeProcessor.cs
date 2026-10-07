using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.BackgroundOperations.Reindex.Workers;

public sealed class ReindexRangeProcessor(
    IFhirRepositoryFactory repositoryFactory,
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirVersionContext fhirVersionContext)
{
    private readonly IFhirRepositoryFactory _repositoryFactory =
        repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    private readonly ITenantConfigurationStore _tenantConfigurationStore =
        tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
    private readonly IFhirVersionContext _fhirVersionContext =
        fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));

    public async Task<ReindexRangeOutput> ProcessAsync(
        ReindexRangeInput input,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantConfigurationStore.GetTenantConfigurationAsync(
            input.TenantId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive.");
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
        var handle = _fhirVersionContext.GetDefinitionsHandle(fhirVersion, input.TenantId);
        if (handle.DefinitionsEventId < input.TargetEventId)
        {
            throw new ReindexDefinitionsNotReadyException(
                handle.DefinitionsEventId,
                input.TargetEventId);
        }

        var repository = await _repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            cancellationToken);
        if (repository is not IReindexStore store)
        {
            throw new ReindexProviderNotSupportedException(input.TenantId);
        }

        long resourcesRead = 0;
        long resourcesReindexed = 0;
        long conflicts = 0;
        long? afterSurrogateId = null;
        var failures = new List<ReindexFailedResource>();
        while (true)
        {
            var page = await store.ReadRangeAsync(
                input.ResourceType,
                input.StartSurrogateId,
                input.EndSurrogateId,
                input.MaximumNumberOfResourcesPerWrite,
                afterSurrogateId,
                cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            resourcesRead += page.Count;
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
                    if (failures.Count < 100)
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
                var updated = await store.UpdateSearchIndicesAsync(extracted, cancellationToken);
                resourcesReindexed += updated.Updated;
                conflicts += updated.Conflicts;
            }

            afterSurrogateId = page[^1].ResourceSurrogateId;
            if (input.QueryDelayIntervalInMilliseconds > 0)
            {
                await Task.Delay(
                    input.QueryDelayIntervalInMilliseconds,
                    cancellationToken);
            }
        }

        return new ReindexRangeOutput(
            resourcesRead,
            resourcesReindexed,
            conflicts,
            failures);
    }
}
