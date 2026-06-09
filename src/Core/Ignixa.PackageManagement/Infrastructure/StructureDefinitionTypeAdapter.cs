// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Converts a FHIR <c>StructureDefinition.snapshot.element</c> list into an
/// <see cref="IType"/> / <see cref="ITypeExtended"/> tree consumable by
/// <c>StructureDefinitionSchemaBuilder</c>.
/// <para>
/// Snapshot-only. Differential resolution is not performed - StructureDefinitions
/// without a snapshot return <c>null</c>. (Snapshot generation is a separate concern
/// tracked in the IG support plan.)
/// </para>
/// <para>
/// The adapter is a pure function: parse JSON, build tree, return root. It has no
/// I/O, logging, or dependencies on FHIR-version-specific schema providers, which
/// makes it cheap to call per profile and easy to test deterministically.
/// </para>
/// </summary>
public sealed class StructureDefinitionTypeAdapter
{
    /// <summary>
    /// Adapts a StructureDefinition's snapshot into an <see cref="IType"/> tree.
    /// </summary>
    /// <param name="structureDefinitionJson">The full StructureDefinition resource JSON.</param>
    /// <param name="fhirVersion">FHIR version (e.g. "4.0.1"). Reserved for future version-specific handling.</param>
    /// <returns>Root <see cref="IType"/> or <c>null</c> if the JSON is not a StructureDefinition with a snapshot.</returns>
    public IType? Adapt(string structureDefinitionJson, string fhirVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(structureDefinitionJson);
        ArgumentException.ThrowIfNullOrEmpty(fhirVersion);

        using var doc = JsonDocument.Parse(structureDefinitionJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetString(root, "resourceType", out var resourceType) || resourceType != "StructureDefinition")
        {
            return null;
        }

        if (!root.TryGetProperty("snapshot", out var snapshot) ||
            snapshot.ValueKind != JsonValueKind.Object ||
            !snapshot.TryGetProperty("element", out var elementsArray) ||
            elementsArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (!TryGetString(root, "type", out var rootTypeName))
        {
            return null;
        }

        var kind = TryGetString(root, "kind", out var k) ? k : null;
        var isAbstract = root.TryGetProperty("abstract", out var a) && a.ValueKind == JsonValueKind.True;
        var isResource = string.Equals(kind, "resource", StringComparison.Ordinal);

        // First pass: enumerate elements once, capturing path + position so we can build
        // the tree by walking sorted paths.
        var elements = new List<ElementSnapshot>();
        int order = 0;
        foreach (var el in elementsArray.EnumerateArray())
        {
            if (!TryGetString(el, "path", out var path) || path is null)
            {
                continue;
            }
            elements.Add(new ElementSnapshot(path, order++, el.Clone()));
        }

        if (elements.Count == 0 || elements[0].Path != rootTypeName)
        {
            return null;
        }

        return BuildNode(elements, 0, rootTypeName!, isResource, isAbstract, parentOrder: 0);
    }

    /// <summary>
    /// Recursively builds a node for <paramref name="path"/> at <paramref name="startIndex"/>,
    /// consuming all descendants in document order.
    /// </summary>
    private static AdaptedType BuildNode(
        List<ElementSnapshot> elements,
        int startIndex,
        string path,
        bool isResource,
        bool isAbstract,
        int parentOrder)
    {
        var self = elements[startIndex];

        var min = ReadInt(self.Element, "min") ?? 0;
        var max = ReadString(self.Element, "max") ?? "*";
        // Root element: FHIR snapshots often declare max="*" at the resource root for
        // historical reasons, but the resource itself is never a collection. Codegen
        // matches this convention by always setting IsCollection=false at the root.
        var isCollection = startIndex != 0
            && !string.Equals(max, "1", StringComparison.Ordinal)
            && !string.Equals(max, "0", StringComparison.Ordinal);
        var isRequired = min > 0;

        var localName = ExtractLocalName(path);
        var isChoiceElement = localName.EndsWith("[x]", StringComparison.Ordinal);

        // FHIR type extraction: ElementDefinition.type may be null on the root.
        var typeRefs = ExtractTypes(self.Element);
        var primaryTypeCode = typeRefs.Count > 0 ? typeRefs[0].Code : null;
        var (fhirTypeName, primitive) = ResolvePrimitive(primaryTypeCode);

        // Root uses the resource type name; descendants use the FHIR type from ElementDefinition.type
        // (falling back to the local element name for BackboneElements).
        var displayName = startIndex == 0 ? path : localName;

        var info = new TypeInfo(
            name: displayName,
            primitive: primitive,
            isResource: startIndex == 0 && isResource,
            isAbstract: startIndex == 0 && isAbstract,
            isChoiceElement: isChoiceElement,
            isModifier: ReadBool(self.Element, "isModifier") ?? false);

        var constraints = ExtractConstraints(self.Element, scopeTypeName: path.Split('.')[0]);
        var binding = ExtractBinding(self.Element);
        var (fixedValue, patternValue) = ExtractFixedAndPattern(self.Element);
        var referenceTargets = ExtractReferenceTargets(typeRefs);

        // Build children: any subsequent element whose path begins with "{path}." up to the
        // next sibling of the current node.
        //
        // Slicing note: a profile may emit multiple ElementDefinitions sharing the same path
        // (the slice header + one entry per slice, distinguished by sliceName). The slice
        // header carries the aggregate cardinality for the element (e.g. extension is 0..*);
        // individual slice members carry per-slice constraints (e.g. extension:race is 0..1).
        // Without slicing-aware validation, emitting one child per slice would attach
        // contradictory cardinality checks. Dedupe by path so the first occurrence (the slice
        // header, since snapshot order places it first) is the single visible child.
        // Slicing-aware checks are a separate plan (see SlicingMetadata on CoreType).
        var children = new List<IType>();
        var seenChildPaths = new HashSet<string>(StringComparer.Ordinal);
        var i = startIndex + 1;
        while (i < elements.Count)
        {
            var candidate = elements[i].Path;
            if (!candidate.StartsWith(path + ".", StringComparison.Ordinal))
            {
                break; // No longer a descendant.
            }

            // Only direct children: exactly one segment beyond the parent path.
            var depthAfter = candidate.AsSpan(path.Length + 1).IndexOf('.');
            if (depthAfter >= 0)
            {
                // Indirect descendant - will be picked up when we recurse on its parent.
                i++;
                continue;
            }

            if (!seenChildPaths.Add(candidate))
            {
                // Duplicate path - this is a slice member of an already-emitted slice header.
                // Skip the entire slice subtree.
                i = AdvancePastSubtree(elements, i);
                continue;
            }

            // Direct child. Recurse, then advance past the whole subtree.
            var child = BuildNode(elements, i, candidate, isResource: false, isAbstract: false, parentOrder: i);
            children.Add(child);
            i = AdvancePastSubtree(elements, i);
        }

        return new AdaptedType(
            info: info,
            isCollection: isCollection,
            isRequired: isRequired,
            order: self.Order,
            min: min,
            max: max,
            children: children,
            constraints: constraints,
            binding: binding,
            fixedValue: fixedValue,
            patternValue: patternValue,
            types: typeRefs,
            defaultTypeName: fhirTypeName,
            referenceTargets: referenceTargets,
            contentReference: ReadString(self.Element, "contentReference"));
    }

    private static int AdvancePastSubtree(List<ElementSnapshot> elements, int startIndex)
    {
        var basePath = elements[startIndex].Path;
        var i = startIndex + 1;
        while (i < elements.Count && elements[i].Path.StartsWith(basePath + ".", StringComparison.Ordinal))
        {
            i++;
        }
        return i;
    }

    private static IReadOnlyList<ITypeReference> ExtractTypes(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var typeArr) || typeArr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ITypeReference>();
        }

        var list = new List<ITypeReference>(typeArr.GetArrayLength());
        foreach (var t in typeArr.EnumerateArray())
        {
            var rawCode = ReadString(t, "code") ?? string.Empty;
            // The FHIRPath system-type extension overrides code with a friendly FHIR type name.
            var resolvedCode = ResolveFhirPathSystemTypeOverride(t) ?? rawCode;
            var profiles = ReadAllStringsInArray(t, "profile");
            var targetProfiles = ReadAllStringsInArray(t, "targetProfile");

            // ITypeReference holds a single profile/targetProfile string. Emit one entry per
            // (profile, targetProfile) cross product. For Reference elements with multiple
            // targets and no profile constraints, this yields one entry per target.
            if (targetProfiles.Count == 0 && profiles.Count == 0)
            {
                list.Add(new TypeReferenceDefinition(resolvedCode));
                continue;
            }

            if (targetProfiles.Count == 0)
            {
                foreach (var profile in profiles)
                {
                    list.Add(new TypeReferenceDefinition(resolvedCode, profile));
                }
                continue;
            }

            foreach (var targetProfile in targetProfiles)
            {
                var profile = profiles.Count > 0 ? profiles[0] : null;
                list.Add(new TypeReferenceDefinition(resolvedCode, profile, targetProfile));
            }
        }
        return list;
    }

    private static string? ResolveFhirPathSystemTypeOverride(JsonElement typeElement)
    {
        if (!typeElement.TryGetProperty("extension", out var extArr) || extArr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var ext in extArr.EnumerateArray())
        {
            if (ReadString(ext, "url") == "http://hl7.org/fhir/StructureDefinition/structuredefinition-fhir-type")
            {
                return ReadString(ext, "valueUrl");
            }
        }
        return null;
    }

    private static IReadOnlyList<IConstraint> ExtractConstraints(JsonElement element, string scopeTypeName)
    {
        if (!element.TryGetProperty("constraint", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<IConstraint>();
        }

        var list = new List<IConstraint>(arr.GetArrayLength());
        foreach (var c in arr.EnumerateArray())
        {
            var key = ReadString(c, "key");
            var expression = ReadString(c, "expression");
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(expression))
            {
                continue;
            }

            // Emit Abstractions.ConstraintDefinition (implements IConstraint). The existing
            // StructureDefinitionSchemaBuilder.ExtractInvariantChecks cast to
            // Specification.ConstraintDefinition is a pre-existing latent bug - that record
            // does not implement IConstraint, so no invariants run today. Task 4 will
            // address that. The scope filter via AppliesTo is also Task 4's concern.
            list.Add(new Ignixa.Abstractions.ConstraintDefinition
            {
                Key = key!,
                Expression = expression!,
                Human = ReadString(c, "human"),
                Severity = ReadString(c, "severity") ?? "error",
                Xpath = ReadString(c, "xpath"),
            });
        }
        return list;
    }

    private static IBinding? ExtractBinding(JsonElement element)
    {
        if (!element.TryGetProperty("binding", out var b) || b.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var strength = ReadString(b, "strength");
        if (string.IsNullOrEmpty(strength))
        {
            return null;
        }
        return new AdaptedBinding(
            strength: strength!,
            valueSet: ReadString(b, "valueSet"),
            description: ReadString(b, "description"));
    }

    private static (object? fixedValue, object? patternValue) ExtractFixedAndPattern(JsonElement element)
    {
        object? fixedValue = null;
        object? patternValue = null;
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.StartsWith("fixed", StringComparison.Ordinal) && prop.Name.Length > 5)
            {
                fixedValue = ExtractScalarOrJson(prop.Value);
            }
            else if (prop.Name.StartsWith("pattern", StringComparison.Ordinal) && prop.Name.Length > 7)
            {
                patternValue = ExtractScalarOrJson(prop.Value);
            }
        }
        return (fixedValue, patternValue);
    }

    private static object? ExtractScalarOrJson(JsonElement value) => value.ValueKind switch
    {
        // For primitives, return JSON-encoded string form so consumers that JsonNode.Parse()
        // the result (e.g. Validation.Checks.PatternCheck) get valid JSON. A bare C# string
        // like "final" parses as invalid JSON; "\"final\"" parses correctly.
        JsonValueKind.String => System.Text.Json.JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText(),
    };

    private static IReadOnlyList<string> ExtractReferenceTargets(IReadOnlyList<ITypeReference> types)
    {
        var targets = new List<string>();
        foreach (var t in types)
        {
            if (!string.IsNullOrEmpty(t.TargetProfile))
            {
                var slash = t.TargetProfile.LastIndexOf('/');
                targets.Add(slash >= 0 ? t.TargetProfile[(slash + 1)..] : t.TargetProfile);
            }
        }
        return targets;
    }

    /// <summary>
    /// Maps a FHIR primitive type code to <see cref="FhirPrimitive"/>. Returns
    /// <see cref="FhirPrimitive.None"/> for complex types or unknown codes.
    /// </summary>
    private static (string fhirTypeName, FhirPrimitive primitive) ResolvePrimitive(string? typeCode)
    {
        if (string.IsNullOrEmpty(typeCode))
        {
            return (string.Empty, FhirPrimitive.None);
        }
        return typeCode switch
        {
            "boolean" => (typeCode, FhirPrimitive.Boolean),
            "integer" => (typeCode, FhirPrimitive.Integer),
            "string" => (typeCode, FhirPrimitive.String),
            "decimal" => (typeCode, FhirPrimitive.Decimal),
            "uri" => (typeCode, FhirPrimitive.Uri),
            "url" => (typeCode, FhirPrimitive.Url),
            "canonical" => (typeCode, FhirPrimitive.Canonical),
            "base64Binary" => (typeCode, FhirPrimitive.Base64Binary),
            "instant" => (typeCode, FhirPrimitive.Instant),
            "date" => (typeCode, FhirPrimitive.Date),
            "dateTime" => (typeCode, FhirPrimitive.DateTime),
            "time" => (typeCode, FhirPrimitive.Time),
            "code" => (typeCode, FhirPrimitive.Code),
            "oid" => (typeCode, FhirPrimitive.Oid),
            "id" => (typeCode, FhirPrimitive.Id),
            "markdown" => (typeCode, FhirPrimitive.Markdown),
            "unsignedInt" => (typeCode, FhirPrimitive.UnsignedInt),
            "positiveInt" => (typeCode, FhirPrimitive.PositiveInt),
            "uuid" => (typeCode, FhirPrimitive.Uuid),
            "xhtml" => (typeCode, FhirPrimitive.None),
            _ => (typeCode, FhirPrimitive.None),
        };
    }

    private static string ExtractLocalName(string path)
    {
        var idx = path.LastIndexOf('.');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private static bool TryGetString(JsonElement parent, string property, out string? value)
    {
        if (parent.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return !string.IsNullOrEmpty(value);
        }
        value = null;
        return false;
    }

    private static string? ReadString(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? ReadInt(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static bool? ReadBool(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var v)
            ? v.ValueKind == JsonValueKind.True ? true : v.ValueKind == JsonValueKind.False ? false : (bool?)null
            : null;

    private static IReadOnlyList<string> ReadAllStringsInArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        var list = new List<string>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    list.Add(s!);
                }
            }
        }
        return list;
    }

    private readonly record struct ElementSnapshot(string Path, int Order, JsonElement Element);
}
