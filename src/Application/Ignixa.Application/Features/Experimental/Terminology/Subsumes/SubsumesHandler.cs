using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Validation.Abstractions;
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
        var parameters = new SubsumesParameters(
            CodeA: request.CodeA,
            CodeB: request.CodeB,
            System: request.System,
            Version: request.Version);

        var result = await _terminologyService.SubsumesAsync(parameters, cancellationToken);

        // Convert to FHIR Parameters resource
        var parametersResponse = new
        {
            resourceType = "Parameters",
            parameter = new[]
            {
                new
                {
                    name = "outcome",
                    valueCode = result.Outcome
                }
            }
        };

        var jsonNode = JsonNode.Parse(JsonSerializer.Serialize(parametersResponse))
            ?? throw new InvalidOperationException("Failed to serialize Parameters");

        return new SubsumesQueryResult(jsonNode);
    }
}
