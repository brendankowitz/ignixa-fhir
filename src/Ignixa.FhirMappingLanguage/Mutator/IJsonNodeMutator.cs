using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Service for mutating ResourceJsonNode properties using FHIRPath navigation.
/// Shared by Transform operations and PATCH operations.
/// Handles array vs single value detection, primitive vs complex types, and intermediate object creation.
/// This is the single source of truth for all resource mutation operations.
/// </summary>
public interface IJsonNodeMutator
{
    /// <summary>
    /// Set property value at the specified FHIRPath expression.
    /// Handles array vs single value automatically based on mode.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath expression to property (e.g., "Patient.name")</param>
    /// <param name="value">Value to set (IElement)</param>
    /// <param name="mode">Mutation mode: Replace (single-valued), Append (multi-valued), or Auto-detect (default)</param>
    void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        IElement value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect);

    /// <summary>
    /// Set property value from JsonNode.
    /// Useful when value is already serialized to JsonNode.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath expression to property</param>
    /// <param name="value">Value to set (JsonNode)</param>
    /// <param name="mode">Mutation mode</param>
    void SetProperty(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect);

    /// <summary>
    /// Ensure property path exists, creating intermediate objects if needed.
    /// Returns the JsonNode at the path for further manipulation.
    /// Example: "Patient.contact.name" creates "contact" object if missing.
    /// </summary>
    /// <param name="resource">Target resource</param>
    /// <param name="fhirPathExpression">FHIRPath expression to property</param>
    /// <returns>JsonNode at the specified path</returns>
    JsonNode EnsurePropertyPath(
        ResourceJsonNode resource,
        string fhirPathExpression);

    /// <summary>
    /// Delete property or array element at the specified FHIRPath.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath expression to the element to delete</param>
    /// <exception cref="System.InvalidOperationException">Thrown if path doesn't match exactly one element</exception>
    void DeleteProperty(
        ResourceJsonNode resource,
        string fhirPathExpression);

    /// <summary>
    /// Insert value into array at specific index.
    /// Creates the array if it doesn't exist.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath expression to the array property</param>
    /// <param name="value">Value to insert</param>
    /// <param name="index">Zero-based index position for insertion</param>
    /// <exception cref="System.ArgumentOutOfRangeException">Thrown if index is out of range</exception>
    void InsertIntoArray(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value,
        int index);

    /// <summary>
    /// Replace array element at specific index.
    /// </summary>
    /// <param name="resource">Target resource to mutate</param>
    /// <param name="fhirPathExpression">FHIRPath expression to the array element</param>
    /// <param name="value">New value to set</param>
    /// <exception cref="System.InvalidOperationException">Thrown if path doesn't match exactly one element or element is not in an array</exception>
    void ReplaceArrayElement(
        ResourceJsonNode resource,
        string fhirPathExpression,
        JsonNode value);

    /// <summary>
    /// Evaluate FHIRPath and return all matching JsonNodes.
    /// Useful for queries that may return 0 or multiple results.
    /// </summary>
    /// <param name="resource">Resource to evaluate against</param>
    /// <param name="fhirPathExpression">FHIRPath expression (e.g., "Patient.name.where(use='official')")</param>
    /// <returns>Matching JsonNodes extracted via Meta</returns>
    IEnumerable<JsonNode> Evaluate(
        ResourceJsonNode resource,
        string fhirPathExpression);

    /// <summary>
    /// Evaluate FHIRPath and require exactly one match.
    /// </summary>
    /// <param name="resource">Resource to evaluate against</param>
    /// <param name="fhirPathExpression">FHIRPath expression</param>
    /// <returns>The single matching JsonNode</returns>
    /// <exception cref="System.InvalidOperationException">Thrown if zero or multiple matches found</exception>
    JsonNode EvaluateSingle(
        ResourceJsonNode resource,
        string fhirPathExpression);
}
