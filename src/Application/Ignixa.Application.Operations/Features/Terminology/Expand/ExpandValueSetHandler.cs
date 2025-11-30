using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Terminology.Expand;

/// <summary>
/// Handles ValueSet $expand operation.
/// </summary>
public class ExpandValueSetHandler : IRequestHandler<ExpandValueSetQuery, ExpandValueSetResult>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<ExpandValueSetHandler> _logger;

    public ExpandValueSetHandler(
        ITerminologyService terminologyService,
        ILogger<ExpandValueSetHandler> logger)
    {
        _terminologyService = terminologyService;
        _logger = logger;
    }

    /// <summary>
    /// Handles ValueSet $expand operation.
    /// </summary>
    /// <param name="request">Expansion request with URL, filter, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expanded ValueSet resource as JsonNode.</returns>
    /// <remarks>
    /// TenantId is currently accepted for API consistency but not used in terminology operations.
    /// Terminology resources (ValueSet, CodeSystem, ConceptMap) are shared across tenants.
    /// Future enhancement: Pass TenantId to ITerminologyService for tenant-specific terminology isolation.
    /// </remarks>
    public async Task<ExpandValueSetResult> HandleAsync(
        ExpandValueSetQuery request,
        CancellationToken cancellationToken)
    {
        var parameters = new ExpansionParameters(
            Url: request.Url,
            Filter: request.Filter,
            Count: request.Count,
            Offset: request.Offset,
            IncludeDesignations: request.IncludeDesignations);

        var result = await _terminologyService.ExpandValueSetAsync(parameters, cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException($"ValueSet '{request.Url}' not found or not expanded");
        }

        // Map ExpandResult to FHIR ValueSet resource
        var valueSetJson = new
        {
            resourceType = "ValueSet",
            url = request.Url,
            expansion = new
            {
                identifier = result.Identifier,
                timestamp = result.Timestamp.ToString("o"),
                total = result.Total,
                offset = result.Offset,
                contains = result.Contains.Select(c => new
                {
                    system = c.System,
                    code = c.Code,
                    display = c.Display,
                    version = c.Version,
                    inactive = c.Inactive
                }).ToList()
            }
        };

        var jsonNode = JsonNode.Parse(JsonSerializer.Serialize(valueSetJson, SerializerOptions))
            ?? throw new InvalidOperationException("Failed to serialize ValueSet");

        return new ExpandValueSetResult(jsonNode);
    }
}
