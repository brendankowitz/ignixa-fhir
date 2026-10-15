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
/// <item>Properties the schema does not describe rank last; callers keep them in source order.</item>
/// </list>
/// </remarks>
internal static class ElementOrdering
{
    public const string ResourceTypeProperty = "resourceType";

    private const int ResourceTypeRank = -1;
    private const int UnknownRank = int.MaxValue;

    private static readonly ConditionalWeakTable<ISchema, ConcurrentDictionary<(IType Parent, string Name), ElementPosition>> Positions = new();

    public static ElementPosition Resolve(IType? parent, string name, ISchema schema)
    {
        if (name == ResourceTypeProperty)
        {
            return new ElementPosition(ResourceTypeRank, Type: null);
        }

        if (parent is null)
        {
            return new ElementPosition(UnknownRank, Type: null);
        }

        return Positions.GetOrCreateValue(schema).GetOrAdd((parent, name), key => Compute(key.Parent, key.Name, schema));
    }

    private static ElementPosition Compute(IType parent, string name, ISchema schema)
    {
        bool isExtensionSibling = name.Length > 1 && name[0] == '_';
        string elementName = isExtensionSibling ? name[1..] : name;

        // Exact names win over choice prefixes: "valueSet" must not resolve as a "value[x]" variant.
        IType? element = parent.Children.FirstOrDefault(child => child.Info.Name == elementName);
        string? choiceTypeName = null;
        if (element is null)
        {
            element = parent.Children.FirstOrDefault(child =>
                child.Info.IsChoiceElement
                && elementName.Length > child.Info.Name.Length
                && elementName.StartsWith(child.Info.Name, StringComparison.Ordinal));
            choiceTypeName = element is null ? null : elementName[element.Info.Name.Length..];
        }

        if (element is null)
        {
            return new ElementPosition(UnknownRank, Type: null);
        }

        int rank = (element.Order * 2) + (isExtensionSibling ? 1 : 0);
        IType? type = isExtensionSibling
            ? schema.GetTypeDefinition("Element")
            : choiceTypeName is not null
                ? schema.GetTypeDefinition(choiceTypeName)
                : ResolveElementType(parent, element, schema);

        return new ElementPosition(rank, type);
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

        // Recursive backbones (Questionnaire.item.item) declare no type, only "#Questionnaire.item".
        if (extended.ContentReference is { Length: > 1 } contentReference)
        {
            return schema.GetTypeDefinition(contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..]);
        }

        string? typeName = extended.DefaultTypeName ?? (extended.Types.Count > 0 ? extended.Types[0].Code : null);
        return typeName is null ? null : schema.GetTypeDefinition(typeName);
    }
}
