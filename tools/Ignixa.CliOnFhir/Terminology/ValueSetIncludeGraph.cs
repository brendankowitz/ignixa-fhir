// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// The ValueSets one expansion reaches through <c>compose.include.valueSet</c> and <c>compose.exclude.valueSet</c>, resolved
/// over one snapshot, with its cycles found before anything is expanded. ValueSets that include each other, directly or
/// through others, form one strongly connected component; an include between two members of one component is a cycle edge.
/// Because those edges are known up front and are never followed, each ValueSet's expansion depends only on the ValueSet
/// itself, not on the path that reached it, so it is computed once however many paths lead to it.
/// </summary>
internal sealed class ValueSetIncludeGraph
{
    private readonly Dictionary<ValueSetEntry, int> _component;

    private ValueSetIncludeGraph(List<ValueSetEntry> dependenciesFirst, Dictionary<ValueSetEntry, int> component)
    {
        DependenciesFirst = dependenciesFirst;
        _component = component;
    }

    /// <summary>
    /// Every reachable ValueSet, ordered so that each comes after every ValueSet it includes, except those it reaches only
    /// through a cycle edge.
    /// </summary>
    public IReadOnlyList<ValueSetEntry> DependenciesFirst { get; }

    /// <summary>Whether <paramref name="from"/> including <paramref name="to"/> closes a cycle (a self-include among them).</summary>
    public bool IsCycleEdge(ValueSetEntry from, ValueSetEntry to) => _component[from] == _component[to];

    /// <summary>
    /// Tarjan's strongly connected components over the ValueSets reachable from <paramref name="root"/>, iterative so that a
    /// long include chain in folder data cannot exhaust the stack. Components come out with every component they reach
    /// before them, which is the order the expansions are computed in.
    /// </summary>
    public static ValueSetIncludeGraph Build(TerminologyStore store, ValueSetEntry root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(root);

        var index = new Dictionary<ValueSetEntry, int>(ReferenceEqualityComparer.Instance);
        var low = new Dictionary<ValueSetEntry, int>(ReferenceEqualityComparer.Instance);
        var children = new Dictionary<ValueSetEntry, List<ValueSetEntry>>(ReferenceEqualityComparer.Instance);
        var component = new Dictionary<ValueSetEntry, int>(ReferenceEqualityComparer.Instance);
        var open = new Stack<ValueSetEntry>();
        var onOpen = new HashSet<ValueSetEntry>(ReferenceEqualityComparer.Instance);
        var work = new Stack<(ValueSetEntry Node, int Next)>();
        List<ValueSetEntry> order = [];
        int components = 0;

        void Visit(ValueSetEntry node)
        {
            int position = index.Count;
            index[node] = position;
            low[node] = position;
            children[node] = Included(store, node);
            open.Push(node);
            onOpen.Add(node);
            work.Push((node, 0));
        }

        Visit(root);
        while (work.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (ValueSetEntry node, int next) = work.Pop();
            List<ValueSetEntry> edges = children[node];
            if (next < edges.Count)
            {
                work.Push((node, next + 1));
                ValueSetEntry child = edges[next];
                if (!index.ContainsKey(child))
                {
                    Visit(child);
                }
                else if (onOpen.Contains(child))
                {
                    low[node] = Math.Min(low[node], index[child]);
                }

                continue;
            }

            if (work.Count > 0)
            {
                ValueSetEntry parent = work.Peek().Node;
                low[parent] = Math.Min(low[parent], low[node]);
            }

            if (low[node] == index[node])
            {
                int id = components++;
                ValueSetEntry member;
                do
                {
                    member = open.Pop();
                    onOpen.Remove(member);
                    component[member] = id;
                    order.Add(member);
                }
                while (!ReferenceEquals(member, node));
            }
        }

        return new ValueSetIncludeGraph(order, component);
    }

    // The ValueSets a ValueSet's rules name that are loaded, in rule order; one not loaded is reported when expanding.
    private static List<ValueSetEntry> Included(TerminologyStore store, ValueSetEntry valueSet) =>
    [
        .. valueSet.Includes.Concat(valueSet.Excludes)
            .SelectMany(rule => rule.ValueSets)
            .Select(canonical => store.ResolveValueSet(canonical).Entry)
            .OfType<ValueSetEntry>(),
    ];
}
