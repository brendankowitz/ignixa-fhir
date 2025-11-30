using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'add' operations.
/// Adds a value to an array property or creates a new array with the value.
/// Uses IJsonNodeMutator for all mutation operations.
/// </summary>
public class AddOperationExecutor(
    ILogger<AddOperationExecutor> logger,
    IJsonNodeMutator mutator) : IOperationExecutor
{
    public FhirPatchOperationType OperationType => FhirPatchOperationType.Add;

    public Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Add operation requires 'path'");
        }

        if (operation.Value is null)
        {
            throw new FhirPatchException("Add operation requires 'value'");
        }

        try
        {
            // Validate parent path exists (FHIR PATCH requires parent to exist for Add)
            var lastDot = operation.Path.LastIndexOf('.');
            if (lastDot > 0)
            {
                var parentPath = operation.Path[..lastDot];
                var parentMatches = mutator.Evaluate(resource, parentPath).ToList();
                if (parentMatches.Count == 0)
                {
                    throw new FhirPatchException($"Parent path '{parentPath}' not found");
                }
            }

            // Try to evaluate the path to see if it exists and validate we can add to it
            var matches = mutator.Evaluate(resource, operation.Path).ToList();

            // If path exists and is not an array element context, validate it's an array
            if (matches.Count > 0)
            {
                var existingNode = matches[0];
                // If the existing value's parent is not a JsonArray, and the value itself is not a JsonArray,
                // then we cannot add to it (it's a single-valued property)
                if (existingNode.Parent is not JsonArray && existingNode is not JsonArray)
                {
                    // Extract property name from path for better error message
                    var propertyName = operation.Path.Split('.')[^1];
                    throw new FhirPatchException($"Cannot add to non-array property '{propertyName}'");
                }
            }

            var valueNode = JsonNodeMutator.SerializeValue(operation.Value)
                ?? throw new FhirPatchException("Failed to serialize value");

            // Use IJsonNodeMutator with Append mode
            // This handles:
            // - Path doesn't exist: creates array with value
            // - Path exists as array: appends to array
            // - Path exists as single value: converts to array with old + new values
            mutator.SetProperty(resource, operation.Path, valueNode, PropertyMutationMode.Append);

            logger.LogDebug("Added value to {Path}", operation.Path);
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return Task.FromResult(resource);
    }
}
