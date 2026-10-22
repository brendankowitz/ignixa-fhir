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
            var message = $"Cannot determine subsumption: {string.Join("; ", undetermined.Reasons.Select(reason => reason.Detail))}";

            // Unsupported content is a 422 only when it is the whole story; any other reason (an unknown code,
            // system, or version) is something the client can correct, so a mix is reported as a 400.
            if (undetermined.Reasons.All(reason => reason.Kind is UnknownKind.UnsupportedFilter or UnknownKind.NonEnumerableSystem))
            {
                throw new TerminologyOperationNotSupportedException(message);
            }

            throw new BadRequestException(message);
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
