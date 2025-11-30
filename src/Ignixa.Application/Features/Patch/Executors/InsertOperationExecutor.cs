using System;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'insert' operations.
/// Inserts a value at a specific index in an array property.
/// Uses IJsonNodeMutator for all mutation operations.
/// </summary>
public class InsertOperationExecutor(
    ILogger<InsertOperationExecutor> logger,
    IJsonNodeMutator mutator) : IOperationExecutor
{
    public FhirPatchOperationType OperationType => FhirPatchOperationType.Insert;

    public Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Insert operation requires 'path'");
        }

        if (operation.Value is null)
        {
            throw new FhirPatchException("Insert operation requires 'value'");
        }

        if (!operation.Index.HasValue)
        {
            throw new FhirPatchException("Insert operation requires 'index'");
        }

        try
        {
            var valueNode = JsonNodeMutator.SerializeValue(operation.Value)
                ?? throw new FhirPatchException("Failed to serialize value");

            mutator.InsertIntoArray(resource, operation.Path, valueNode, operation.Index.Value);
            logger.LogDebug("Inserted value at {Path}[{Index}]", operation.Path, operation.Index.Value);
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return Task.FromResult(resource);
    }
}
