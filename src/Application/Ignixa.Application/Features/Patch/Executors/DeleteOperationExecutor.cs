using System;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch.Validation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'delete' operations.
/// Deletes a property or array element, with protection for immutable properties.
/// Uses IJsonNodeMutator for all mutation operations.
/// </summary>
public class DeleteOperationExecutor(
    ILogger<DeleteOperationExecutor> logger,
    IJsonNodeMutator mutator) : IOperationExecutor
{
    public FhirPatchOperationType OperationType => FhirPatchOperationType.Delete;

    public Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Delete operation requires 'path'");
        }

        // Check for immutable properties
        if (ImmutablePathChecker.IsImmutablePath(operation.Path))
        {
            throw new FhirPatchException($"Cannot delete immutable property '{operation.Path}'");
        }

        try
        {
            mutator.DeleteProperty(resource, operation.Path);
            logger.LogDebug("Deleted element at {Path}", operation.Path);
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return Task.FromResult(resource);
    }
}
