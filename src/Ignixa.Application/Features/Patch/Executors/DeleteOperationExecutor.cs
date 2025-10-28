using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'delete' operations.
/// Deletes a property or array element, with protection for immutable properties.
/// Uses FHIRPath evaluation for navigation.
/// </summary>
public class DeleteOperationExecutor : IOperationExecutor
{
    private readonly ILogger<DeleteOperationExecutor> _logger;
    private readonly FhirPathPatchHelper _fhirPathHelper;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Delete;

    public DeleteOperationExecutor(
        ILogger<DeleteOperationExecutor> logger,
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
            throw new FhirPatchException("Delete operation requires 'path'");
        }

        // Check for immutable properties
        if (FhirPathPatchHelper.IsImmutablePath(operation.Path))
        {
            throw new FhirPatchException($"Cannot delete immutable property '{operation.Path}'");
        }

        // Use FHIRPath to evaluate the target path
        var targetJsonNode = _fhirPathHelper.EvaluateToSingleJsonNode(resource, operation.Path);

        // Check if the target is an array element
        if (targetJsonNode.Parent is JsonArray array)
        {
            // Remove from array
            var index = array.IndexOf(targetJsonNode);
            if (index >= 0)
            {
                array.RemoveAt(index);
                _logger.LogDebug("Deleted array element at {Path}", operation.Path);
            }
            else
            {
                throw new FhirPatchException($"Cannot find element in array at path '{operation.Path}'");
            }
        }
        else
        {
            // Remove property from parent object
            var parentInfo = FhirPathPatchHelper.GetParentAndProperty(targetJsonNode, resource.MutableNode);
            if (parentInfo == null)
            {
                throw new FhirPatchException($"Cannot find parent for path '{operation.Path}'");
            }

            var (parent, propertyName) = parentInfo.Value;
            parent.Remove(propertyName);
            _logger.LogDebug("Deleted property {Path}", operation.Path);
        }

        return Task.FromResult(resource);
    }
}
