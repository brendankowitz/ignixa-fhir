using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using ISchema = Ignixa.Abstractions.ISchema;
using IType = Ignixa.Abstractions.IType;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Resolves spec-form FHIRPath Patch operations (http://hl7.org/fhir/fhirpatch.html) against the schema:
/// an 'add' with a 'name' part is bound to its parent object with known cardinality, and an
/// anonymous-type value given as nested parts becomes a JSON value shaped by the element definitions.
/// Path-only operations with a value[x] pass through unchanged.
/// </summary>
public static class FhirPatchOperationResolver
{
    /// <summary>
    /// Returns an operation with <see cref="FhirPatchOperation.Name"/> and <see cref="FhirPatchOperation.ValueParts"/>
    /// folded into executable form. Resolve immediately before execution so earlier operations are visible.
    /// </summary>
    /// <exception cref="FhirPatchException">
    /// The path is not valid FHIRPath, or the operation cannot be bound to the resource schema.
    /// </exception>
    public static FhirPatchOperation Resolve(ResourceJsonNode resource, FhirPatchOperation operation, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(schema);

        if (operation.Name == null && operation.ValueParts == null)
        {
            return operation;
        }

        if (operation.Name != null)
        {
            return ResolveNamedAdd(resource, operation, schema);
        }

        var targetType = ResolveTargetType(resource, schema, operation.Path!);
        return operation with
        {
            Value = BuildObject(schema, targetType, operation.ValueParts!),
            ValueParts = null,
        };
    }

    private static FhirPatchOperation ResolveNamedAdd(ResourceJsonNode resource, FhirPatchOperation operation, ISchema schema)
    {
        var matches = Evaluate(resource, schema, operation.Path!);
        if (matches.Count != 1)
        {
            throw new FhirPatchException(
                $"Add path '{operation.Path}' must resolve to a single element, but matched {matches.Count}");
        }

        var parentType = matches[0].InstanceType;
        if (matches[0].Meta<JsonNode>() is not JsonObject parent)
        {
            throw new FhirPatchException(
                $"Add path '{operation.Path}' resolves to a primitive {parentType} value; adding '{operation.Name}' by name requires a complex parent element");
        }

        var element = ResolveElement(schema, parentType, operation.Name!, operation.ValueType);
        if (!element.IsCollection && element.JsonNames.FirstOrDefault(name => parent.ContainsKey(name) || parent.ContainsKey("_" + name)) is { } existing)
        {
            throw new FhirPatchException(
                $"Cannot add '{operation.Name}' to {parentType}: the element does not repeat and already has a value ('{existing}')");
        }

        return operation with
        {
            Path = $"{operation.Path}.{element.PropertyName}",
            Name = null,
            Value = operation.ValueParts != null ? BuildValue(schema, element, operation.ValueParts) : operation.Value,
            ValueParts = null,
            TargetParent = parent,
            TargetProperty = element.PropertyName,
            TargetIsCollection = element.IsCollection,
        };
    }

    private static string ResolveTargetType(ResourceJsonNode resource, ISchema schema, string path)
    {
        var matches = Evaluate(resource, schema, path);
        return matches.Count > 0 ? matches[0].InstanceType : ResolveAbsentTargetType(resource, schema, path);
    }

    /// <summary>
    /// An absent target (e.g. the first List.entry) is typed from the schema through its parent, matching
    /// the value[x] form, where the executor creates the missing element. Whether the operation may create
    /// it (add, insert) or requires it to exist (replace) is left to the executor.
    /// </summary>
    private static string ResolveAbsentTargetType(ResourceJsonNode resource, ISchema schema, string path)
    {
        var lastDot = path.LastIndexOf('.');
        var name = lastDot > 0 ? path[(lastDot + 1)..] : string.Empty;
        if (!IsSimpleIdentifier(name))
        {
            throw new FhirPatchException($"Path '{path}' did not match any element");
        }

        var parentPath = path[..lastDot];
        var parents = Evaluate(resource, schema, parentPath);
        if (parents.Count != 1)
        {
            throw new FhirPatchException(
                $"Path '{path}' did not match any element, and its parent '{parentPath}' matched {parents.Count} elements (expected 1)");
        }

        var element = ResolveElement(schema, parents[0].InstanceType, name, valueType: null);
        return element.TypeName
            ?? throw new FhirPatchException($"Element '{element.PropertyName}' cannot be built from nested parts");
    }

    private static bool IsSimpleIdentifier(string name) =>
        name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static List<IElement> Evaluate(ResourceJsonNode resource, ISchema schema, string path)
    {
        // Earlier operations in the same patch may have changed the resource.
        resource.InvalidateCaches();
        try
        {
            return resource.ToElement(schema).Select(path).ToList();
        }
        catch (FormatException ex)
        {
            throw new FhirPatchException($"Path '{path}' is not a valid FHIRPath expression: {ex.Message}", ex);
        }
        catch (FhirPathEvaluationException ex)
        {
            throw new FhirPatchException($"Path '{path}' could not be evaluated: {ex.Message}", ex);
        }
    }

    private static JsonNode BuildValue(ISchema schema, ElementResolution element, IReadOnlyList<FhirPatchValuePart> parts) =>
        BuildObject(schema, element.TypeName
            ?? throw new FhirPatchException($"Element '{element.PropertyName}' cannot be built from nested parts"), parts);

    private static JsonObject BuildObject(ISchema schema, string typeName, IReadOnlyList<FhirPatchValuePart> parts)
    {
        var value = new JsonObject();
        foreach (var part in parts)
        {
            var element = ResolveElement(schema, typeName, part.Name, part.ValueType);
            var node = part.Value != null ? part.Value.DeepClone() : BuildValue(schema, element, part.Parts);

            if (element.IsCollection)
            {
                if (value[element.PropertyName] is not JsonArray items)
                {
                    items = [];
                    value[element.PropertyName] = items;
                }

                items.Add(node);
            }
            else if (element.JsonNames.Any(value.ContainsKey))
            {
                throw new FhirPatchException($"Element '{part.Name}' of {typeName} does not repeat but was supplied more than once");
            }
            else
            {
                value[element.PropertyName] = node;
            }
        }

        return value;
    }

    private static ElementResolution ResolveElement(ISchema schema, string parentType, string name, string? valueType)
    {
        var parentDefinition = schema.GetTypeDefinition(parentType)
            ?? throw new FhirPatchException($"Type '{parentType}' is not known to the schema");

        var element = parentDefinition.Children.FirstOrDefault(child => child.Info.Name == name)
            ?? parentDefinition.Children.FirstOrDefault(child => child.Info.Name == name + "[x]")
            ?? throw new FhirPatchException($"'{name}' is not an element of {parentType}");

        if (element.Info.IsChoiceElement || element.Info.Name.EndsWith("[x]", StringComparison.Ordinal))
        {
            if (valueType == null)
            {
                throw new FhirPatchException($"Choice element '{name}' of {parentType} requires a typed value[x]");
            }

            var allowedTypes = (element as ITypeExtended)?.Types.Select(type => type.Code).ToList() ?? [];
            var choiceType = allowedTypes.FirstOrDefault(code => string.Equals(code, valueType, StringComparison.OrdinalIgnoreCase))
                ?? throw new FhirPatchException($"Type '{valueType}' is not allowed for choice element '{name}' of {parentType}");

            // Every variant counts as the same element, so cardinality checks consider all of them.
            return new ElementResolution(ChoiceProperty(name, choiceType), element.IsCollection, choiceType,
                allowedTypes.Select(code => ChoiceProperty(name, code)).ToList());
        }

        return new ElementResolution(name, element.IsCollection, ResolveElementTypeName(schema, parentType, name, element), [name]);
    }

    private static string ChoiceProperty(string name, string typeCode) =>
        name + char.ToUpperInvariant(typeCode[0]) + typeCode[1..];

    private static string? ResolveElementTypeName(ISchema schema, string parentType, string name, IType element)
    {
        // A BackboneElement is defined under its qualified path (e.g. "List.entry").
        if (schema.GetTypeDefinition($"{parentType}.{name}") is { } backbone)
        {
            return backbone.Info.Name;
        }

        if (element is ITypeExtended extended)
        {
            // Recursive elements (e.g. Questionnaire.item.item) reuse the definition named by contentReference.
            if (extended.ContentReference is { Length: > 1 } contentReference)
            {
                return contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..];
            }

            if (extended.Types.Count == 1)
            {
                return extended.Types[0].Code;
            }
        }

        return null;
    }

    private sealed record ElementResolution(string PropertyName, bool IsCollection, string? TypeName, IReadOnlyList<string> JsonNames);
}
