using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.SourceNodeSerialization.Specification;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Helper for FHIRPath evaluation in PATCH operations.
/// Uses IAnnotated to extract JsonNode from ITypedElement for in-place mutation.
/// </summary>
public class FhirPathPatchHelper
{
    private readonly FhirPathEvaluator _evaluator;
    private readonly FhirPathCompiler _compiler;
    private readonly IStructureDefinitionSummaryProvider _structureProvider;

    public FhirPathPatchHelper(
        FhirPathEvaluator evaluator,
        FhirPathCompiler compiler,
        IStructureDefinitionSummaryProvider structureProvider)
    {
        _evaluator = evaluator;
        _compiler = compiler;
        _structureProvider = structureProvider;
    }

    /// <summary>
    /// Evaluate FHIRPath expression and return matching JsonNodes.
    /// </summary>
    /// <param name="resource">Resource to evaluate against</param>
    /// <param name="fhirPathExpression">FHIRPath expression (e.g., "Patient.name.where(use='official')")</param>
    /// <returns>Matching JsonNodes extracted via IAnnotated</returns>
    public IEnumerable<JsonNode> EvaluateToJsonNodes(ResourceJsonNode resource, string fhirPathExpression)
    {
        // 1. Parse FHIRPath expression
        var expression = _compiler.Parse(fhirPathExpression);

        // 2. Convert ResourceJsonNode to ISourceNode (with annotations)
        var sourceNode = resource.ToSourceNode();

        // 3. Convert ISourceNode to ITypedElement (preserves annotations)
        var typedElement = sourceNode.ToTypedElement(_structureProvider);

        // 4. Evaluate FHIRPath expression
        var matches = _evaluator.Evaluate(typedElement, expression);

        // 5. Extract JsonNodes using IAnnotated
        foreach (var match in matches)
        {
            if (match is IAnnotated annotated)
            {
                var jsonNode = annotated.Annotation<JsonNode>();
                if (jsonNode != null)
                {
                    yield return jsonNode;
                }
            }
        }
    }

    /// <summary>
    /// Evaluate FHIRPath and require exactly one match.
    /// </summary>
    /// <param name="resource">Resource to evaluate against</param>
    /// <param name="fhirPathExpression">FHIRPath expression</param>
    /// <returns>The single matching JsonNode</returns>
    /// <exception cref="FhirPatchException">Thrown if zero or multiple matches found</exception>
    public JsonNode EvaluateToSingleJsonNode(ResourceJsonNode resource, string fhirPathExpression)
    {
        var matches = EvaluateToJsonNodes(resource, fhirPathExpression).ToList();

        if (matches.Count == 0)
        {
            throw new FhirPatchException($"FHIRPath expression '{fhirPathExpression}' did not match any elements");
        }

        if (matches.Count > 1)
        {
            throw new FhirPatchException($"FHIRPath expression '{fhirPathExpression}' matched {matches.Count} elements (expected 1)");
        }

        return matches[0];
    }

    /// <summary>
    /// Get the parent JsonObject and property name for a given JsonNode.
    /// </summary>
    /// <param name="jsonNode">The JsonNode to find the parent of</param>
    /// <param name="root">The root JsonNode to search from</param>
    /// <returns>Tuple of (parent JsonObject, property name), or null if parent not found</returns>
    public static (JsonObject parent, string propertyName)? GetParentAndProperty(JsonNode jsonNode, JsonNode root)
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
    /// Serialize value to JsonNode.
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

        var json = JsonSerializer.Serialize(value);
        return JsonNode.Parse(json);
    }

    /// <summary>
    /// Check if FHIRPath references immutable property.
    /// </summary>
    /// <param name="fhirPath">FHIRPath expression</param>
    /// <returns>True if the path references an immutable property</returns>
    public static bool IsImmutablePath(string fhirPath)
    {
        // Check for immutable properties (case-insensitive)
        // Simple check for now - will be enhanced with actual FHIRPath evaluation
        return fhirPath.Contains(".id", StringComparison.OrdinalIgnoreCase) ||
               fhirPath.Contains(".meta.versionid", StringComparison.OrdinalIgnoreCase) ||
               fhirPath.Contains(".meta.lastupdated", StringComparison.OrdinalIgnoreCase);
    }
}
