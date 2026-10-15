// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// Reads a CodeSystem resource into a <see cref="CodeSystemEntry"/>: concepts depth first, the hierarchy from nested
/// <c>concept.concept</c> and from the <c>parent</c>/<c>subsumedBy</c> and <c>child</c> properties (by code, or by a
/// property declared with the <c>http://hl7.org/fhir/concept-properties</c> URI), and every concept's ancestor set.
/// </summary>
internal static class CodeSystemReader
{
    /// <summary>The entry, or the reason the resource cannot be used (no <c>url</c>).</summary>
    public static (CodeSystemEntry? Entry, string? Problem) Read(JsonElement resource, ResourceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (TerminologyJson.NonEmpty(resource, "url") is not { } url)
        {
            return (null, "has no url");
        }

        PropertyRoles roles = PropertyRoles.Of(resource);
        var raw = new List<RawConcept>();
        var byCode = new Dictionary<string, RawConcept>(StringComparer.Ordinal);
        int duplicates = 0;
        Collect(TerminologyJson.Array(resource, "concept"), parent: null, roles, raw, byCode, ref duplicates);

        (Dictionary<string, List<string>> parents, int dangling) = ParentEdges(raw, byCode);
        (Dictionary<string, IReadOnlySet<string>> ancestors, int cycles) = Ancestors(raw, parents);

        Dictionary<string, List<string>> children = raw.ToDictionary(concept => concept.Code, _ => new List<string>(), StringComparer.Ordinal);
        foreach (RawConcept concept in raw)
        {
            foreach (string parent in parents[concept.Code])
            {
                children[parent].Add(concept.Code);
            }
        }

        List<Concept> concepts = [.. raw.Select((concept, order) => new Concept(
            concept.Code,
            concept.Display,
            concept.Definition,
            concept.Status,
            concept.Inactive,
            concept.Properties,
            concept.Designations,
            [.. parents[concept.Code].OrderBy(code => byCode[code].Order)],
            [.. children[concept.Code].OrderBy(code => byCode[code].Order)],
            order))];

        var entry = new CodeSystemEntry(
            url,
            TerminologyJson.NonEmpty(resource, "version"),
            TerminologyJson.NonEmpty(resource, "content"),
            TerminologyJson.NonEmpty(resource, "hierarchyMeaning"),
            TerminologyJson.Boolean(resource, "caseSensitive"),
            TerminologyJson.NonEmpty(resource, "supplements"),
            concepts,
            ancestors,
            new HierarchyProblems(dangling, cycles, duplicates),
            origin,
            TerminologyJson.Fingerprint(
                TerminologyJson.Member(resource, "content"),
                TerminologyJson.Member(resource, "caseSensitive"),
                TerminologyJson.Member(resource, "hierarchyMeaning"),
                TerminologyJson.Member(resource, "concept")));
        return (entry, null);
    }

    private static void Collect(IEnumerable<JsonElement> elements, RawConcept? parent, PropertyRoles roles, List<RawConcept> raw, Dictionary<string, RawConcept> byCode, ref int duplicates)
    {
        foreach (JsonElement element in elements)
        {
            if (TerminologyJson.NonEmpty(element, "code") is not { } code)
            {
                continue;
            }

            if (byCode.TryGetValue(code, out RawConcept? existing))
            {
                // A repeated code keeps its first definition, but its nesting still adds a parent edge.
                duplicates++;
                if (parent is not null)
                {
                    existing.ParentCodes.Add(parent.Code);
                }

                Collect(TerminologyJson.Array(element, "concept"), existing, roles, raw, byCode, ref duplicates);
                continue;
            }

            RawConcept concept = RawConcept.Read(element, code, raw.Count, roles);
            if (parent is not null)
            {
                concept.ParentCodes.Add(parent.Code);
            }

            raw.Add(concept);
            byCode.Add(code, concept);
            Collect(TerminologyJson.Array(element, "concept"), concept, roles, raw, byCode, ref duplicates);
        }
    }

    // Each concept's direct parents in the order they were declared; an edge to a code the system does not define is dropped.
    private static (Dictionary<string, List<string>> Parents, int Dangling) ParentEdges(List<RawConcept> raw, Dictionary<string, RawConcept> byCode)
    {
        Dictionary<string, List<string>> parents = raw.ToDictionary(concept => concept.Code, _ => new List<string>(), StringComparer.Ordinal);
        int dangling = 0;

        void Add(string child, string parent)
        {
            if (!byCode.ContainsKey(parent) || !byCode.ContainsKey(child))
            {
                dangling++;
                return;
            }

            if (child != parent && !parents[child].Contains(parent, StringComparer.Ordinal))
            {
                parents[child].Add(parent);
            }
        }

        foreach (RawConcept concept in raw)
        {
            foreach (string parent in concept.ParentCodes)
            {
                Add(concept.Code, parent);
            }

            foreach (string child in concept.ChildCodes)
            {
                Add(child, concept.Code);
            }
        }

        return (parents, dangling);
    }

    // Ancestor sets by an iterative depth-first walk (a deep chain cannot exhaust the stack). An edge to a concept still
    // being walked would close a cycle: it is removed from the hierarchy and counted.
    private static (Dictionary<string, IReadOnlySet<string>> Ancestors, int Cycles) Ancestors(List<RawConcept> raw, Dictionary<string, List<string>> parents)
    {
        var ancestors = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        int cycles = 0;
        var stack = new Stack<(string Code, int Next)>();
        foreach (RawConcept start in raw)
        {
            if (ancestors.ContainsKey(start.Code))
            {
                continue;
            }

            stack.Push((start.Code, 0));
            visiting.Add(start.Code);
            while (stack.Count > 0)
            {
                (string code, int next) = stack.Pop();
                List<string> edges = parents[code];
                if (next < edges.Count)
                {
                    stack.Push((code, next + 1));
                    string parent = edges[next];
                    if (visiting.Contains(parent))
                    {
                        edges.RemoveAt(next);
                        stack.Pop();
                        stack.Push((code, next));
                        cycles++;
                    }
                    else if (!ancestors.ContainsKey(parent))
                    {
                        visiting.Add(parent);
                        stack.Push((parent, 0));
                    }

                    continue;
                }

                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (string parent in edges)
                {
                    set.Add(parent);
                    set.UnionWith(ancestors[parent]);
                }

                ancestors[code] = set;
                visiting.Remove(code);
            }
        }

        return (ancestors, cycles);
    }
}
