using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Service for mutating ResourceJsonNode properties using FHIRPath navigation.
/// Shared by Transform operations and potentially PATCH operations.
/// Handles array vs single value detection, primitive vs complex types, and intermediate object creation.
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
}
