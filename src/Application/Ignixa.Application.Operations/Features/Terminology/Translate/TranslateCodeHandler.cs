using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.Terminology.Translate;

/// <summary>
/// Handles ConceptMap $translate operation.
/// </summary>
public class TranslateCodeHandler : IRequestHandler<TranslateCodeCommand, TranslateCodeResult>
{
    private readonly ITerminologyService _terminologyService;
    private readonly ILogger<TranslateCodeHandler> _logger;

    public TranslateCodeHandler(
        ITerminologyService terminologyService,
        ILogger<TranslateCodeHandler> logger)
    {
        _terminologyService = terminologyService;
        _logger = logger;
    }

    /// <summary>
    /// Handles ConceptMap $translate operation.
    /// </summary>
    /// <param name="request">Translation request with code, system, and target parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parameters resource with translation matches as JsonNode.</returns>
    /// <remarks>
    /// TenantId is currently accepted for API consistency but not used in terminology operations.
    /// Terminology resources (ValueSet, CodeSystem, ConceptMap) are shared across tenants.
    /// Future enhancement: Pass TenantId to ITerminologyService for tenant-specific terminology isolation.
    /// </remarks>
    public async Task<TranslateCodeResult> HandleAsync(
        TranslateCodeCommand request,
        CancellationToken cancellationToken)
    {
        var parameters = new TranslateParameters(
            Url: request.Url,
            ConceptMapVersion: request.ConceptMapVersion,
            Code: request.Code,
            System: request.System,
            Version: request.Version,
            Source: request.Source,
            Target: request.Target,
            TargetSystem: request.TargetSystem,
            Reverse: request.Reverse);

        var result = await _terminologyService.TranslateCodeAsync(parameters, cancellationToken);

        // Convert to FHIR Parameters resource
        var parametersList = new List<object>
        {
            new
            {
                name = "result",
                valueBoolean = result.Result
            }
        };

        if (result.Message != null)
        {
            parametersList.Add(new
            {
                name = "message",
                valueString = result.Message
            });
        }

        foreach (var match in result.Matches)
        {
            var parts = new List<object>
            {
                new { name = "equivalence", valueCode = match.Equivalence },
                new { name = "concept", valueCoding = new
                {
                    system = match.Concept.System,
                    code = match.Concept.Code,
                    display = match.Concept.Display
                }},
                new { name = "source", valueUri = match.Source }
            };

            if (match.Comment != null)
            {
                parts.Add(new { name = "comment", valueString = match.Comment });
            }

            parametersList.Add(new
            {
                name = "match",
                part = parts
            });
        }

        var parametersResponse = new
        {
            resourceType = "Parameters",
            parameter = parametersList
        };

        var jsonNode = JsonNode.Parse(JsonSerializer.Serialize(parametersResponse))
            ?? throw new InvalidOperationException("Failed to serialize Parameters");

        return new TranslateCodeResult(jsonNode);
    }
}
