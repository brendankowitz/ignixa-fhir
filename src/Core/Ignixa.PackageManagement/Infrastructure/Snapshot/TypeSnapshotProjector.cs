// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Projects a base-provider <see cref="ITypeExtended"/> tree back into a flat FHIR
/// <c>snapshot.element</c> array. This is the inverse of <see cref="StructureDefinitionTypeAdapter"/>
/// and exists for one reason: core FHIR types are only available in-process as pre-built
/// <see cref="IType"/> trees (generated <c>*CoreSchemaProvider</c>), never as raw StructureDefinition
/// JSON. When a package profile's <c>baseDefinition</c> points at a core type, this projector
/// synthesizes the base snapshot the merger needs.
/// </summary>
/// <remarks>
/// Fidelity: the projection preserves the validation-relevant facets carried by
/// <see cref="ITypeExtended"/> — <c>path</c>, <c>min</c>, <c>max</c>, <c>type</c>, <c>binding</c>,
/// <c>constraint</c>, <c>contentReference</c>. Purely descriptive base fields (<c>short</c>,
/// <c>definition</c>, <c>mustSupport</c>, <c>isSummary</c>) and base-level <c>fixed[x]</c>/
/// <c>pattern[x]</c> are not reconstructed: they are near-absent on core base elements for M1
/// constraint targets, and any differential supplies its own. When the base is available as raw
/// package JSON (profile-on-profile), that JSON is used directly and this projector is bypassed.
/// </remarks>
internal static class TypeSnapshotProjector
{
    /// <summary>Projects <paramref name="root"/> into a snapshot <c>element</c> array in document order.</summary>
    /// <param name="root">The root type of a core StructureDefinition (e.g. Patient).</param>
    /// <returns>A fresh <see cref="JsonArray"/> of <c>ElementDefinition</c> objects.</returns>
    public static JsonArray Project(ITypeExtended root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var elements = new JsonArray();
        Walk(root, root.Info.Name, elements);
        return elements;
    }

    private static void Walk(IType type, string path, JsonArray elements)
    {
        elements.Add(BuildElement(type, path));
        foreach (var child in type.Children)
        {
            Walk(child, $"{path}.{child.Info.Name}", elements);
        }
    }

    private static JsonObject BuildElement(IType type, string path)
    {
        var element = new JsonObject { ["path"] = path };
        if (type is not ITypeExtended ext)
        {
            return element;
        }

        element["min"] = ext.Min;
        element["max"] = ext.Max;

        if (ext.Types.Count > 0)
        {
            element["type"] = BuildTypes(ext.Types);
        }

        if (ext.Binding is { } binding)
        {
            element["binding"] = BuildBinding(binding);
        }

        if (ext.Constraints.Count > 0)
        {
            element["constraint"] = BuildConstraints(ext.Constraints);
        }

        if (ext.ContentReference is { } contentReference)
        {
            element["contentReference"] = contentReference;
        }

        return element;
    }

    private static JsonArray BuildTypes(IReadOnlyList<ITypeReference> types)
    {
        var array = new JsonArray();
        foreach (var typeReference in types)
        {
            var entry = new JsonObject { ["code"] = typeReference.Code };
            if (typeReference.Profile is { } profile)
            {
                entry["profile"] = new JsonArray(JsonValue.Create(profile));
            }

            if (typeReference.TargetProfile is { } targetProfile)
            {
                entry["targetProfile"] = new JsonArray(JsonValue.Create(targetProfile));
            }

            array.Add(entry);
        }

        return array;
    }

    private static JsonObject BuildBinding(IBinding binding)
    {
        var result = new JsonObject { ["strength"] = binding.Strength };
        if (binding.ValueSet is { } valueSet)
        {
            result["valueSet"] = valueSet;
        }

        if (binding.Description is { } description)
        {
            result["description"] = description;
        }

        return result;
    }

    private static JsonArray BuildConstraints(IReadOnlyList<IConstraint> constraints)
    {
        var array = new JsonArray();
        foreach (var constraint in constraints)
        {
            var entry = new JsonObject
            {
                ["key"] = constraint.Key,
                ["severity"] = constraint.Severity,
                ["expression"] = constraint.Expression,
            };
            if (constraint.Human is { } human)
            {
                entry["human"] = human;
            }

            if (constraint.Xpath is { } xpath)
            {
                entry["xpath"] = xpath;
            }

            array.Add(entry);
        }

        return array;
    }
}
