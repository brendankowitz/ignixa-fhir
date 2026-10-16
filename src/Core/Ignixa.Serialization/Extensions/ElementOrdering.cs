// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Ranks JSON properties into StructureDefinition element order for one schema.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>resourceType</c> first, then elements by <see cref="IType.Order"/>.</item>
/// <item>Choice variants (<c>valueQuantity</c>) take the position of their choice element (<c>value[x]</c>).</item>
/// <item>A primitive's extension sibling (<c>_birthDate</c>) immediately follows the primitive.</item>
/// <item>Properties the schema does not describe share the last rank; a stable sort keeps their source order.</item>
/// </list>
/// Each type's positions are built from the schema, never from property names in a request, so the
/// cache is bounded by the schema and an unknown name costs one failed lookup.
/// </remarks>
internal sealed class ElementOrdering
{
    public const string ResourceTypeProperty = "resourceType";

    public static readonly ElementPosition ResourceType = new(-1, Type: null);
    public static readonly ElementPosition Unknown = new(int.MaxValue, Type: null);

    private static readonly ConditionalWeakTable<ISchema, ElementOrdering> PerSchema = new();

    private readonly ISchema _schema;
    private readonly ConcurrentDictionary<IType, FrozenDictionary<string, ElementPosition>> _byParent = new();

    private ElementOrdering(ISchema schema) => _schema = schema;

    public static ElementOrdering For(ISchema schema) => PerSchema.GetValue(schema, static s => new ElementOrdering(s));

    /// <summary>
    /// True where a JSON object is a resource: the root (no type yet) or an element typed as a resource.
    /// </summary>
    public static bool IsResourcePosition(IType? type) => type is null || type.Info.IsResource;

    /// <summary>
    /// Every property name the schema defines under <paramref name="parent"/>, including choice variants and
    /// <c>_</c> extension siblings.
    /// </summary>
    public FrozenDictionary<string, ElementPosition> ForParent(IType parent) =>
        _byParent.GetOrAdd(parent, static (type, schema) => Build(type, schema), _schema);

    private static FrozenDictionary<string, ElementPosition> Build(IType parent, ISchema schema)
    {
        var positions = new Dictionary<string, ElementPosition>(StringComparer.Ordinal);
        IType? extensionSiblingType = schema.GetTypeDefinition("Element");

        // Exact names first so they win over choice variants: "valueSet" must not resolve as a "value[x]" variant.
        foreach (IType child in parent.Children)
        {
            AddWithExtensionSibling(positions, child.Info.Name, child.Order, ResolveElementType(parent, child, schema), extensionSiblingType);
        }

        foreach (IType child in parent.Children)
        {
            if (!child.Info.IsChoiceElement || child is not ITypeExtended extended)
            {
                continue;
            }

            foreach (ITypeReference declared in extended.Types)
            {
                if (string.IsNullOrEmpty(declared.Code))
                {
                    continue;
                }

                // The JSON suffix is the type code with only its first letter capitalized ("string" -> "valueString").
                string variant = child.Info.Name + char.ToUpperInvariant(declared.Code[0]) + declared.Code[1..];
                AddWithExtensionSibling(positions, variant, child.Order, schema.GetTypeDefinition(declared.Code), extensionSiblingType);
            }
        }

        return positions.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void AddWithExtensionSibling(
        Dictionary<string, ElementPosition> positions,
        string name,
        int order,
        IType? type,
        IType? extensionSiblingType)
    {
        // Ranks are doubled so an extension sibling ("_birthDate") slots between its element and the next one.
        positions.TryAdd(name, new ElementPosition(order * 2, type));
        positions.TryAdd("_" + name, new ElementPosition((order * 2) + 1, extensionSiblingType));
    }

    private static IType? ResolveElementType(IType parent, IType element, ISchema schema)
    {
        // A BackboneElement is registered under its qualified path, e.g. "Patient.contact".
        IType? backbone = schema.GetTypeDefinition($"{parent.Info.Name}.{element.Info.Name}");
        if (backbone is not null)
        {
            return backbone;
        }

        if (element is not ITypeExtended extended)
        {
            return schema.GetTypeDefinition(element.Info.Name);
        }

        // Recursive backbones (Questionnaire.item.item) declare no type, only a contentReference:
        // "#Questionnaire.item", or "<canonical>#Questionnaire.item" from packages. Resolve the path after '#'.
        if (extended.ContentReference is { Length: > 1 } contentReference)
        {
            return schema.GetTypeDefinition(contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..]);
        }

        string? typeName = extended.DefaultTypeName ?? (extended.Types.Count > 0 ? extended.Types[0].Code : null);
        return typeName is null ? null : schema.GetTypeDefinition(typeName);
    }
}
