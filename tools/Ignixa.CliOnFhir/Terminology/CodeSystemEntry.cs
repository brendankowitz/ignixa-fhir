// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// One loaded CodeSystem version: its concepts, hierarchy and the ancestor set of every concept, computed once when the
/// entry is built so subsumption and navigation are lookups.
/// </summary>
public sealed class CodeSystemEntry
{
    private readonly IReadOnlyDictionary<string, Concept> _concepts;
    private readonly IReadOnlyDictionary<string, Concept>? _conceptsIgnoringCase;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _ancestors;
    private readonly Lazy<IReadOnlyList<FoldedConcept>> _folded;

    internal CodeSystemEntry(
        string url,
        string? version,
        string? content,
        string? hierarchyMeaning,
        bool? caseSensitive,
        string? supplements,
        IReadOnlyList<Concept> concepts,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ancestors,
        HierarchyProblems hierarchyProblems,
        ResourceOrigin origin,
        string fingerprint)
    {
        Url = url;
        Version = version;
        Content = content;
        HierarchyMeaning = hierarchyMeaning;
        CaseSensitive = caseSensitive;
        Supplements = supplements;
        InOrder = concepts;
        _concepts = concepts.ToDictionary(concept => concept.Code, StringComparer.Ordinal);

        // FHIR: a CodeSystem that is not case sensitive matches codes ignoring case; the first spelling in definition order wins.
        _conceptsIgnoringCase = caseSensitive == false
            ? concepts.DistinctBy(concept => concept.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(concept => concept.Code, StringComparer.OrdinalIgnoreCase)
            : null;
        _ancestors = ancestors;
        HierarchyProblems = hierarchyProblems;
        Origin = origin;
        Fingerprint = fingerprint;
        _folded = new Lazy<IReadOnlyList<FoldedConcept>>(
            () => [.. concepts.Select(concept => new FoldedConcept(
                concept,
                concept.Display is null ? null : TextFolding.Fold(concept.Display),
                [.. concept.Designations.Select(designation => TextFolding.Fold(designation.Value))]))],
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Url { get; }

    public string? Version { get; }

    /// <summary>CodeSystem.content: complete, fragment, example, not-present or supplement (null when missing).</summary>
    public string? Content { get; }

    public string? HierarchyMeaning { get; }

    public bool? CaseSensitive { get; }

    /// <summary>The canonical this CodeSystem supplements; null unless <see cref="Content"/> is <c>supplement</c>.</summary>
    public string? Supplements { get; }

    /// <summary>Every concept in definition order (depth first through nested concepts).</summary>
    public IReadOnlyList<Concept> InOrder { get; }

    public HierarchyProblems HierarchyProblems { get; }

    /// <summary>Every concept with its display and designations folded for text search, computed on first use.</summary>
    public IReadOnlyList<FoldedConcept> Folded => _folded.Value;

    public ResourceOrigin Origin { get; }

    /// <summary>Hash of the semantic content (content mode, case sensitivity, hierarchy meaning, concepts), used to tell a true canonical collision from a repeat.</summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Whether the entry provides the system's concepts at all. A <c>not-present</c> stub (how core packages declare SNOMED
    /// CT, LOINC and the like) and a <c>supplement</c> do not: for answers, such a system is not loaded.
    /// </summary>
    public bool ProvidesConcepts => Content is not ("not-present" or "supplement");

    /// <summary>Whether a code missing from this entry is authoritatively not in the system version (<c>content = complete</c>).</summary>
    public bool IsComplete => Content == "complete";

    /// <summary>The concept with <paramref name="code"/>, ignoring case when the CodeSystem says it is not case sensitive; null when absent.</summary>
    public Concept? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return _concepts.GetValueOrDefault(code) ?? _conceptsIgnoringCase?.GetValueOrDefault(code);
    }

    /// <summary>Every ancestor of <paramref name="concept"/> (transitive parents, excluding itself).</summary>
    public IReadOnlySet<string> AncestorsOf(Concept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        return _ancestors[concept.Code];
    }

    /// <summary>
    /// The concepts reachable from <paramref name="concept"/> by parent (<paramref name="up"/>) or child edges, each with its
    /// shortest distance, up to <paramref name="maxDepth"/> steps; ordered by distance, then definition order.
    /// </summary>
    public IReadOnlyList<(Concept Concept, int Distance)> Walk(Concept concept, bool up, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [concept.Code] = 0 };
        List<(Concept, int)> reached = [];
        List<Concept> frontier = [concept];
        for (int depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            List<Concept> next = [];
            foreach (string code in frontier.SelectMany(current => up ? current.Parents : current.Children))
            {
                if (distances.TryAdd(code, depth))
                {
                    next.Add(_concepts[code]);
                }
            }

            next.Sort((left, right) => left.Order.CompareTo(right.Order));
            reached.AddRange(next.Select(found => (found, depth)));
            frontier = next;
        }

        return reached;
    }

    /// <summary>The concept and every descendant of it, in definition order.</summary>
    public IEnumerable<Concept> SelfAndDescendants(Concept concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        return InOrder.Where(candidate => candidate.Code == concept.Code || _ancestors[candidate.Code].Contains(concept.Code));
    }
}
