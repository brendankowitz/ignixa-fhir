using System;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'move' operations.
/// Moves a value from source path to destination path (delete + add).
/// Uses IJsonNodeMutator for FHIRPath evaluation and delegates to Delete/Add executors.
/// </summary>
public class MoveOperationExecutor(
    ILogger<MoveOperationExecutor> logger,
    DeleteOperationExecutor deleteExecutor,
    AddOperationExecutor addExecutor,
    IJsonNodeMutator mutator) : IOperationExecutor
{
    public FhirPatchOperationType OperationType => FhirPatchOperationType.Move;

    public async Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Source))
        {
            throw new FhirPatchException("Move operation requires 'source'");
        }

        if (string.IsNullOrEmpty(operation.Destination))
        {
            throw new FhirPatchException("Move operation requires 'destination'");
        }

        try
        {
            // Get source value using IJsonNodeMutator
            var sourceValue = mutator.EvaluateSingle(resource, operation.Source);

            // Remove from source
            var deleteOp = new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Delete,
                Path = operation.Source,
            };
            resource = await deleteExecutor.ExecuteAsync(resource, deleteOp, cancellationToken);

            // Add to destination
            var addOp = new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Add,
                Path = operation.Destination,
                Value = sourceValue,
            };
            resource = await addExecutor.ExecuteAsync(resource, addOp, cancellationToken);

            logger.LogDebug("Moved value from {Source} to {Destination}", operation.Source, operation.Destination);
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return resource;
    }
}
