using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'replace' operations.
/// Replaces the value of a property or array element, with protection for immutable properties.
/// Uses FHIRPath evaluation for navigation.
/// </summary>
public class ReplaceOperationExecutor : IOperationExecutor
{
    private readonly ILogger<ReplaceOperationExecutor> _logger;
    private readonly FhirPathPatchHelper _fhirPathHelper;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Replace;

    public ReplaceOperationExecutor(
        ILogger<ReplaceOperationExecutor> logger,
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
            throw new FhirPatchException("Replace operation requires 'path'");
        }

        if (operation.Value == null)
        {
            throw new FhirPatchException("Replace operation requires 'value'");
        }

        // Check for immutable properties
        if (FhirPathPatchHelper.IsImmutablePath(operation.Path))
        {
            throw new FhirPatchException($"Cannot replace immutable property '{operation.Path}'");
        }

        // Use FHIRPath to evaluate the target path
        var targetJsonNode = _fhirPathHelper.EvaluateToSingleJsonNode(resource, operation.Path);

        var valueNode = FhirPathPatchHelper.SerializeValue(operation.Value);

        // Check if we're replacing an array element or a property
        var parent = targetJsonNode.Parent;

        if (parent is JsonArray parentArray)
        {
            // Replacing an array element - find the index and replace at that position
            var index = parentArray.IndexOf(targetJsonNode);
            if (index < 0)
            {
                throw new FhirPatchException($"Cannot find index of target element in array at path '{operation.Path}'");
            }

            parentArray[index] = valueNode;
            _logger.LogDebug("Replaced array element at {Path} (index {Index})", operation.Path, index);
        }
        else
        {
            // Replacing a property - get the parent object and property name
            var parentInfo = FhirPathPatchHelper.GetParentAndProperty(targetJsonNode, resource.MutableNode);
            if (parentInfo == null)
            {
                throw new FhirPatchException($"Cannot find parent for path '{operation.Path}'");
            }

            var (parentObj, propertyName) = parentInfo.Value;
            parentObj[propertyName] = valueNode;
            _logger.LogDebug("Replaced property at {Path}", operation.Path);
        }

        return Task.FromResult(resource);
    }
}
