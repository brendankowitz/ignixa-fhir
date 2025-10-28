using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'add' operations.
/// Adds a value to an array property or creates a new array with the value.
/// Uses FHIRPath evaluation for navigation.
/// </summary>
public class AddOperationExecutor : IOperationExecutor
{
    private readonly ILogger<AddOperationExecutor> _logger;
    private readonly FhirPathPatchHelper _fhirPathHelper;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Add;

    public AddOperationExecutor(
        ILogger<AddOperationExecutor> logger,
        FhirPathPatchHelper fhirPathHelper)
    {
        _logger = logger;
        _fhirPathHelper = fhirPathHelper;
    }

    public Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Add operation requires 'path'");
        }

        if (operation.Value == null)
        {
            throw new FhirPatchException("Add operation requires 'value'");
        }

        // Try to evaluate the path to see if it exists
        var matches = _fhirPathHelper.EvaluateToJsonNodes(resource, operation.Path).ToList();

        JsonObject targetParent;
        string propertyName;

        if (matches.Count == 0)
        {
            // Path doesn't exist - try to get parent path and create array
            // For now, use simplified approach - extract property name from path
            var lastDot = operation.Path.LastIndexOf('.');
            if (lastDot < 0)
            {
                throw new FhirPatchException($"Path '{operation.Path}' not found and cannot be created");
            }

            var parentPath = operation.Path.Substring(0, lastDot);
            propertyName = operation.Path.Substring(lastDot + 1);

            var parentMatches = _fhirPathHelper.EvaluateToJsonNodes(resource, parentPath).ToList();
            if (parentMatches.Count != 1 || parentMatches[0] is not JsonObject parentObj)
            {
                throw new FhirPatchException($"Parent path '{parentPath}' not found or is not an object");
            }

            targetParent = parentObj;
        }
        else
        {
            // Path exists - get its parent
            var targetJsonNode = matches[0];
            var parentInfo = FhirPathPatchHelper.GetParentAndProperty(targetJsonNode, resource.MutableNode);
            if (parentInfo == null)
            {
                throw new FhirPatchException($"Cannot find parent for path '{operation.Path}'");
            }

            (targetParent, propertyName) = parentInfo.Value;
        }

        var valueNode = FhirPathPatchHelper.SerializeValue(operation.Value);

        // Get existing value
        var existing = targetParent[propertyName];

        if (existing is JsonArray existingArray)
        {
            // Add to existing array
            existingArray.Add(valueNode);
        }
        else if (existing == null)
        {
            // Create new array with the value
            var newArray = new JsonArray { valueNode };
            targetParent[propertyName] = newArray;
        }
        else
        {
            throw new FhirPatchException($"Cannot add to non-array property '{propertyName}'");
        }

        _logger.LogDebug("Added value to {Path}", operation.Path);

        return Task.FromResult(resource);
    }
}
