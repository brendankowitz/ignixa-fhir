using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Applies FHIR Patch operations to a FHIR resource.
/// Uses in-place mutation of the internal JsonObject for efficiency.
/// </summary>
public class FhirPatchEngine
{
    private readonly ILogger<FhirPatchEngine> _logger;

    public FhirPatchEngine(ILogger<FhirPatchEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Apply patch operations to a resource using in-place mutation.
    /// </summary>
    public async Task<ResourceJsonNode> ApplyPatchAsync(
        ResourceJsonNode resource,
        FhirPatchOperation[] operations,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying {OperationCount} patch operations to {ResourceType}/{ResourceId}",
            operations.Length, resource.ResourceType, resource.Id);

        // Get the internal mutable JsonObject directly
        var jsonNode = resource.MutableNode;

        // Apply each operation in-place
        foreach (var operation in operations)
        {
            // Operations mutate jsonNode in-place, but we keep the reference for consistency
            _ = await ApplyOperationAsync(jsonNode, operation, cancellationToken);
        }

        _logger.LogDebug("Successfully applied {OperationCount} patch operations",
            operations.Length);

        // Return the same resource instance (mutations were applied in-place)
        return resource;
    }

    private Task<JsonNode> ApplyOperationAsync(
        JsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        return operation.Type switch
        {
            FhirPatchOperationType.Add => ApplyAdd(resource, operation),
            FhirPatchOperationType.Insert => ApplyInsert(resource, operation),
            FhirPatchOperationType.Delete => ApplyDelete(resource, operation),
            FhirPatchOperationType.Replace => ApplyReplace(resource, operation),
            FhirPatchOperationType.Move => ApplyMove(resource, operation),
            _ => throw new FhirPatchException($"Unknown operation type: {operation.Type}"),
        };
    }

    private Task<JsonNode> ApplyAdd(JsonNode resource, FhirPatchOperation operation)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Add operation requires 'path'");
        }

        if (operation.Value == null)
        {
            throw new FhirPatchException("Add operation requires 'value'");
        }

        // Parse path (simplified - just handle property access for now)
        // Example: "Patient.name" or "Patient.telecom"
        var target = NavigateToParent(resource, operation.Path);

        if (target == null)
        {
            throw new FhirPatchException($"Path '{operation.Path}' not found");
        }

        var propertyName = GetPropertyName(operation.Path);
        var valueNode = SerializeValue(operation.Value);

        // Get existing value
        var existing = target[propertyName];

        if (existing is JsonArray existingArray)
        {
            // Add to existing array
            existingArray.Add(valueNode);
        }
        else if (existing == null)
        {
            // Create new array with the value
            var newArray = new JsonArray { valueNode };
            target[propertyName] = newArray;
        }
        else
        {
            throw new FhirPatchException($"Cannot add to non-array property '{propertyName}'");
        }

        return Task.FromResult(resource);
    }

    private Task<JsonNode> ApplyInsert(JsonNode resource, FhirPatchOperation operation)
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

        var target = NavigateToParent(resource, operation.Path);
        if (target == null)
        {
            throw new FhirPatchException($"Path '{operation.Path}' not found");
        }

        var propertyName = GetPropertyName(operation.Path);
        var existing = target[propertyName];

        if (existing is not JsonArray existingArray)
        {
            throw new FhirPatchException($"Cannot insert into non-array property '{propertyName}'");
        }

        var index = operation.Index.Value;
        if (index < 0 || index > existingArray.Count)
        {
            throw new FhirPatchException($"Index {index} out of range (array length: {existingArray.Count})");
        }

        var valueNode = SerializeValue(operation.Value);
        existingArray.Insert(index, valueNode);

        return Task.FromResult(resource);
    }

    private Task<JsonNode> ApplyDelete(JsonNode resource, FhirPatchOperation operation)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            throw new FhirPatchException("Delete operation requires 'path'");
        }

        // Check for immutable properties
        if (IsImmutableProperty(operation.Path))
        {
            throw new FhirPatchException($"Cannot delete immutable property '{operation.Path}'");
        }

        // Handle array element deletion (e.g., "Patient.name[0]")
        if (operation.Path.Contains('[', StringComparison.Ordinal))
        {
            var (parentPath, index) = ParseArrayPath(operation.Path);
            var parent = NavigateToParent(resource, parentPath);
            if (parent == null)
            {
                throw new FhirPatchException($"Path '{parentPath}' not found");
            }

            var propertyName = GetPropertyName(parentPath);
            var array = parent[propertyName] as JsonArray;
            if (array == null)
            {
                throw new FhirPatchException($"Path '{parentPath}' is not an array");
            }

            if (index < 0 || index >= array.Count)
            {
                throw new FhirPatchException($"Index {index} out of range");
            }

            array.RemoveAt(index);
        }
        else
        {
            // Delete entire property
            var parent = NavigateToParent(resource, operation.Path);
            if (parent == null)
            {
                throw new FhirPatchException($"Path '{operation.Path}' not found");
            }

            var propertyName = GetPropertyName(operation.Path);
            if (parent is JsonObject obj)
            {
                obj.Remove(propertyName);
            }
        }

        return Task.FromResult(resource);
    }

    private Task<JsonNode> ApplyReplace(JsonNode resource, FhirPatchOperation operation)
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
        if (IsImmutableProperty(operation.Path))
        {
            throw new FhirPatchException($"Cannot replace immutable property '{operation.Path}'");
        }

        // Handle array element replacement (e.g., "Patient.name[0].family")
        if (operation.Path.Contains('[', StringComparison.Ordinal))
        {
            var target = Navigate(resource, operation.Path);
            if (target == null)
            {
                throw new FhirPatchException($"Path '{operation.Path}' not found");
            }

            // Replace the value at this location
            var parentPath = GetParentPath(operation.Path);
            var parent = Navigate(resource, parentPath);
            var propertyName = GetPropertyName(operation.Path);

            if (parent is JsonObject obj)
            {
                obj[propertyName] = SerializeValue(operation.Value);
            }
            else
            {
                throw new FhirPatchException($"Cannot replace value at '{operation.Path}'");
            }
        }
        else
        {
            // Replace simple property
            var parent = NavigateToParent(resource, operation.Path);
            if (parent == null)
            {
                throw new FhirPatchException($"Path '{operation.Path}' not found");
            }

            var propertyName = GetPropertyName(operation.Path);
            if (parent is JsonObject obj)
            {
                obj[propertyName] = SerializeValue(operation.Value);
            }
        }

        return Task.FromResult(resource);
    }

    private async Task<JsonNode> ApplyMove(JsonNode resource, FhirPatchOperation operation)
    {
        if (string.IsNullOrEmpty(operation.Source))
        {
            throw new FhirPatchException("Move operation requires 'source'");
        }

        if (string.IsNullOrEmpty(operation.Destination))
        {
            throw new FhirPatchException("Move operation requires 'destination'");
        }

        // Get source value
        var sourceValue = Navigate(resource, operation.Source);
        if (sourceValue == null)
        {
            throw new FhirPatchException($"Source path '{operation.Source}' not found");
        }

        // Remove from source
        var deleteOp = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = operation.Source,
        };
        resource = await ApplyDelete(resource, deleteOp);

        // Add to destination
        var addOp = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = operation.Destination,
            Value = sourceValue,
        };
        resource = await ApplyAdd(resource, addOp);

        return resource;
    }

    // Helper methods

    private JsonNode? NavigateToParent(JsonNode root, string path)
    {
        var parentPath = GetParentPath(path);
        return string.IsNullOrEmpty(parentPath) ? root : Navigate(root, parentPath);
    }

    private JsonNode? Navigate(JsonNode? current, string path)
    {
        if (current == null || string.IsNullOrEmpty(path))
        {
            return current;
        }

        var parts = path.Split('.');
        foreach (var part in parts)
        {
            if (part.Contains('[', StringComparison.Ordinal))
            {
                // Handle array indexing (e.g., "name[0]")
                var propertyName = part.Substring(0, part.IndexOf('[', StringComparison.Ordinal));
                var indexStart = part.IndexOf('[', StringComparison.Ordinal);
                var indexEnd = part.IndexOf(']', StringComparison.Ordinal);
                var indexStr = part.Substring(indexStart + 1, indexEnd - indexStart - 1);
                var index = int.Parse(indexStr);

                current = current?[propertyName];
                if (current is JsonArray array && index >= 0 && index < array.Count)
                {
                    current = array[index];
                }
                else
                {
                    return null;
                }
            }
            else
            {
                current = current?[part];
            }
        }

        return current;
    }

    private string GetParentPath(string path)
    {
        var lastDot = path.LastIndexOf('.');
        return lastDot > 0 ? path.Substring(0, lastDot) : string.Empty;
    }

    private string GetPropertyName(string path)
    {
        var lastDot = path.LastIndexOf('.');
        var propertyPart = lastDot > 0 ? path.Substring(lastDot + 1) : path;

        // Remove array index if present (e.g., "name[0]" → "name")
        if (propertyPart.Contains('[', StringComparison.Ordinal))
        {
            return propertyPart.Substring(0, propertyPart.IndexOf('[', StringComparison.Ordinal));
        }

        return propertyPart;
    }

    private (string parentPath, int index) ParseArrayPath(string path)
    {
        var indexStart = path.LastIndexOf('[');
        var indexEnd = path.LastIndexOf(']');
        var indexStr = path.Substring(indexStart + 1, indexEnd - indexStart - 1);
        var index = int.Parse(indexStr);
        var parentPath = path.Substring(0, indexStart);

        return (parentPath, index);
    }

    private bool IsImmutableProperty(string path)
    {
        // Check for immutable properties (case-insensitive)
        return path.Contains(".id", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".meta.versionid", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".meta.lastupdated", StringComparison.OrdinalIgnoreCase);
    }

    private JsonNode? SerializeValue(object value)
    {
        if (value is JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        var json = JsonSerializer.Serialize(value);
        return JsonNode.Parse(json);
    }
}
