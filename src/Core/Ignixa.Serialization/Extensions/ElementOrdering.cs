// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Ranks JSON properties into StructureDefinition element order - the order the Firely SDK serializer emits.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>resourceType</c> first, then elements by <see cref="IType.Order"/>.</item>
/// <item>Choice variants (<c>valueQuantity</c>) take the position of their choice element (<c>value[x]</c>).</item>
/// <item>A primitive's extension sibling (<c>_birthDate</c>) immediately follows the primitive.</item>
/// <item>Properties the schema does not describe share the last rank; a stable sort keeps their source order.</item>
/// </list>
/// </remarks>
internal static class ElementOrdering
{
    public const string ResourceTypeProperty = "resourceType";

    private const int ResourceTypeRank = -1;
    private const int UnknownRank = int.MaxValue;

    private static readonly ConditionalWeakTable<ISchema, ConcurrentDictionary<(IType Parent, string Name), ElementPosition>> Positions = new();

    /// <summary>
    /// True where a JSON object is a resource: the root (no type yet) or an element typed as a resource.
    /// </summary>
    public static bool IsResourcePosition(IType? type) => type is null || type.Info.IsResource;

    public static ElementPosition Resolve(IType? parent, string name, ISchema schema)
    {
        if (name == ResourceTypeProperty && IsResourcePosition(parent))
        {
            return new ElementPosition(ResourceTypeRank, Type: null);
        }

        if (parent is null)
        {
            return new ElementPosition(UnknownRank, Type: null);
        }

        ConcurrentDictionary<(IType Parent, string Name), ElementPosition> positions = Positions.GetOrCreateValue(schema);
        if (positions.TryGetValue((parent, name), out ElementPosition cached))
        {
            return cached;
        }

        // Property names come from request bodies, so only names the schema defines are cached:
        // caching unknown names would let clients grow this process-lifetime cache without bound.
        ElementPosition position = Compute(parent, name, schema);
        return position.Rank == UnknownRank ? position : positions.GetOrAdd((parent, name), position);
    }

    private static ElementPosition Compute(IType parent, string name, ISchema schema)
    {
        bool isExtensionSibling = name.Length > 1 && name[0] == '_';
        string elementName = isExtensionSibling ? name[1..] : name;

        // Exact names win over choice prefixes: "valueSet" must not resolve as a "value[x]" variant.
        IType? element = parent.Children.FirstOrDefault(child => child.Info.Name == elementName);
        string? choiceTypeCode = null;
        if (element is null)
        {
            (element, choiceTypeCode) = FindChoiceVariant(parent, elementName);
        }

        if (element is null)
        {
            return new ElementPosition(UnknownRank, Type: null);
        }

        // Ranks are doubled so an extension sibling ("_birthDate") slots between its element and the next one.
        int rank = (element.Order * 2) + (isExtensionSibling ? 1 : 0);
        IType? type = isExtensionSibling
            ? schema.GetTypeDefinition("Element")
            : choiceTypeCode is not null
                ? schema.GetTypeDefinition(choiceTypeCode)
                : ResolveElementType(parent, element, schema);

        return new ElementPosition(rank, type);
    }

    /// <summary>
    /// Matches "valueQuantity" to the "value[x]" element only when "Quantity" is one of its declared
    /// types, so invented names ("valueZz1") stay unknown and are never cached.
    /// </summary>
    private static (IType? Element, string? TypeCode) FindChoiceVariant(IType parent, string elementName)
    {
        foreach (IType child in parent.Children)
        {
            string baseName = child.Info.Name;
            if (!child.Info.IsChoiceElement
                || child is not ITypeExtended extended
                || elementName.Length <= baseName.Length
                || !elementName.StartsWith(baseName, StringComparison.Ordinal))
            {
                continue;
            }

            string suffix = elementName[baseName.Length..];
            ITypeReference? declared = extended.Types.FirstOrDefault(type => string.Equals(type.Code, suffix, StringComparison.OrdinalIgnoreCase));
            if (declared is not null)
            {
                return (child, declared.Code);
            }
        }

        return (null, null);
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
