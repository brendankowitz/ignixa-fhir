// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirMappingLanguage.Mutator;

public partial class JsonNodeMutator
{
    /// <inheritdoc />
    public IType GetTargetDefinition(IElement parent, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return FindMappingChild(RequireMappingType(GetMappingSchema(), NormalizeMappingType(parent.InstanceType)), propertyName);
    }

    /// <inheritdoc />
    public IElement CreateElement(string typeName, object? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var schema = GetMappingSchema();
        typeName = NormalizeMappingType(typeName);
        var definition = RequireMappingType(schema, typeName);
        EnsureConcreteType(definition);

        var (node, shadow) = value is IElement element
            ? SerializeMappingValue(element, definition)
            : CreateMappingValue(value, definition);

        // A holder lets the existing navigator pair primitive values with their metadata shadows.
        var holder = new JsonObject();
        WriteScalarTarget(holder, typeName, node, shadow);
        return WrapTarget(holder, typeName, 0, schema, definition, typeName);
    }

    /// <inheritdoc />
    public IElement SetTarget(
        IElement parent,
        string propertyName,
        IElement? value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect,
        bool insertFirst = false)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var schema = GetMappingSchema();
        var parentDefinition = RequireMappingType(schema, NormalizeMappingType(parent.InstanceType));
        var child = FindMappingChild(parentDefinition, propertyName);
        if (parentDefinition.Info.IsPrimitive && ChildName(child) == "value")
        {
            throw new InvalidOperationException("A primitive value must be assigned through its containing property.");
        }

        string typeName = ResolveMappingChildType(schema, parentDefinition, child, propertyName, value);
        var type = RequireMappingType(schema, typeName);
        EnsureConcreteType(type);
        string wireName = IsChoice(child)
            ? ChoiceWireName(ChildName(child), typeName)
            : ChildName(child);
        var (node, shadow) = value is null
            ? CreateMappingValue(null, type)
            : SerializeMappingValue(value, type);
        var target = GetMappingTargetObject(parent, parentDefinition);

        if (IsChoice(child))
        {
            RemoveOtherChoiceTargets(target, child, wireName);
        }

        int index = 0;
        if (child.IsCollection)
        {
            index = WriteCollectionTarget(target, wireName, node, shadow, mode, insertFirst);
        }
        else
        {
            WriteScalarTarget(target, wireName, node, shadow);
        }

        return WrapTarget(target, wireName, index, schema, child, typeName);
    }

    /// <inheritdoc />
    public IElement Refresh(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var schema = GetMappingSchema();
        var type = RequireMappingType(schema, NormalizeMappingType(element.InstanceType));
        var node = element.Meta<JsonNode>()
            ?? throw new InvalidOperationException($"Element '{element.Location}' is not JSON-backed.");
        var definition = element.Type ?? type;

        if (node.Parent is JsonArray array && array.Parent is JsonObject arrayOwner)
        {
            return WrapTarget(arrayOwner, element.Name, array.IndexOf(node), schema, definition, element.InstanceType);
        }

        if (node.Parent is JsonObject owner)
        {
            return WrapTarget(owner, element.Name, 0, schema, definition, element.InstanceType);
        }

        return new SchemaAwareElement(
            JsonNodeSourceNode.Create(node, element.Name), schema, definition, element.InstanceType);
    }

    /// <inheritdoc />
    public void MoveTarget(IElement element, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var node = (JsonNode?)element.Meta<JsonPrimitiveValueNode>()?.Value ?? element.Meta<JsonNode>();
        if (node?.Parent is not JsonArray array || array.Parent is not JsonObject owner)
            throw new InvalidOperationException("Target ordering requires a JSON collection item.");
        var oldIndex = array.IndexOf(node);
        var newIndex = index ?? array.Count - 1;
        if (newIndex < 0 || newIndex >= array.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (oldIndex == newIndex)
            return;
        var name = owner.Single(p => ReferenceEquals(p.Value, array)).Key;
        var pairedName = name.StartsWith('_') ? name[1..] : "_" + name;
        array.RemoveAt(oldIndex);
        array.Insert(newIndex, node);
        if (owner[pairedName] is JsonArray paired)
        {
            PadArray(paired, array.Count);
            var partner = paired[oldIndex];
            paired.RemoveAt(oldIndex);
            paired.Insert(newIndex, partner);
        }
    }

    private ISchema GetMappingSchema() => _schemaProviderFactory()
        ?? throw new InvalidOperationException("FHIR mapping target mutation requires a schema.");

    private static IType RequireMappingType(ISchema schema, string typeName) =>
        schema.GetTypeDefinition(typeName)
        ?? throw new InvalidOperationException($"FHIR type '{typeName}' is not defined in the mapping schema.");

    private static void EnsureConcreteType(IType definition)
    {
        if (definition.Info.IsAbstract)
        {
            throw new InvalidOperationException($"Cannot create abstract FHIR type '{definition.Info.Name}'.");
        }
    }

    private static string NormalizeMappingType(string typeName) => typeName switch
    {
        "System.String" => "string",
        "System.Boolean" => "boolean",
        "System.Integer" => "integer",
        "System.Long" or "long" => "integer64",
        "System.Decimal" => "decimal",
        "System.Date" => "date",
        "System.DateTime" => "dateTime",
        "System.Time" => "time",
        "System.Quantity" => "Quantity",
        _ => typeName
    };

    private static string ChildName(IType child)
    {
        string name = child.Info.Name;
        name = name[(name.LastIndexOf('.') + 1)..];
        return name.EndsWith("[x]", StringComparison.Ordinal) ? name[..^3] : name;
    }

    private static bool IsChoice(IType child) =>
        child.Info.IsChoiceElement || child.Info.Name.EndsWith("[x]", StringComparison.Ordinal);

    private static string ChoiceWireName(string name, string typeName) =>
        name + char.ToUpperInvariant(typeName[0]) + typeName[1..];

    private static IType FindMappingChild(IType parent, string propertyName)
    {
        var child = parent.Children.FirstOrDefault(candidate =>
            ChildName(candidate) == propertyName ||
            (IsChoice(candidate) &&
             (ChildName(candidate) + "[x]" == propertyName ||
              candidate is ITypeExtended extended &&
              extended.Types.Any(type => ChoiceWireName(ChildName(candidate), type.Code) == propertyName))));

        return child ?? throw new InvalidOperationException(
            $"FHIR type '{parent.Info.Name}' has no target child '{propertyName}'.");
    }

    private static string ResolveMappingChildType(
        ISchema schema,
        IType parent,
        IType child,
        string propertyName,
        IElement? value)
    {
        string name = ChildName(child);
        if (IsChoice(child))
        {
            if (child is not ITypeExtended choice)
            {
                throw new InvalidOperationException($"Choice target '{propertyName}' has no type metadata.");
            }

            string? suffixType = choice.Types.FirstOrDefault(type =>
                ChoiceWireName(name, type.Code) == propertyName)?.Code;
            string? valueType = value is null ? null : NormalizeMappingType(value.InstanceType);
            // Generated providers also populate DefaultTypeName with the first choice; that is not
            // an instruction to silently choose it when an FML target has no value.
            string? selected = suffixType ?? valueType;
            if (selected is null && choice.Types.Count == 1)
            {
                selected = choice.Types[0].Code;
            }

            if (selected is null || !choice.Types.Any(type => type.Code == selected))
            {
                throw new InvalidOperationException($"Cannot determine a supported type for choice target '{propertyName}'.");
            }

            if (suffixType is not null && valueType is not null && suffixType != valueType)
            {
                throw new InvalidOperationException(
                    $"Choice target '{propertyName}' cannot receive a value of type '{valueType}'.");
            }

            return selected;
        }

        var qualifiedType = schema.GetTypeDefinition($"{parent.Info.Name}.{name}");
        if (qualifiedType is not null)
        {
            return qualifiedType.Info.Name;
        }

        if (child is not ITypeExtended extended)
        {
            throw new InvalidOperationException($"Target '{propertyName}' has no type metadata.");
        }

        if (extended.ContentReference is { Length: > 0 } reference)
        {
            return reference[(reference.IndexOf('#', StringComparison.Ordinal) + 1)..];
        }

        string? typeName = extended.DefaultTypeName;
        if (typeName is null && extended.Types.Count == 1)
        {
            typeName = extended.Types[0].Code;
        }

        if (typeName is "Resource" or "DomainResource")
        {
            typeName = value?.InstanceType;
            if (typeName is null || !RequireMappingType(schema, typeName).Info.IsResource)
            {
                throw new InvalidOperationException($"Target '{propertyName}' requires a concrete resource value.");
            }
        }

        return typeName ?? throw new InvalidOperationException($"Cannot determine the type of target '{propertyName}'.");
    }

    private (JsonNode? Node, JsonObject? Shadow) CreateMappingValue(object? value, IType definition)
    {
        if (value is null)
        {
            return definition.Info.IsPrimitive
                ? (null, new JsonObject())
                : (PrepareComplexValue(new JsonObject(), definition), null);
        }

        JsonNode? node = SerializeRawMappingValue(value);
        if (definition.Info.IsPrimitive)
        {
            if (node is not JsonValue)
            {
                throw new InvalidOperationException($"FHIR primitive '{definition.Info.Name}' requires a scalar value.");
            }

            return (definition.Info.Primitive == FhirPrimitive.Integer64
                ? JsonValue.Create(long.Parse(node.ToString(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))
                : node, null);
        }

        return (PrepareComplexValue(node, definition), null);
    }

    private (JsonNode? Node, JsonObject? Shadow) SerializeMappingValue(IElement element, IType definition)
    {
        var sourceType = RequireMappingType(GetMappingSchema(), NormalizeMappingType(element.InstanceType));
        if (element.Meta<JsonNode>() is not null)
        {
            element = Refresh(element);
        }

        if (!definition.Info.IsPrimitive)
        {
            if (element is ISystemValueElement { Value: FhirQuantity quantity } && definition.Info.Name == "Quantity")
            {
                return (CastTransform.ToFhirQuantity(quantity), null);
            }
            return (PrepareComplexValue(SerializeValue(element), definition), null);
        }

        if (!element.HasPrimitiveValue && !sourceType.Info.IsPrimitive)
        {
            throw new InvalidOperationException($"FHIR primitive '{definition.Info.Name}' cannot receive a complex value.");
        }

        var rawValue = element.Meta<JsonPrimitiveValueNode>()?.Value;
        JsonNode? node = rawValue?.DeepClone()
            ?? (element.HasPrimitiveValue && element.Value is { } scalar ? SerializePrimitive(scalar) : null);
        if (definition.Info.Primitive == FhirPrimitive.Integer64 && element.Value is { } integer)
        {
            node = JsonValue.Create(Convert.ToInt64(integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
        }
        JsonObject? shadow = element.Meta<JsonNode>() is JsonObject content
            ? (JsonObject)content.DeepClone()
            : null;

        if (shadow is null && element.Children().Count > 0)
        {
            shadow = (JsonObject)SerializeComplexElement(element);
        }

        return (node, shadow ?? (node is null ? new JsonObject() : null));
    }

    private static JsonObject PrepareComplexValue(JsonNode? node, IType definition)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidOperationException($"FHIR type '{definition.Info.Name}' requires an object value.");
        }

        if (definition.Info.IsResource)
        {
            if (obj["resourceType"] is { } existing && existing.GetValue<string>() != definition.Info.Name)
            {
                throw new InvalidOperationException($"Resource value does not have type '{definition.Info.Name}'.");
            }

            obj["resourceType"] = definition.Info.Name;
        }
        else if (obj.ContainsKey("resourceType"))
        {
            throw new InvalidOperationException($"FHIR datatype '{definition.Info.Name}' cannot contain resourceType.");
        }

        return obj;
    }

    private JsonNode? SerializeRawMappingValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case IElement element:
                return SerializeValue(element);
            case JsonNode node:
                return node.DeepClone();
            case JsonElement json:
                return JsonNode.Parse(json.GetRawText());
            case IDictionary dictionary:
                var obj = new JsonObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string name)
                    {
                        throw new InvalidOperationException("FHIR object property names must be strings.");
                    }

                    obj[name] = SerializeRawMappingValue(entry.Value);
                }

                return obj;
            case string or bool or int or long or decimal or double or float or DateTime or DateTimeOffset or FhirTemporal:
                return SerializePrimitive(value);
            case IEnumerable sequence:
                var array = new JsonArray();
                foreach (object? item in sequence)
                {
                    array.Add(SerializeRawMappingValue(item));
                }

                return array;
            default:
                throw new InvalidOperationException($"Unsupported mapping value type '{value.GetType().FullName}'.");
        }
    }

    private static void RemoveOtherChoiceTargets(JsonObject target, IType child, string wireName)
    {
        if (child is not ITypeExtended choice)
        {
            return;
        }

        foreach (var type in choice.Types)
        {
            string otherName = ChoiceWireName(ChildName(child), type.Code);
            if (otherName != wireName)
            {
                target.Remove(otherName);
                target.Remove("_" + otherName);
            }
        }
    }

    private static JsonObject GetMappingTargetObject(IElement parent, IType definition)
    {
        var node = parent.Meta<JsonNode>();
        if (node is JsonObject obj)
        {
            return obj;
        }

        if (!definition.Info.IsPrimitive || node is not JsonValue)
        {
            throw new InvalidOperationException($"Target '{parent.Location}' is not a JSON-backed object.");
        }

        string shadowName = "_" + parent.Name;
        if (node.Parent is JsonObject owner)
        {
            if (owner[shadowName] is JsonObject existing)
            {
                return existing;
            }

            var shadow = new JsonObject();
            owner[shadowName] = shadow;
            return shadow;
        }

        if (node.Parent is JsonArray values && values.Parent is JsonObject arrayOwner)
        {
            var shadows = arrayOwner[shadowName] as JsonArray;
            if (shadows is null)
            {
                shadows = new JsonArray();
                arrayOwner[shadowName] = shadows;
            }

            PadArray(shadows, values.Count);
            int index = values.IndexOf(node);
            if (shadows[index] is JsonObject existing)
            {
                return existing;
            }

            var shadow = new JsonObject();
            shadows[index] = shadow;
            return shadow;
        }

        throw new InvalidOperationException($"Primitive target '{parent.Location}' has no JSON property owner.");
    }

    private static void WriteScalarTarget(JsonObject target, string name, JsonNode? node, JsonObject? shadow)
    {
        if (node is null)
        {
            target.Remove(name);
        }
        else
        {
            target[name] = node;
        }

        if (shadow is null)
        {
            target.Remove("_" + name);
        }
        else
        {
            target["_" + name] = shadow;
        }
    }

    private static int WriteCollectionTarget(
        JsonObject target,
        string name,
        JsonNode? node,
        JsonObject? shadow,
        PropertyMutationMode mode,
        bool insertFirst)
    {
        bool replace = mode == PropertyMutationMode.Replace;
        if (!replace && (target[name] is not (null or JsonArray) || target["_" + name] is not (null or JsonArray)))
        {
            throw new InvalidOperationException($"Collection target '{name}' is not represented by an array.");
        }

        var values = !replace && target[name] is JsonArray existingValues ? existingValues : new JsonArray();
        var shadows = !replace && target["_" + name] is JsonArray existingShadows ? existingShadows : null;
        if (values.Parent is null)
        {
            target[name] = values;
        }

        if (replace)
        {
            target.Remove("_" + name);
        }

        if (shadow is not null && shadows is null)
        {
            shadows = new JsonArray();
            target["_" + name] = shadows;
        }

        int count = Math.Max(values.Count, shadows?.Count ?? 0);
        PadArray(values, count);
        if (shadows is not null)
        {
            PadArray(shadows, count);
        }

        int index = insertFirst ? 0 : count;
        values.Insert(index, node);
        shadows?.Insert(index, shadow);
        return index;
    }

    private static void PadArray(JsonArray array, int count)
    {
        while (array.Count < count)
        {
            array.Add((JsonNode?)null);
        }
    }

    private static IElement WrapTarget(
        JsonObject owner,
        string name,
        int index,
        ISchema schema,
        IType definition,
        string instanceType)
    {
        var source = JsonNodeSourceNode.Create(owner).Children(name).ElementAt(index);
        return new SchemaAwareElement(source, schema, definition, instanceType);
    }
}
