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
/// Executes FHIRPath Patch 'add' operations: appends to a repeating element, or sets an absent
/// non-repeating one when the add was resolved from a 'name' part.
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

        var valueNode = JsonNodeMutator.SerializeValue(operation.Value)
            ?? throw new FhirPatchException("Failed to serialize value");

        if (operation.TargetParent is { } parent)
        {
            AddToResolvedParent(parent, operation.TargetProperty!, operation.TargetIsCollection == true, valueNode);
            logger.LogDebug("Added value at {Path}", operation.Path);
            return Task.FromResult(resource);
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

            // Path-only add: cardinality is unknown, so infer it from the existing JSON and reject single-valued targets.
            var matches = mutator.Evaluate(resource, operation.Path).ToList();
            if (matches.Count > 0)
            {
                var existingNode = matches[0];
                if (existingNode.Parent is not JsonArray && existingNode is not JsonArray)
                {
                    var propertyName = operation.Path.Split('.')[^1];
                    throw new FhirPatchException($"Cannot add to non-array property '{propertyName}'");
                }
            }

            // Append mode creates the array when the path is absent and appends when it exists.
            mutator.SetProperty(resource, operation.Path, valueNode, PropertyMutationMode.Append);

            logger.LogDebug("Added value to {Path}", operation.Path);
        }
        catch (InvalidOperationException ex)
        {
            throw new FhirPatchException(ex.Message, ex);
        }

        return Task.FromResult(resource);
    }

    /// <summary>
    /// Writes into the exact parent object the resolver selected, so indexed or filtered parent paths
    /// (e.g. Patient.name[0]) are honored rather than re-derived from the path text.
    /// </summary>
    private static void AddToResolvedParent(JsonObject parent, string property, bool repeats, JsonNode value)
    {
        if (!repeats)
        {
            if (parent.ContainsKey(property))
            {
                throw new FhirPatchException($"Cannot add '{property}': the element does not repeat and already has a value");
            }

            parent[property] = value;
            return;
        }

        switch (parent[property])
        {
            case null:
                parent[property] = new JsonArray(value);
                break;
            case JsonArray items:
                items.Add(value);
                break;
            default:
                throw new FhirPatchException($"Cannot add to '{property}': the existing value is not an array");
        }
    }
}
