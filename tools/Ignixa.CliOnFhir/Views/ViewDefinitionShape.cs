// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.SqlOnFhir.packages;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Which properties a ViewDefinition may have, read from the ViewDefinition StructureDefinition that
/// <c>Ignixa.SqlOnFhir</c> embeds. The evaluator ignores a property it does not know, so a misspelt one (<c>selct</c>,
/// <c>forEch</c>) would otherwise make a view that quietly does less than it says.
/// </summary>
internal static class ViewDefinitionShape
{
    private const string RootPath = "ViewDefinition";

    private static readonly Lazy<Elements> Loaded = new(Load);

    /// <summary>
    /// The JSON path of the first property the ViewDefinition resource (or one of its backbone elements: constant, select, column,
    /// tag, where) has that the definition does not define, with array indexes; null when there is none. Every name on the path
    /// before the last is defined, so only the last, the unknown one, can be folder text.
    /// </summary>
    public static (string Parent, string Property)? UnknownProperty(JsonObject viewDefinition)
    {
        ArgumentNullException.ThrowIfNull(viewDefinition);

        return Find(viewDefinition, RootPath, string.Empty, Loaded.Value);
    }

    private static (string Parent, string Property)? Find(JsonObject node, string elementPath, string jsonPath, Elements elements)
    {
        foreach ((string key, JsonNode? value) in node)
        {
            if (elementPath == RootPath && key == "resourceType")
            {
                continue;
            }

            string name = key.StartsWith('_') ? key[1..] : key;
            if (!elements.Defines(elementPath, name))
            {
                return (jsonPath, key);
            }

            string childPath = elements.Resolve($"{elementPath}.{name}");
            if (key.StartsWith('_') || !elements.HasChildren(childPath))
            {
                continue;
            }

            foreach ((JsonObject child, string where) in Objects(value, $"{jsonPath}{(jsonPath.Length == 0 ? string.Empty : ".")}{name}"))
            {
                if (Find(child, childPath, where, elements) is { } unknown)
                {
                    return unknown;
                }
            }
        }

        return null;
    }

    private static IEnumerable<(JsonObject Object, string Path)> Objects(JsonNode? value, string path)
    {
        if (value is JsonObject single)
        {
            yield return (single, path);
        }
        else if (value is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject item)
                {
                    yield return (item, $"{path}[{i}]");
                }
            }
        }
    }

    private static Elements Load()
    {
        Assembly assembly = typeof(SqlOnFhirEmbeddedPackage).Assembly;
        string resource = $"{new SqlOnFhirEmbeddedPackage().ResourcePrefix}.StructureDefinition-ViewDefinition.json";
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded ViewDefinition definition '{resource}' is missing from {assembly.GetName().Name}.");
        JsonNode definition = JsonNode.Parse(stream)!;
        var elements = new Elements();
        foreach (JsonNode? element in definition["snapshot"]!["element"]!.AsArray())
        {
            elements.Add((string)element!["path"]!, (string?)element["contentReference"]);
        }

        return elements;
    }

    private sealed class Elements
    {
        private readonly Dictionary<string, HashSet<string>> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _choices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _references = new(StringComparer.Ordinal);

        public void Add(string path, string? contentReference)
        {
            int dot = path.LastIndexOf('.');
            if (dot < 0)
            {
                return;
            }

            string parent = path[..dot];
            string leaf = path[(dot + 1)..];
            if (leaf.EndsWith("[x]", StringComparison.Ordinal))
            {
                GetOrAdd(_choices, parent).Add(leaf[..^3]);
            }
            else
            {
                GetOrAdd(_names, parent).Add(leaf);
            }

            if (contentReference is not null)
            {
                _references[path] = contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..];
            }
        }

        public bool Defines(string elementPath, string name) =>
            (_names.TryGetValue(elementPath, out HashSet<string>? names) && names.Contains(name))
            || (_choices.TryGetValue(elementPath, out List<string>? choices) && choices.Exists(prefix => name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length])));

        public string Resolve(string path) => _references.GetValueOrDefault(path, path);

        public bool HasChildren(string path) => _names.ContainsKey(path);

        private static HashSet<string> GetOrAdd(Dictionary<string, HashSet<string>> map, string key) =>
            map.TryGetValue(key, out HashSet<string>? value) ? value : map[key] = [];

        private static List<string> GetOrAdd(Dictionary<string, List<string>> map, string key) =>
            map.TryGetValue(key, out List<string>? value) ? value : map[key] = [];
    }
}
