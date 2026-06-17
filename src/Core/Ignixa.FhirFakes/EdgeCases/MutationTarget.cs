// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A single mutable primitive leaf of a resource, discovered by walking the schema-typed element
/// tree. Carries the schema facts strategies gate on (<see cref="InstanceType"/>,
/// <see cref="IsRequiredBound"/>) and the backing value <see cref="JsonNode"/> so it can be mutated
/// in place.
/// </summary>
/// <remarks>
/// Mutation is element-backed: the leaf's value <see cref="JsonNode"/> knows its own parent, so the
/// object-property vs array-element distinction is resolved at <see cref="Replace"/> time (the same
/// technique used by <c>JsonNodeMutator</c>). That collapses the former
/// <c>PropertyTarget</c>/<c>ArrayItemTarget</c> hierarchy into this single type.
/// </remarks>
public sealed class MutationTarget
{
    private readonly JsonNode _valueNode;

    /// <summary>Creates a target from a discovered leaf's value node and schema facts.</summary>
    /// <param name="valueNode">The primitive value <see cref="JsonNode"/> to mutate (not the shadow object).</param>
    /// <param name="elementName">The leaf element name (e.g. "family").</param>
    /// <param name="path">The dotted/indexed location of this leaf (e.g. "Patient.name[0].family").</param>
    /// <param name="value">The current string value of this leaf.</param>
    /// <param name="instanceType">The FHIR instance type (e.g. "string", "date", "code").</param>
    /// <param name="isRequiredBound">True when the element has a required terminology binding.</param>
    public MutationTarget(
        JsonNode valueNode,
        string elementName,
        string path,
        string value,
        string instanceType,
        bool isRequiredBound)
    {
        ArgumentNullException.ThrowIfNull(valueNode);
        ArgumentNullException.ThrowIfNull(elementName);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(instanceType);
        _valueNode = valueNode;
        ElementName = elementName;
        Path = path;
        Value = value;
        InstanceType = instanceType;
        IsRequiredBound = isRequiredBound;
    }

    /// <summary>The leaf element name (the property key, e.g. "family").</summary>
    public string ElementName { get; }

    /// <summary>The dotted/indexed location of this leaf (e.g. "Patient.name[0].family", "Patient.birthDate").</summary>
    public string Path { get; }

    /// <summary>The current string value of this leaf.</summary>
    public string Value { get; }

    /// <summary>The FHIR instance type of this leaf (e.g. "string", "markdown", "date", "dateTime", "code").</summary>
    public string InstanceType { get; }

    /// <summary>True when the element carries a required terminology binding (a bound code) and must not be free-text mutated.</summary>
    public bool IsRequiredBound { get; }

    /// <summary>Replaces this leaf's value in its parent container in place, via the value node's parent.</summary>
    public void Replace(string newValue)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        switch (_valueNode.Parent)
        {
            case JsonArray array:
                var index = array.IndexOf(_valueNode);
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        $"Leaf at '{Path}' was not found in its parent array.");
                }

                array[index] = newValue;
                break;
            case JsonObject obj:
                obj[FindPropertyName(obj, _valueNode)] = newValue;
                break;
            default:
                throw new InvalidOperationException(
                    $"Leaf at '{Path}' has no mutable parent container.");
        }
    }

    private static string FindPropertyName(JsonObject parent, JsonNode child)
    {
        foreach (var kvp in parent)
        {
            if (ReferenceEquals(kvp.Value, child))
            {
                return kvp.Key;
            }
        }

        throw new InvalidOperationException("Leaf value node not found in its parent object.");
    }
}
