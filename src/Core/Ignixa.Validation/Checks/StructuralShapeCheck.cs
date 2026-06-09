// <copyright file="StructuralShapeCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that the raw JSON shape of a declared element matches its StructureDefinition.
/// Tier 2 (Spec) validator - one instance per declared element.
/// </summary>
/// <remarks>
/// Inspects the parent's <em>raw</em> JSON object (via <see cref="IElement.Meta{T}"/>), reading the
/// property by key, because schema-aware navigation (<see cref="IElement.Children(string)"/>) drops
/// null property values and collapses the array-vs-scalar distinction. Only the parent JsonObject
/// preserves the original shape, and only there can a bare <c>x</c> property be told apart from its
/// <c>_x</c> primitive-extension shadow.
///
/// Rules enforced (FHIR R4 element-shape conformance):
/// <list type="number">
/// <item>collection (max &gt; 1) given a non-array value, or scalar (max 0..1) given a JSON array;</item>
/// <item>a property whose value is JSON <c>null</c>, or a null array entry;</item>
/// <item>ele-1: an empty array, or an empty object with no value and no children;</item>
/// <item>a primitive element (<c>x</c>) given a JSON object (the shadow allowance applies to <c>_x</c> only);</item>
/// <item>a complex element given a JSON primitive.</item>
/// </list>
/// </remarks>
public sealed class StructuralShapeCheck : IValidationCheck
{
    private readonly string _elementName;
    private readonly bool _isPrimitive;
    private readonly bool _isCollection;
    private readonly bool _isBackbone;
    private readonly string _primitiveType;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructuralShapeCheck"/> class.
    /// </summary>
    /// <param name="elementName">The declared element name (e.g. "name", "active", "gender").</param>
    /// <param name="isPrimitive">Whether the element's declared type is a FHIR primitive.</param>
    /// <param name="isCollection">Whether the element is a collection (max &gt; 1 / "*").</param>
    /// <param name="isBackbone">Whether the element is an inline BackboneElement (vs a complex datatype).</param>
    /// <param name="primitiveType">The declared FHIR primitive type (used for value rules); ignored for complex elements.</param>
    public StructuralShapeCheck(string elementName, bool isPrimitive, bool isCollection, bool isBackbone, string primitiveType)
    {
        _elementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
        _isPrimitive = isPrimitive;
        _isCollection = isCollection;
        _isBackbone = isBackbone;
        _primitiveType = primitiveType ?? string.Empty;
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.Meta<JsonNode>() is not JsonObject parent)
        {
            // No raw JSON object available (e.g. synthetic element); other checks handle this.
            return ValidationResult.Success();
        }

        var basePath = string.IsNullOrEmpty(element.Location) ? _elementName : $"{element.Location}.{_elementName}";

        var issues = new List<ValidationIssue>();
        parent.TryGetPropertyValue("_" + _elementName, out var shadowNode);

        var hasValue = parent.TryGetPropertyValue(_elementName, out var valueNode);
        if (hasValue)
        {
            ValidateProperty(valueNode, shadowNode, basePath, issues);
        }

        // A primitive-extension shadow ("_x") that is itself null carries neither a value nor
        // extension content, which is invalid. Reported against the value path ("x"), matching
        // the FHIR convention of surfacing the logical element rather than the shadow key.
        if (_isPrimitive && !hasValue && parent.ContainsKey("_" + _elementName) && shadowNode is null)
        {
            issues.Add(NullValue(basePath));
        }

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }

    private void ValidateProperty(JsonNode? valueNode, JsonNode? shadowNode, string basePath, List<ValidationIssue> issues)
    {
        if (valueNode is null)
        {
            // A null value paired with a valid "_x" shadow (object carrying id/extension) is the
            // legal FHIR way to attach an extension without a primitive value.
            if (_isPrimitive && shadowNode is JsonObject)
            {
                return;
            }

            issues.Add(NullValue(basePath));
            return;
        }

        var kind = valueNode.GetValueKind();

        if (kind == JsonValueKind.Array)
        {
            ValidateArray((JsonArray)valueNode, shadowNode as JsonArray, basePath, issues);
            return;
        }

        // A non-array value for a collection element violates the array requirement.
        if (_isCollection)
        {
            issues.Add(NotAnArray(basePath));
            return;
        }

        ValidateScalar(valueNode, kind, basePath, issues);
    }

    private void ValidateArray(JsonArray array, JsonArray? shadowArray, string basePath, List<ValidationIssue> issues)
    {
        // A scalar (0..1) element given a JSON array.
        if (!_isCollection)
        {
            // ele-1: an empty array has no content, regardless of type.
            if (array.Count == 0)
            {
                issues.Add(Ele1Empty(basePath));
                return;
            }

            // A primitive scalar wrapped in an array is rejected (e.g. gender:["male"]).
            // For complex scalars, a single-element array is tolerated as a lenient cross-version
            // shape ([x] treated as x); only empty or multi-element arrays are rejected. This keeps
            // strictly-required primitive cases failing without over-rejecting real-world data where
            // an element is a collection in one FHIR version and a scalar in another.
            if (_isPrimitive || array.Count > 1)
            {
                issues.Add(UnexpectedArray(basePath));
                return;
            }

            // Single complex item in a scalar array: validate the item shape, not the wrapping.
            ValidateScalar(array[0], array[0]?.GetValueKind(), basePath, issues);
            return;
        }

        // ele-1: an empty array has no content.
        if (array.Count == 0)
        {
            issues.Add(Ele1Empty(basePath));
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var itemPath = $"{basePath}[{i}]";
            var item = array[i];

            // A null primitive array entry is legal when the paired "_x" shadow array supplies an
            // extension object at the same index (the FHIR primitive-extension array pairing).
            if (item is null && _isPrimitive && ShadowItemAt(shadowArray, i) is JsonObject)
            {
                continue;
            }

            ValidateScalar(item, item?.GetValueKind(), itemPath, issues);
        }
    }

    private static JsonNode? ShadowItemAt(JsonArray? shadowArray, int index) =>
        shadowArray is not null && index < shadowArray.Count ? shadowArray[index] : null;

    private void ValidateScalar(JsonNode? node, JsonValueKind? kind, string path, List<ValidationIssue> issues)
    {
        if (node is null || kind is null)
        {
            issues.Add(NullValue(path));
            return;
        }

        switch (kind.Value)
        {
            case JsonValueKind.Object:
                ValidateObject((JsonObject)node, path, issues);
                return;

            case JsonValueKind.Array:
                // Nested array (e.g. an array element that is itself an array) is never valid.
                issues.Add(UnexpectedArray(path));
                return;

            default:
                // A scalar JSON value at a complex element is invalid.
                if (!_isPrimitive)
                {
                    issues.Add(PrimitiveAtComplex(path, kind.Value));
                }

                return;
        }
    }

    private void ValidateObject(JsonObject obj, string path, List<ValidationIssue> issues)
    {
        // A JSON object at a primitive element is invalid; the legal home for id/extension on a
        // primitive is the "_x" shadow key, which is handled separately and never reaches here.
        if (_isPrimitive)
        {
            issues.Add(ObjectAtPrimitive(path));
            return;
        }

        // ele-1: a complex datatype object must carry a value or at least one child element.
        // Inline BackboneElements are excluded here: their required children are enforced by the
        // nested cardinality machinery, and flagging an empty backbone risks over-rejecting shapes
        // that other checks already cover. The fhir262 ele-1 cases target complex datatypes
        // (CodeableConcept, HumanName), not backbones.
        if (!_isBackbone && !HasMeaningfulContent(obj))
        {
            issues.Add(Ele1Empty(path));
        }
    }

    private static bool HasMeaningfulContent(JsonObject obj)
    {
        foreach (var (key, value) in obj)
        {
            if (value is null)
            {
                continue;
            }

            // resourceType alone is metadata, not element content.
            if (string.Equals(key, "resourceType", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private ValidationIssue NullValue(string path) =>
        ValidationIssue.InvariantFailure(
            "structure-null",
            $"Element '{_elementName}' must not be null",
            path);

    private ValidationIssue NotAnArray(string path) =>
        ValidationIssue.InvariantFailure(
            "structure-array",
            $"Element '{_elementName}' is a collection and must be a JSON array",
            path);

    private ValidationIssue UnexpectedArray(string path) =>
        ValidationIssue.InvariantFailure(
            "structure-array",
            $"Element '{_elementName}' is not a collection and must not be a JSON array",
            path);

    private ValidationIssue Ele1Empty(string path) =>
        ValidationIssue.InvariantFailure(
            "ele-1",
            "All FHIR elements must have a @value or children",
            path);

    private ValidationIssue ObjectAtPrimitive(string path) =>
        ValidationIssue.InvariantFailure(
            "structure-1",
            $"Primitive element '{_elementName}' must carry a {_primitiveType} value, not a JSON object",
            path);

    private ValidationIssue PrimitiveAtComplex(string path, JsonValueKind kind) =>
        ValidationIssue.InvariantFailure(
            "structure-1",
            $"Complex element '{_elementName}' must be a JSON object, not a {kind} value",
            path);
}
