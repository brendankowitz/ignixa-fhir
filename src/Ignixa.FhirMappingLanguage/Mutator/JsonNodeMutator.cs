using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Service for mutating ResourceJsonNode properties using FHIRPath navigation.
/// Extracted from PATCH operations to enable Transform operations to reuse the same mutation logic.
/// Handles array vs single value detection, primitive vs complex types, and intermediate object creation.
/// Request-aware via schema provider factory function.
/// </summary>
public class JsonNodeMutator : IJsonNodeMutator
{
    private readonly FhirPathEvaluator _evaluator;
    private readonly FhirPathParser _parser;
    private readonly Func<ISchema> _schemaProviderFactory;

    public JsonNodeMutator(
        FhirPathEvaluator evaluator,
        FhirPathParser parser,
        Func<ISchema> schemaProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(schemaProviderFactory);

        _evaluator = evaluator;
        _parser = parser;
        _schemaProviderFactory = schemaProviderFactory;
    }

    /// <inheritdoc />
    public void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        IElement value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);
        ArgumentNullException.ThrowIfNull(value);

        // Serialize IElement to JsonNode
        var serializedValue = SerializeValue(value);
        if (serializedValue is null)
        {
            throw new InvalidOperationException(
                $"Failed to serialize IElement to JsonNode for path '{fhirPathExpression}'");
        }

        // Delegate to JsonNode overload
        SetProperty(resource, fhirPathExpression, serializedValue, mode);
    }

    /// <inheritdoc />
    public void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);
        ArgumentNullException.ThrowIfNull(value);

        // 1. Evaluate FHIRPath to get existing nodes (may be empty)
        var existingNodes = EvaluateToJsonNodes(resource, fhirPathExpression).ToList();

        // 2. Determine effective mode based on existing property structure or schema
        PropertyMutationMode effectiveMode = mode;
        if (mode == PropertyMutationMode.AutoDetect)
        {
            if (existingNodes.Count > 0)
            {
                // Check if existing property is an array
                var firstNode = existingNodes[0];
                effectiveMode = firstNode.Parent is JsonArray
                    ? PropertyMutationMode.Append
                    : PropertyMutationMode.Replace;
            }
            else
            {
                // No existing value - check schema to determine if property is an array
                // Extract property name from FHIRPath (e.g., "Patient.name" -> "name")
                var parts = fhirPathExpression.Split('.');
                if (parts.Length >= 2)
                {
                    var propertyName = parts[^1]; // Last part
                    var resourceType = parts[0]; // First part

                    // Get type definition from schema (request-aware via factory)
                    var structureProvider = _schemaProviderFactory();
                    var typeDefinition = structureProvider.GetTypeDefinition(resourceType);
                    if (typeDefinition is not null)
                    {
                        // Find child property by name
                        var propertyDefinition = typeDefinition.Children.FirstOrDefault(c => c.Info.Name == propertyName);
                        if (propertyDefinition != null)
                        {
                            // Use IsCollection to determine if property is an array (max cardinality > 1)
                            effectiveMode = propertyDefinition.IsCollection
                                ? PropertyMutationMode.Append
                                : PropertyMutationMode.Replace;
                        }
                        else
                        {
                            // Property not found in schema - default to Replace
                            effectiveMode = PropertyMutationMode.Replace;
                        }
                    }
                    else
                    {
                        // No schema info - default to Replace
                        effectiveMode = PropertyMutationMode.Replace;
                    }
                }
                else
                {
                    effectiveMode = PropertyMutationMode.Replace;
                }
            }
        }

        // 3. Execute mutation based on effective mode
        if (effectiveMode == PropertyMutationMode.Append)
        {
            AppendToArray(resource, fhirPathExpression, value);
        }
        else
        {
            ReplaceValue(resource, fhirPathExpression, value);
        }
    }

    /// <inheritdoc />
    public JsonNode EnsurePropertyPath(
        ResourceJsonNode resource,
        string fhirPathExpression)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);

        // Parse path: "Patient.contact.name" → ["Patient", "contact", "name"]
        var parts = fhirPathExpression.Split('.');
        if (parts.Length < 2)
        {
            throw new ArgumentException(
                $"FHIRPath expression must contain at least resource type and property: '{fhirPathExpression}'",
                nameof(fhirPathExpression));
        }

        JsonNode current = resource.MutableNode;

        // Walk path, creating missing objects
        // Skip resource type (first part) and final property (last part)
        for (int i = 1; i < parts.Length; i++)
        {
            var part = parts[i];

            // For last part, return the parent so caller can set the property
            if (i == parts.Length - 1)
            {
                return current;
            }

            var currentObj = current as JsonObject;
            if (currentObj is null)
            {
                throw new InvalidOperationException(
                    $"Path segment '{parts[i - 1]}' in '{fhirPathExpression}' is not a JsonObject");
            }

            // Create intermediate object if missing
            if (!currentObj.ContainsKey(part))
            {
                currentObj[part] = new JsonObject();
            }

            current = currentObj[part]!;
        }

        return current;
    }

    /// <summary>
    /// Evaluate FHIRPath expression and return matching JsonNodes.
    /// </summary>
    /// <param name="resource">Resource to evaluate against</param>
    /// <param name="fhirPathExpression">FHIRPath expression (e.g., "Patient.name.where(use='official')")</param>
    /// <returns>Matching JsonNodes extracted via Meta</returns>
    private IEnumerable<JsonNode> EvaluateToJsonNodes(ResourceJsonNode resource, string fhirPathExpression)
    {
        // 1. Parse FHIRPath expression
        var expression = _parser.Parse(fhirPathExpression);

        // 2. Convert ResourceJsonNode to ISourceNode (with annotations) - uses request-aware schema provider via factory
        var structureProvider = _schemaProviderFactory();
        var typedElement = resource.ToElement(structureProvider);

        // 3. Evaluate FHIRPath expression
        var matches = _evaluator.Evaluate(typedElement, expression);

        // 4. Extract JsonNodes using Meta<T>
        foreach (var match in matches)
        {
            var jsonNode = match.Meta<JsonNode>();
            if (jsonNode is not null)
            {
                yield return jsonNode;
            }
        }
    }

    /// <summary>
    /// Append value to array at the specified path.
    /// Creates array if it doesn't exist.
    /// </summary>
    private void AppendToArray(ResourceJsonNode resource, string fhirPathExpression, JsonNode value)
    {
        // Try to evaluate to existing nodes
        var existingNodes = EvaluateToJsonNodes(resource, fhirPathExpression).ToList();

        if (existingNodes.Count > 0)
        {
            // Find the array (parent of first node)
            var firstNode = existingNodes[0];
            if (firstNode.Parent is JsonArray existingArray)
            {
                // Deep clone to avoid "node already has a parent" error
                var clonedValue = JsonNode.Parse(value.ToJsonString());
                existingArray.Add(clonedValue);
                return;
            }

            // Existing property is not an array - need to find parent and convert to array
            var (parentObj, propertyName) = GetParentAndProperty(firstNode, resource.MutableNode)
                ?? throw new InvalidOperationException(
                    $"Cannot find parent for FHIRPath '{fhirPathExpression}' to convert to array");

            // Convert single value to array with both old and new values
            var clonedExisting = JsonNode.Parse(firstNode.ToJsonString());
            var clonedNew = JsonNode.Parse(value.ToJsonString());
            var newArray = new JsonArray { clonedExisting, clonedNew };
            parentObj[propertyName] = newArray;
        }
        else
        {
            // No existing value - create new array with single element
            // Need to find where to place it
            var parent = EnsurePropertyPath(resource, fhirPathExpression);
            var propertyName = fhirPathExpression.Split('.')[^1]; // Last part

            var clonedValue = JsonNode.Parse(value.ToJsonString());
            var newArray = new JsonArray { clonedValue };
            parent[propertyName] = newArray;
        }
    }

    /// <summary>
    /// Replace value at the specified path.
    /// Creates property if it doesn't exist.
    /// </summary>
    private void ReplaceValue(ResourceJsonNode resource, string fhirPathExpression, JsonNode value)
    {
        // Try to evaluate to existing node
        var existingNodes = EvaluateToJsonNodes(resource, fhirPathExpression).ToList();

        if (existingNodes.Count > 0)
        {
            // Replace existing value
            var targetNode = existingNodes[0];
            var (parentObj, propertyName) = GetParentAndProperty(targetNode, resource.MutableNode)
                ?? throw new InvalidOperationException(
                    $"Cannot find parent for FHIRPath '{fhirPathExpression}' to replace value");

            // Deep clone to avoid "node already has a parent" error
            var clonedValue = JsonNode.Parse(value.ToJsonString());
            parentObj[propertyName] = clonedValue;
        }
        else
        {
            // No existing value - create new property
            var parent = EnsurePropertyPath(resource, fhirPathExpression);
            var propertyName = fhirPathExpression.Split('.')[^1]; // Last part

            // Deep clone to avoid "node already has a parent" error
            var clonedValue = JsonNode.Parse(value.ToJsonString());
            parent[propertyName] = clonedValue;
        }
    }

    /// <summary>
    /// Get the parent JsonObject and property name for a given JsonNode.
    /// </summary>
    /// <param name="jsonNode">The JsonNode to find the parent of</param>
    /// <param name="root">The root JsonNode to search from</param>
    /// <returns>Tuple of (parent JsonObject, property name), or null if parent not found</returns>
    private static (JsonObject parent, string propertyName)? GetParentAndProperty(JsonNode jsonNode, JsonNode root)
    {
        // Get the parent from the JsonNode hierarchy
        var parent = jsonNode.Parent;

        if (parent is JsonObject parentObj)
        {
            // Find the property name by searching the parent's properties
            foreach (var kvp in parentObj)
            {
                if (ReferenceEquals(kvp.Value, jsonNode))
                {
                    return (parentObj, kvp.Key);
                }
            }
        }
        else if (parent is JsonArray parentArray)
        {
            // For array elements, we need to find the array's parent
            var arrayParent = parentArray.Parent;
            if (arrayParent is JsonObject arrayParentObj)
            {
                foreach (var kvp in arrayParentObj)
                {
                    if (ReferenceEquals(kvp.Value, parentArray))
                    {
                        return (arrayParentObj, kvp.Key);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Serialize IElement to JsonNode.
    /// Handles both primitive values and complex objects.
    /// </summary>
    /// <param name="element">The IElement to serialize</param>
    /// <returns>JsonNode representation of the element</returns>
    private JsonNode? SerializeValue(IElement element)
    {
        // Check if element has a primitive value
        if (element.Value is not null)
        {
            return SerializePrimitive(element.Value);
        }

        // Check if element has JsonNode metadata - clone it to avoid "node already has parent" errors
        var jsonNode = element.Meta<JsonNode>();
        if (jsonNode is not null)
        {
            // Deep clone by serializing and deserializing
            return JsonNode.Parse(jsonNode.ToJsonString());
        }

        // Complex object - recursively build from children
        return SerializeComplexElement(element);
    }

    /// <summary>
    /// Serialize a primitive value to JsonNode.
    /// </summary>
    /// <param name="value">The primitive value</param>
    /// <returns>JsonValue representation</returns>
    private static JsonNode? SerializePrimitive(object value)
    {
        return value switch
        {
            string s => JsonValue.Create(s),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            bool b => JsonValue.Create(b),
            decimal d => JsonValue.Create(d),
            double db => JsonValue.Create(db),
            float f => JsonValue.Create(f),
            DateTime dt => JsonValue.Create(dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK")),
            DateTimeOffset dto => JsonValue.Create(dto.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK")),
            _ => JsonNode.Parse(JsonSerializer.Serialize(value))
        };
    }

    /// <summary>
    /// Serialize a complex IElement to JsonObject by recursively processing children.
    /// </summary>
    /// <param name="element">The complex IElement</param>
    /// <returns>JsonObject representation</returns>
    private JsonNode SerializeComplexElement(IElement element)
    {
        var obj = new JsonObject();

        foreach (var child in element.Children())
        {
            var childValue = child.Value is not null
                ? SerializePrimitive(child.Value)
                : SerializeComplexElement(child);

            // Handle array properties (multiple children with same name)
            if (obj.ContainsKey(child.Name))
            {
                // Convert to array or append to existing array
                if (obj[child.Name] is JsonArray existingArray)
                {
                    existingArray.Add(childValue);
                }
                else
                {
                    // First duplicate - convert single value to array
                    var existingValue = obj[child.Name];
                    obj[child.Name] = new JsonArray { existingValue, childValue };
                }
            }
            else
            {
                obj[child.Name] = childValue;
            }
        }

        return obj;
    }

    #region IJsonNodeMutator New Methods (PATCH Operation Support)

    /// <inheritdoc />
    public void DeleteProperty(ResourceJsonNode resource, string fhirPathExpression)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);

        var targetNode = EvaluateSingle(resource, fhirPathExpression);

        // Check if array element or property
        if (targetNode.Parent is JsonArray array)
        {
            // Remove from array
            var index = array.IndexOf(targetNode);
            if (index >= 0)
            {
                array.RemoveAt(index);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot find element in array at path '{fhirPathExpression}'");
            }
        }
        else
        {
            // Remove property from parent object
            var parentInfo = GetParentAndProperty(targetNode, resource.MutableNode);
            if (parentInfo is null)
            {
                throw new InvalidOperationException(
                    $"Cannot find parent for path '{fhirPathExpression}'");
            }

            var (parent, propertyName) = parentInfo.Value;
            parent.Remove(propertyName);
        }
    }

    /// <inheritdoc />
    public void InsertIntoArray(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value,
        int index)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);
        ArgumentNullException.ThrowIfNull(value);

        // Try to evaluate the path to get the array
        var matches = Evaluate(resource, fhirPathExpression).ToList();

        JsonObject targetParent;
        string propertyName;

        if (matches.Count == 0)
        {
            // Path doesn't exist - try to get parent path and create array
            var lastDot = fhirPathExpression.LastIndexOf('.');
            if (lastDot < 0)
            {
                throw new InvalidOperationException(
                    $"Path '{fhirPathExpression}' not found and cannot determine parent");
            }

            var parentPath = fhirPathExpression[..lastDot];
            propertyName = fhirPathExpression[(lastDot + 1)..];

            var parentMatches = Evaluate(resource, parentPath).ToList();
            if (parentMatches.Count != 1 || parentMatches[0] is not JsonObject parentObj)
            {
                throw new InvalidOperationException(
                    $"Parent path '{parentPath}' not found or is not an object");
            }

            targetParent = parentObj;
        }
        else
        {
            // Path exists - get its parent using GetParentAndProperty helper
            // FHIRPath returns array elements, not the array container, so we need to traverse up
            var targetJsonNode = matches[0];
            var parentInfo = GetParentAndProperty(targetJsonNode, resource.MutableNode);
            if (parentInfo is null)
            {
                throw new InvalidOperationException(
                    $"Cannot find parent for path '{fhirPathExpression}'");
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
        else if (existing is null)
        {
            targetArray = [];
            targetParent[propertyName] = targetArray;
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot insert into non-array property '{propertyName}'");
        }

        // Validate index and insert
        if (index < 0 || index > targetArray.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Index {index} out of range (array length: {targetArray.Count})");
        }

        // Deep clone to avoid "node already has a parent" error
        var clonedValue = JsonNode.Parse(value.ToJsonString());
        targetArray.Insert(index, clonedValue);
    }

    /// <inheritdoc />
    public void ReplaceArrayElement(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);
        ArgumentNullException.ThrowIfNull(value);

        var targetNode = EvaluateSingle(resource, fhirPathExpression);

        if (targetNode.Parent is not JsonArray parentArray)
        {
            throw new InvalidOperationException(
                $"Path '{fhirPathExpression}' does not reference an array element");
        }

        var index = parentArray.IndexOf(targetNode);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Cannot find index of target element in array at path '{fhirPathExpression}'");
        }

        // Deep clone to avoid "node already has a parent" error
        var clonedValue = JsonNode.Parse(value.ToJsonString());
        parentArray[index] = clonedValue;
    }

    /// <inheritdoc />
    public IEnumerable<JsonNode> Evaluate(ResourceJsonNode resource, string fhirPathExpression)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);

        return EvaluateToJsonNodes(resource, fhirPathExpression);
    }

    /// <inheritdoc />
    public JsonNode EvaluateSingle(ResourceJsonNode resource, string fhirPathExpression)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fhirPathExpression);

        var matches = Evaluate(resource, fhirPathExpression).ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"FHIRPath expression '{fhirPathExpression}' did not match any elements");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"FHIRPath expression '{fhirPathExpression}' matched {matches.Count} elements (expected 1)");
        }

        return matches[0];
    }

    /// <summary>
    /// Serialize value to JsonNode.
    /// Handles object, JsonNode, JsonElement, and primitive types.
    /// Deep clones if the value is already a JsonNode to avoid parent conflicts.
    /// </summary>
    /// <param name="value">The value to serialize</param>
    /// <returns>JsonNode representation of the value</returns>
    public static JsonNode? SerializeValue(object value)
    {
        if (value is JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        if (value is JsonNode node)
        {
            // Deep clone to avoid "node already has a parent" error
            // JsonNode doesn't allow a node to have multiple parents
            return JsonNode.Parse(node.ToJsonString());
        }

        // For primitive types, use SerializePrimitive
        return SerializePrimitive(value);
    }

    #endregion
}
