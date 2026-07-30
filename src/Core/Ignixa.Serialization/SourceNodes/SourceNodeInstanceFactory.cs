// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Instance-selector object creation backed by Ignixa's native source-node model.
/// Wire <see cref="Create"/> as the <c>InstanceCreator</c> delegate on an evaluation context.
/// Builds a JSON object for the requested type and returns it as a first-class
/// <see cref="SchemaAwareElement"/> — the same node kind the FHIRPath engine
/// navigates elsewhere — so created instances support full navigation.
/// Declines (returns null) for types unknown to the schema, and for the <c>System</c>
/// namespace, which has no FHIR construction path yet.
/// </summary>
/// <remarks>
/// The FHIRPath object-creation section is STU and is silent on choice-element naming,
/// duplicate assignments, resource identification, and whether the result must be a valid
/// standalone instance. This factory resolves those gaps as follows:
/// <list type="bullet">
/// <item>Resources emit <c>resourceType</c> so the backing JSON re-parses as a FHIR resource.
/// It is written after the assignments so an assignment cannot forge it.</item>
/// <item>An assignment naming a choice element by its base name (<c>value</c>) is emitted under the
/// type-suffixed name (<c>valueQuantity</c>) when the assigned value matches one of the declared
/// choice types. Names that already carry a suffix pass through untouched.</item>
/// <item>Several values for one element — whether from repeated assignments or from a single
/// multi-item value expression — are aggregated rather than overwriting. Assigning
/// more values than the element can hold throws, which the spec permits ("the engine MAY throw
/// an error"). Cardinality is resolved through the choice base name and its suffixed forms
/// alike.</item>
/// <item>Element names absent from the schema are emitted verbatim; this factory constructs, it
/// does not validate.</item>
/// <item>When the target type is a FHIR primitive and the only assignment is the special
/// <c>value</c> element, the result is a primitive node (scalar <c>Value</c>,
/// <c>HasPrimitiveValue</c>) rather than an object carrying a <c>value</c> child.</item>
/// </list>
/// A single value assigned to a repeating element is still emitted as a JSON scalar rather than a
/// one-item array, and primitive shadow content (<c>_value</c> extensions/id) is not emitted, so
/// the backing JSON is not always canonical FHIR.
/// <para>
/// Instances hold only the schema and are otherwise stateless, so one instance per
/// <see cref="ISchema"/> can be shared across threads and evaluation contexts. There is
/// deliberately no default instance: the schema is version-specific and this assembly does
/// not depend on any concrete one.
/// </para>
/// </remarks>
public sealed class SourceNodeInstanceFactory(ISchema schema)
{
    private const string ResourceTypeProperty = "resourceType";

    private readonly ISchema _schema = schema ?? throw new ArgumentNullException(nameof(schema));

    public IElement? Create(InstanceCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var typeName = request.TypeName;
        var elements = request.Elements;

        // This factory constructs FHIR types; System-namespace primitives are out of scope.
        if (string.Equals(request.NamespacePrefix, "System", StringComparison.Ordinal))
        {
            return null;
        }

        var definition = _schema.GetTypeDefinition(typeName);
        if (definition is null)
        {
            // Host cannot construct an unknown type — engine yields an empty result.
            return null;
        }

        // Per spec, primitive target types carry their value via the special "value"
        // element. Build a primitive value node rather than a complex object so the
        // result behaves like any other primitive (HasPrimitiveValue, scalar Value).
        if (definition.Info.IsPrimitive
            && elements is [{ Name: "value", Values: [var primitiveValue] }]
            && ElementJsonConverter.ToJsonNode(primitiveValue) is JsonValue primitiveNode)
        {
            var primitiveSource = JsonNodeSourceNode.Create(primitiveNode, typeName);
            return new SchemaAwareElement(primitiveSource, _schema, definition, typeName);
        }

        var obj = BuildObject(definition, typeName, elements);
        var source = JsonNodeSourceNode.Create(obj, typeName);
        return new SchemaAwareElement(source, _schema, definition, typeName);
    }

    private JsonObject BuildObject(IType definition, string typeName, IReadOnlyList<InstanceElement> elements)
    {
        var obj = new JsonObject();

        // Group so that repeated assignments to one name aggregate instead of overwriting.
        foreach (var group in elements.GroupBy(e => e.Name, StringComparer.Ordinal))
        {
            var values = group.SelectMany(e => e.Values).ToList();
            var nodes = values
                .Select(ElementJsonConverter.ToJsonNode)
                .OfType<JsonNode>()
                .ToList();

            if (nodes.Count == 0)
            {
                continue;
            }

            var childDefinition = FindChildDefinition(definition, group.Key);
            if (nodes.Count > 1 && childDefinition is { IsCollection: false })
            {
                throw new InvalidOperationException(
                    $"Element '{group.Key}' of type '{typeName}' does not repeat, but {nodes.Count} values were assigned.");
            }

            var propertyName = ResolvePropertyName(group.Key, childDefinition, values[0]);
            obj[propertyName] = nodes.Count == 1 ? nodes[0] : new JsonArray([.. nodes]);
        }

        // Written last so an assignment cannot forge the discriminator. Without it a created
        // resource serializes to an object with no type discriminator, which no FHIR parser
        // can read back.
        if (definition.Info.IsResource)
        {
            obj[ResourceTypeProperty] = JsonValue.Create(typeName);
        }

        return obj;
    }

    /// <summary>
    /// Locates the schema child for an assignment name, accepting the base name of a choice element
    /// (<c>value</c>) and its type-suffixed forms (<c>valueString</c>) as well as an exact match.
    /// Returns null for names the schema does not declare.
    /// </summary>
    private static IType? FindChildDefinition(IType definition, string name)
    {
        var exact = definition.Children.FirstOrDefault(c => string.Equals(c.Info.Name, name, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        var byBaseName = definition.Children.FirstOrDefault(c => IsChoice(c) && string.Equals(ChoiceBaseName(c), name, StringComparison.Ordinal));
        if (byBaseName is not null)
        {
            return byBaseName;
        }

        // Without this, cardinality is not enforced for an already-suffixed assignment.
        return definition.Children.FirstOrDefault(c => IsChoice(c) && MatchesChoiceSuffix(c, name));
    }

    /// <summary>
    /// Determines whether an assignment name is the given choice element's base name followed by
    /// one of its declared type codes (<c>valueString</c> for <c>value[x]</c> declaring <c>string</c>).
    /// </summary>
    private static bool MatchesChoiceSuffix(IType choice, string name)
    {
        var baseName = ChoiceBaseName(choice);
        if (name.Length <= baseName.Length || !name.StartsWith(baseName, StringComparison.Ordinal))
        {
            return false;
        }

        if (choice is not ITypeExtended { Types.Count: > 0 } extended)
        {
            return false;
        }

        var suffix = name[baseName.Length..];
        return extended.Types.Any(t => string.Equals(t.Code, suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps an assignment onto the JSON property name, expanding a choice element's base name into
    /// its type-suffixed form when the assigned value matches one of the declared choice types.
    /// </summary>
    private static string ResolvePropertyName(string name, IType? childDefinition, IElement value)
    {
        // Only the bare base name is expanded; an already-suffixed name would otherwise be
        // suffixed twice (valueQuantity -> valueQuantityQuantity).
        if (childDefinition is null
            || !IsChoice(childDefinition)
            || !string.Equals(ChoiceBaseName(childDefinition), name, StringComparison.Ordinal))
        {
            return name;
        }

        if (childDefinition is not ITypeExtended { Types.Count: > 0 } extended)
        {
            return name;
        }

        var assignedType = LocalTypeName(value.InstanceType);
        var declared = extended.Types.FirstOrDefault(t =>
            string.Equals(t.Code, assignedType, StringComparison.OrdinalIgnoreCase));

        // An unmatched type is left alone rather than rejected — validation is not this
        // factory's job, and guessing a suffix would produce a silently wrong element name.
        return declared?.Code is { Length: > 0 } code
            ? name + char.ToUpperInvariant(code[0]) + code[1..]
            : name;
    }

    private static bool IsChoice(IType type)
        => type.Info.IsChoiceElement || type.Info.Name.EndsWith("[x]", StringComparison.Ordinal);

    private static string ChoiceBaseName(IType type)
        => type.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
            ? type.Info.Name[..^3]
            : type.Info.Name;

    /// <summary>
    /// Strips a namespace qualifier so <c>System.String</c> and <c>FHIR.Quantity</c> can be compared
    /// against the unqualified type codes the schema declares for a choice element.
    /// </summary>
    private static string LocalTypeName(string instanceType)
    {
        var separator = instanceType.LastIndexOf('.');
        return separator >= 0 ? instanceType[(separator + 1)..] : instanceType;
    }
}
