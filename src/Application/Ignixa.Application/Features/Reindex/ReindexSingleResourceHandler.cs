using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;
using Medino;

namespace Ignixa.Application.Features.Reindex;

public sealed class ReindexSingleResourceHandler(
    IFhirRepositoryFactory repositoryFactory,
    IFhirRequestContextAccessor requestContextAccessor,
    IFhirVersionContext fhirVersionContext) : IRequestHandler<ReindexSingleResourceCommand, ReindexSingleResourceResult>
{
    private readonly IFhirRepositoryFactory _repositoryFactory =
        repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    private readonly IFhirRequestContextAccessor _requestContextAccessor =
        requestContextAccessor ?? throw new ArgumentNullException(nameof(requestContextAccessor));
    private readonly IFhirVersionContext _fhirVersionContext =
        fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));

    public async Task<ReindexSingleResourceResult> HandleAsync(
        ReindexSingleResourceCommand command,
        CancellationToken cancellationToken)
    {
        var requestContext = _requestContextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available.");
        var repository = await _repositoryFactory.GetRepositoryAsync(
            requestContext.TenantId,
            cancellationToken);
        if (repository is not IReindexStore store)
        {
            return new ReindexSingleResourceProviderUnavailableResult();
        }

        var current = await store.ReadCurrentResourceAsync(
            command.ResourceType,
            command.ResourceId,
            cancellationToken);
        if (current.Resource is null)
        {
            return current.IsDeleted
                ? new ReindexSingleResourceDeletedResult()
                : new ReindexSingleResourceNotFoundResult();
        }

        var resource = current.Resource;
        var tenantConfiguration = requestContext.TenantConfiguration
            ?? throw new InvalidOperationException("FHIR tenant configuration not available.");
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(
            tenantConfiguration.FhirVersion);
        var handle = _fhirVersionContext.GetDefinitionsHandle(
            fhirVersion,
            requestContext.TenantId);
        var indexedResource = resource.Resource with
        {
            SearchIndices = handle.Indexer.Extract(
                (IElement)resource.Resource.Resource.ToElement(handle.SchemaProvider)).ToArray(),
            DefinitionsEventId = handle.DefinitionsEventId,
            FhirVersion = tenantConfiguration.FhirVersion,
            TenantId = requestContext.TenantId
        };
        var indices = indexedResource.SearchIndices!
            .Cast<Ignixa.Search.Indexing.SearchIndexEntry>()
            .ToArray();
        if (!command.Persist)
        {
            return new ReindexSingleResourceCompletedResult(indices, false);
        }

        var update = await store.UpdateSearchIndicesAsync(
            [resource with { Resource = indexedResource }],
            cancellationToken);
        return new ReindexSingleResourceCompletedResult(indices, update.Conflicts != 0);
    }
}
