using System.Text.Json.Nodes;
using Ignixa.Domain.Exceptions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Terminology.Subsumes;

/// <summary>
/// Handles CodeSystem $subsumes operation.
/// </summary>
public class SubsumesHandler : IRequestHandler<SubsumesQuery, SubsumesQueryResult>
{
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<SubsumesHandler> _logger;

    public SubsumesHandler(
        ITerminologyService terminologyService,
        ILogger<SubsumesHandler> logger)
    {
        _terminologyService = terminologyService;
        _logger = logger;
    }

    /// <summary>
    /// Handles CodeSystem $subsumes operation.
    /// </summary>
    /// <param name="request">Subsumption test request with codeA, codeB, and system.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parameters resource with subsumption outcome as JsonNode.</returns>
    /// <remarks>
    /// TenantId is currently accepted for API consistency but not used in terminology operations.
    /// Terminology resources (ValueSet, CodeSystem, ConceptMap) are shared across tenants.
    /// Future enhancement: Pass TenantId to ITerminologyService for tenant-specific terminology isolation.
    /// </remarks>
    public async Task<SubsumesQueryResult> HandleAsync(
        SubsumesQuery request,
        CancellationToken cancellationToken)
    {
        var subsumes = new SubsumesRequest(
            new CodeRef(request.System, request.CodeA, request.Version),
            new CodeRef(request.System, request.CodeB, request.Version));

        var outcome = await _terminologyService.SubsumesAsync(subsumes, cancellationToken);
        if (outcome is SubsumesOutcome.Undetermined undetermined)
        {
            if (undetermined.Reason.Kind is UnknownKind.UnsupportedFilter or UnknownKind.NonEnumerableSystem)
            {
                throw new ResourceNotSupportedException(undetermined.Reason.Detail);
            }

            throw new BadRequestException($"Cannot determine subsumption: {undetermined.Reason.Detail}");
        }

        var parametersResponse = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray(new JsonObject
            {
                ["name"] = "outcome",
                ["valueCode"] = outcome.KnownRelation!.Value.ToFhirCode(),
            }),
        };

        return new SubsumesQueryResult(parametersResponse);
    }
}
