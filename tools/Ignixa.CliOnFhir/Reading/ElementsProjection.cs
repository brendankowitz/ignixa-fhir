// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// The <c>_elements</c> trim: keeps <c>resourceType</c>, <c>id</c>, <c>meta</c> and the named top-level elements,
/// (without <c>meta.profile</c>, which repeats on every resource) and marks the result <c>SUBSETTED</c> as FHIR requires. Response shaping on the raw JSON, not resource navigation.
/// </summary>
public sealed class ElementsProjection
{
    public const string SubsettedSystem = "http://terminology.hl7.org/CodeSystem/v3-ObservationValue";

    public const string SubsettedCode = "SUBSETTED";

    private static readonly string[] AlwaysKept = ["resourceType", "id", "meta"];

    private readonly ISchema _schema;
    private readonly Dictionary<string, TypeShape?> _shapes = new(StringComparer.Ordinal);

    private ElementsProjection(IReadOnlySet<string> names, ISchema schema)
    {
        Names = names;
        _schema = schema;
    }

    /// <summary>
    /// The requested element names. A choice element may be named without its type (<c>value</c> or <c>value[x]</c>, which
    /// selects every <c>value*</c> property) or with it (<c>valueQuantity</c>).
    /// </summary>
    public IReadOnlySet<string> Names { get; }

    /// <summary>
    /// Parses comma-separated element lists; null when none was given, so the caller returns whole resources.
    /// </summary>
    /// <exception cref="ToolRequestException">A list was given but names no element.</exception>
    public static ElementsProjection? Parse(IReadOnlyCollection<string> lists, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(schema);

        if (lists.Count == 0)
        {
            return null;
        }

        HashSet<string> names = new(
            lists
                .SelectMany(list => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(name => name.EndsWith("[x]", StringComparison.Ordinal) ? name[..^3] : name),
            StringComparer.Ordinal);
        if (names.Count == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", "'elements' must name at least one element, for example 'code,valueQuantity'.");
        }

        return new ElementsProjection(names, schema);
    }

    /// <summary>Warnings for requested names that are not top-level elements of <paramref name="resourceType"/>, which therefore select nothing.</summary>
    public IReadOnlyList<IssueItem> Validate(string resourceType)
    {
        TypeShape? shape = ShapeOf(resourceType);
        if (shape is null)
        {
            return [];
        }

        return
        [
            .. Names
                .Where(name => !shape.Selects(name))
                .Order(StringComparer.Ordinal)
                .Select(name => new IssueItem("warning", "element-ignored", $"'{name}' is not a top-level element of {resourceType}, so it selected nothing.", Location: null)),
        ];
    }

    /// <summary>Returns a trimmed copy of <paramref name="resource"/>; the original is untouched.</summary>
    public JsonObject Apply(JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        TypeShape? shape = ShapeOf((string?)resource["resourceType"] ?? string.Empty);
        var trimmed = new JsonObject();
        foreach (KeyValuePair<string, JsonNode?> property in resource)
        {
            if (IsKept(property.Key, shape))
            {
                trimmed.Add(property.Key, property.Value?.DeepClone());
            }
        }

        DropProfiles(trimmed);
        MarkSubsetted(trimmed);
        return trimmed;
    }

    private bool IsKept(string property, TypeShape? shape) =>
        AlwaysKept.Contains(property, StringComparer.Ordinal)
        || Selected(property, shape)
        || (property.StartsWith('_') && Selected(property[1..], shape));

    private bool Selected(string property, TypeShape? shape) =>
        Names.Contains(property) || (shape?.ChoiceElements.Any(choice => Names.Contains(choice) && IsTypedForm(property, choice)) ?? false);

    private TypeShape? ShapeOf(string resourceType)
    {
        if (!_shapes.TryGetValue(resourceType, out TypeShape? shape))
        {
            shape = _schema.GetTypeDefinition(resourceType) is { } type ? new TypeShape(type) : null;
            _shapes[resourceType] = shape;
        }

        return shape;
    }

    /// <summary>True for <c>valueQuantity</c> of choice element <c>value</c>.</summary>
    private static bool IsTypedForm(string property, string choice) =>
        property.Length > choice.Length && property.StartsWith(choice, StringComparison.Ordinal) && char.IsUpper(property[choice.Length]);

    private sealed class TypeShape
    {
        private readonly HashSet<string> _elements;

        public TypeShape(IType type)
        {
            _elements = [.. type.Children.Select(child => child.Info.Name)];
            ChoiceElements = [.. type.Children.Where(child => child.Info.IsChoiceElement).Select(child => child.Info.Name)];
        }

        public IReadOnlyList<string> ChoiceElements { get; }

        public bool Selects(string name) =>
            _elements.Contains(name) || ChoiceElements.Any(choice => IsTypedForm(name, choice));
    }

    /// <summary>
    /// <c>meta.profile</c> is the same long list on every resource of a type and is not something the caller asked for, so a
    /// trimmed resource leaves it out; the rest of <c>meta</c> (version, last update, tags) stays.
    /// </summary>
    private static void DropProfiles(JsonObject resource)
    {
        if (resource["meta"] is JsonObject meta)
        {
            meta.Remove("profile");
        }
    }

    private static void MarkSubsetted(JsonObject resource)
    {
        if (!resource.TryGetPropertyValue("meta", out JsonNode? metaNode))
        {
            metaNode = new JsonObject();
            resource.Add("meta", metaNode);
        }

        if (metaNode is not JsonObject meta)
        {
            return;
        }

        if (meta["tag"] is not JsonArray tags)
        {
            tags = [];
            meta["tag"] = tags;
        }

        bool alreadyMarked = tags.OfType<JsonObject>().Any(tag => (string?)tag["code"] == SubsettedCode && (string?)tag["system"] == SubsettedSystem);
        if (!alreadyMarked)
        {
            tags.Add(new JsonObject { ["system"] = SubsettedSystem, ["code"] = SubsettedCode });
        }
    }
}
