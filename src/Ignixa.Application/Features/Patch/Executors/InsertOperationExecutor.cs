using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'insert' operations.
/// Inserts a value at a specific index in an array property.
/// Uses FHIRPath evaluation for navigation.
/// </summary>
public class InsertOperationExecutor : IOperationExecutor
{
    private readonly ILogger<InsertOperationExecutor> _logger;
    private readonly FhirPathPatchHelper _fhirPathHelper;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Insert;

    public InsertOperationExecutor(
        ILogger<InsertOperationExecutor> logger,
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
            throw new FhirPatchException("Insert operation requires 'path'");
        }

        if (operation.Value == null)
        {
            throw new FhirPatchException("Insert operation requires 'value'");
        }

        if (!operation.Index.HasValue)
        {
            throw new FhirPatchException("Insert operation requires 'index'");
        }

        // Try to evaluate the path to get the array
        var matches = _fhirPathHelper.EvaluateToJsonNodes(resource, operation.Path).ToList();

        JsonObject targetParent;
        string propertyName;

        if (matches.Count == 0)
        {
            // Path doesn't exist - try to get parent path and create array
            var lastDot = operation.Path.LastIndexOf('.');
            if (lastDot < 0)
            {
                throw new FhirPatchException($"Path '{operation.Path}' not found");
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
            // Path exists - get its parent using GetParentAndProperty helper
            // FHIRPath returns array elements, not the array container, so we need to traverse up
            var targetJsonNode = matches[0];
            var parentInfo = FhirPathPatchHelper.GetParentAndProperty(targetJsonNode, resource.MutableNode);
            if (parentInfo == null)
            {
                throw new FhirPatchException($"Cannot find parent for path '{operation.Path}'");
            }

            (targetParent, propertyName) = parentInfo.Value;
        }

        // Access the array property on the parent
        var existing = targetParent[propertyName];
        JsonArray targetArray;

        if (existing is JsonArray existingArray)
        {
            targetArray = existingArray;
        }
        else if (existing == null)
        {
            targetArray = new JsonArray();
            targetParent[propertyName] = targetArray;
        }
        else
        {
            throw new FhirPatchException($"Cannot insert into non-array property '{propertyName}'");
        }

        var index = operation.Index.Value;
        if (index < 0 || index > targetArray.Count)
        {
            throw new FhirPatchException($"Index {index} out of range (array length: {targetArray.Count})");
        }

        var valueNode = FhirPathPatchHelper.SerializeValue(operation.Value);
        targetArray.Insert(index, valueNode);

        _logger.LogDebug("Inserted value at {Path}[{Index}]", operation.Path, index);

        return Task.FromResult(resource);
    }
}
