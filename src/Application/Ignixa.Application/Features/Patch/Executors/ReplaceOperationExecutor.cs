using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'replace' operations.
/// Replaces the value of a property or array element, with protection for immutable properties.
/// Uses IJsonNodeMutator for all mutation operations.
/// </summary>
public class ReplaceOperationExecutor(
    ILogger<ReplaceOperationExecutor> logger,
    IJsonNodeMutator mutator) : IOperationExecutor
{
    public FhirPatchOperationType OperationType => FhirPatchOperationType.Replace;

    public Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Replace operation requires 'path'");
        }

        if (operation.Value is null)
        {
            throw new FhirPatchException("Replace operation requires 'value'");
        }

        // Check for immutable properties
        if (ImmutablePathChecker.IsImmutablePath(operation.Path))
        {
            throw new FhirPatchException($"Cannot replace immutable property '{operation.Path}'");
        }

        try
        {
            // Use FHIRPath to verify the target path exists (PATCH replace requires existing element)
            var targetJsonNode = mutator.EvaluateSingle(resource, operation.Path, cancellationToken);

            var valueNode = JsonNodeMutator.SerializeValue(operation.Value)
                ?? throw new FhirPatchException("Failed to serialize value");

            // Check if we're replacing an array element or a property
            var parent = targetJsonNode.Parent;

            if (parent is JsonArray)
            {
                // Replacing an array element - use ReplaceArrayElement
                mutator.ReplaceArrayElement(resource, operation.Path, valueNode, cancellationToken);
                logger.LogDebug("Replaced array element at {Path}", operation.Path.SanitizeExpressionForLog());
            }
            else
            {
                // Replacing a property - use SetProperty with Replace mode
                mutator.SetProperty(resource, operation.Path, valueNode, PropertyMutationMode.Replace, cancellationToken);
                logger.LogDebug("Replaced property at {Path}", operation.Path.SanitizeExpressionForLog());
            }
        }
        catch (FhirPathResourceLimitExceededException)
        {
            // Derives from InvalidOperationException, but is not an invalid patch: FhirPatchEngine reports it as
            // too costly.
            throw;
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return Task.FromResult(resource);
    }
}
