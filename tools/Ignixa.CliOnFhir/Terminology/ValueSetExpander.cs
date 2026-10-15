// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// Expands ValueSets over one <see cref="TerminologyStore"/> snapshot. A rule selects a code system's concepts (listed, by
/// filter, or all of them) and/or other ValueSets (intersected); excludes remove. Filters evaluated: <c>is-a</c>,
/// <c>descendent-of</c>, <c>is-not-a</c>, <c>generalizes</c>, <c>child-of</c> on <c>concept</c>, and <c>=</c>, <c>in</c>
/// and <c>exists</c> on a concept property. Anything that cannot be evaluated over loaded content (another filter, a system
/// or ValueSet not loaded, content not complete, a cycle) makes the expansion incomplete, with an issue saying which.
/// Include cycles are found on the static include graph (<see cref="ValueSetIncludeGraph"/>) before anything is expanded,
/// and no include inside a cycle is followed, so every ValueSet in a request is expanded once and the result is the same
/// whichever ValueSet of a cycle is asked for first. Results are cached on the snapshot.
/// </summary>
internal sealed class ValueSetExpander(TerminologyStore store)
{
    /// <summary>The most codes one expansion may hold; a larger one fails rather than truncating.</summary>
    public const int MaxCodes = 100_000;

    public const string TooLargeCode = "expansion-too-large";

    public const string CycleCode = "value-set-cycle";

    /// <summary>
    /// The expansion of <paramref name="valueSet"/>, computed once per snapshot. A cancelled computation is not kept, so
    /// the next request computes it afresh.
    /// </summary>
    public ValueSetExpansion Expand(ValueSetEntry valueSet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(valueSet);

        (string Url, string? Version) key = (valueSet.Url, valueSet.Version);
        while (true)
        {
            Lazy<ValueSetExpansion> expansion = store.Expansions.GetOrAdd(
                key,
                _ => new Lazy<ValueSetExpansion>(() => ExpandRequest(valueSet, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return expansion.Value;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                // The Lazy keeps the cancellation; drop it. When it was another caller's, this caller computes it itself. A
                // failure, or an OperationCanceledException no token asked for, is not retried.
                store.Expansions.TryRemove(KeyValuePair.Create(key, expansion));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    // Every ValueSet the request reaches is expanded once, in dependency order, after the cycles are found; a ValueSet
    // included along many paths (a diamond) is looked up, not recomputed.
    private ValueSetExpansion ExpandRequest(ValueSetEntry valueSet, CancellationToken cancellationToken)
    {
        ValueSetIncludeGraph graph = ValueSetIncludeGraph.Build(store, valueSet, cancellationToken);
        var expanded = new Dictionary<ValueSetEntry, ValueSetExpansion>(ReferenceEqualityComparer.Instance);
        foreach (ValueSetEntry entry in graph.DependenciesFirst)
        {
            expanded[entry] = Compute(new ExpansionContext(entry, graph, expanded, cancellationToken));
        }

        return expanded[valueSet];
    }

    private ValueSetExpansion Compute(ExpansionContext context)
    {
        ValueSetEntry valueSet = context.ValueSet;
        context.Used.Add(valueSet.Origin);
        if (!valueSet.HasCompose)
        {
            foreach (ExpansionCode code in valueSet.StoredExpansion)
            {
                context.Result.Add(code);
            }

            context.Notes.Add(new IssueItem("information", "stored-expansion", $"ValueSet {Name(valueSet)} has no compose; its codes are the expansion its source ships, not computed by this tool.", valueSet.Origin.ToEvidence().File));
            return context.Finish();
        }

        foreach (ValueSetRule rule in valueSet.Includes)
        {
            foreach (ExpansionCode code in Evaluate(rule, context, "include").Codes)
            {
                context.Result.Add(code);
                ThrowIfTooLarge(context.Result.Count, valueSet);
            }
        }

        foreach (ValueSetRule rule in valueSet.Excludes)
        {
            int incompleteBefore = context.Incomplete.Count;
            ExpansionBuilder excluded = Evaluate(rule, context, "exclude");
            context.Result.RemoveWhere(excluded.Contains);
            context.ExclusionsComplete &= context.Incomplete.Count == incompleteBefore;
        }

        if (valueSet.IncludesInactive == false)
        {
            context.Result.RemoveWhere(code => code.Inactive == true);
        }

        return context.Finish();
    }

    private ExpansionBuilder Evaluate(ValueSetRule rule, ExpansionContext context, string part)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        List<ExpansionBuilder> sets = [];
        if (rule.System is { } system)
        {
            sets.Add(EvaluateSystem(system, rule, context, part));
        }

        foreach (string canonical in rule.ValueSets)
        {
            sets.Add(EvaluateValueSet(canonical, context, part));
        }

        if (sets.Count == 0)
        {
            context.Incomplete.Add(Problem(context, "rule-unusable", $"a compose.{part} of ValueSet {Name(context.ValueSet)} has neither a system nor a valueSet"));
            return new ExpansionBuilder();
        }

        ExpansionBuilder result = sets[0];
        foreach (ExpansionBuilder other in sets.Skip(1))
        {
            result.RemoveWhere(code => !other.Contains(code));
        }

        return result;
    }

    private ExpansionBuilder EvaluateSystem(string system, ValueSetRule rule, ExpansionContext context, string part)
    {
        var codes = new ExpansionBuilder();
        CodeSystemResolution resolution = store.ResolveCodeSystem(system, rule.Version);
        if (resolution.OtherVersions is { } others)
        {
            context.Notes.Add(others);
        }

        if (resolution.Entry is not { } codeSystem)
        {
            if (rule.Concepts.Count > 0 && rule.Filters.Count == 0)
            {
                foreach (ValueSetRuleConcept listed in rule.Concepts)
                {
                    codes.Add(new ExpansionCode(system, rule.Version, listed.Code, listed.Display, Inactive: null));
                }

                context.Notes.Add(Problem(context, "codes-not-checked", $"{Describe(resolution)}, so the {rule.Concepts.Count} codes a compose.{part} of ValueSet {Name(context.ValueSet)} lists for it are taken as listed, unchecked (displays as the ValueSet gives them, status unknown)", "information"));
                return codes;
            }

            context.Incomplete.Add(Problem(context, resolution.Outcome, $"{Describe(resolution)}, so a compose.{part} of ValueSet {Name(context.ValueSet)} over it cannot be evaluated"));
            return codes;
        }

        context.Used.Add(codeSystem.Origin);
        foreach (IssueItem conflict in store.ConflictIssues("CodeSystem", codeSystem.Url, codeSystem.Version))
        {
            context.Notes.Add(conflict);
        }

        if (rule.Concepts.Count > 0)
        {
            AddListed(codeSystem, rule, context, part, codes);
            return codes;
        }

        if (!codeSystem.IsComplete)
        {
            context.Incomplete.Add(Problem(context, "content-not-complete", $"CodeSystem {Name(codeSystem)} from {codeSystem.Origin.Source.Name} has content '{TerminologyNames.Token(codeSystem.Content ?? "(none)")}', so selecting its concepts for a compose.{part} of ValueSet {Name(context.ValueSet)} may miss some"));
        }

        IEnumerable<Concept> selected = codeSystem.InOrder;
        foreach (ValueSetFilter filter in rule.Filters)
        {
            if (Filter(codeSystem, filter, selected, context, part) is { } filtered)
            {
                selected = filtered;
            }
            else
            {
                context.Incomplete.Add(Problem(context, "filter-not-supported", $"the filter '{TerminologyNames.Token(filter.Property ?? "(none)")} {TerminologyNames.Token(filter.Op ?? "(none)")}' in a compose.{part} of ValueSet {Name(context.ValueSet)} is not evaluated by this tool"));
                return codes;
            }
        }

        foreach (Concept concept in selected)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            codes.Add(new ExpansionCode(codeSystem.Url, codeSystem.Version, concept.Code, concept.Display, concept.Inactive, concept.NotSelectable));
            ThrowIfTooLarge(codes.Count, context.ValueSet);
        }

        return codes;
    }

    private static void AddListed(CodeSystemEntry codeSystem, ValueSetRule rule, ExpansionContext context, string part, ExpansionBuilder codes)
    {
        int missing = 0;
        foreach (ValueSetRuleConcept listed in rule.Concepts)
        {
            if (codeSystem.Find(listed.Code) is { } concept)
            {
                codes.Add(new ExpansionCode(codeSystem.Url, codeSystem.Version, concept.Code, listed.Display ?? concept.Display, concept.Inactive, concept.NotSelectable));
            }
            else if (codeSystem.IsComplete)
            {
                missing++;
            }
            else
            {
                codes.Add(new ExpansionCode(codeSystem.Url, codeSystem.Version, listed.Code, listed.Display, Inactive: null));
            }
        }

        if (missing > 0)
        {
            context.Notes.Add(Problem(context, "value-set-code-not-in-system", $"a compose.{part} of ValueSet {Name(context.ValueSet)} lists {missing} codes that CodeSystem {Name(codeSystem)} (content complete) does not define; they are left out", "warning"));
        }
    }

    private ExpansionBuilder EvaluateValueSet(string canonical, ExpansionContext context, string part)
    {
        CanonicalResolution<ValueSetEntry> resolution = store.ResolveValueSet(canonical);
        if (resolution.OtherVersions is { } others)
        {
            context.Notes.Add(others);
        }

        if (resolution.Entry is not { } nested)
        {
            context.Incomplete.Add(Problem(context, "value-set-not-loaded", $"ValueSet {TerminologyNames.Canonical(resolution.Url, resolution.Version)}, used by a compose.{part} of ValueSet {Name(context.ValueSet)}, is not loaded"));
            return new ExpansionBuilder();
        }

        if (context.Graph.IsCycleEdge(context.ValueSet, nested))
        {
            string cycle = ReferenceEquals(nested, context.ValueSet)
                ? $"ValueSet {Name(nested)} includes itself in a compose.{part}"
                : $"ValueSet {Name(nested)}, used by a compose.{part} of ValueSet {Name(context.ValueSet)}, includes it in turn (directly or through other ValueSets)";
            context.Incomplete.Add(Problem(context, CycleCode, $"{cycle}, so that include is not followed"));
            return new ExpansionBuilder();
        }

        ValueSetExpansion expansion = context.Expanded[nested];
        context.Used.AddRange(expansion.Used);
        context.Incomplete.AddRange(expansion.Incomplete);
        context.Notes.AddRange(expansion.Notes);
        context.ExclusionsComplete &= expansion.ExclusionsComplete;
        var codes = new ExpansionBuilder();
        foreach (ExpansionCode code in expansion.Codes)
        {
            codes.Add(code);
        }

        return codes;
    }

    // Null when the filter cannot be evaluated.
    private static IEnumerable<Concept>? Filter(CodeSystemEntry codeSystem, ValueSetFilter filter, IEnumerable<Concept> selected, ExpansionContext context, string part)
    {
        if (filter.Op is null || filter.Property is null || filter.Value is null)
        {
            return null;
        }

        if (filter.Property == "concept" && filter.Op is "is-a" or "descendent-of" or "is-not-a" or "generalizes" or "child-of")
        {
            if (codeSystem.Find(filter.Value) is not { } anchor)
            {
                context.Notes.Add(Problem(context, "filter-code-not-in-system", $"a '{filter.Op}' filter in a compose.{part} of ValueSet {Name(context.ValueSet)} names a code CodeSystem {Name(codeSystem)} does not define", "warning"));
                return filter.Op == "is-not-a" ? selected : [];
            }

            HashSet<string> family = filter.Op switch
            {
                "is-a" or "is-not-a" => [.. codeSystem.SelfAndDescendants(anchor).Select(concept => concept.Code)],
                "descendent-of" => [.. codeSystem.SelfAndDescendants(anchor).Select(concept => concept.Code).Where(code => code != anchor.Code)],
                "generalizes" => [anchor.Code, .. codeSystem.AncestorsOf(anchor)],
                _ => [.. anchor.Children],
            };
            return filter.Op == "is-not-a" ? selected.Where(concept => !family.Contains(concept.Code)) : selected.Where(concept => family.Contains(concept.Code));
        }

        // Filters on the concept itself (its code or display) other than the hierarchy ones are not evaluated, rather than
        // being read as a property no concept has.
        if (filter.Property is "concept" or "code" or "display")
        {
            return null;
        }

        return filter.Op switch
        {
            "=" => selected.Where(concept => concept.Properties.Any(property => property.Code == filter.Property && property.Value == filter.Value)),
            "in" => selected.Where(concept => concept.Properties.Any(property => property.Code == filter.Property && filter.Value.Split(',').Contains(property.Value, StringComparer.Ordinal))),
            "exists" when filter.Value is "true" or "false" => selected.Where(concept => concept.Properties.Any(property => property.Code == filter.Property) == (filter.Value == "true")),
            _ => null,
        };
    }

    private static void ThrowIfTooLarge(int count, ValueSetEntry valueSet)
    {
        if (count > MaxCodes)
        {
            throw ToolRequestException.Error(TooLargeCode, $"ValueSet {Name(valueSet)} expands to more than {MaxCodes} codes, which this tool does not hold in memory; expand a narrower ValueSet.");
        }
    }

    private static string Describe(CodeSystemResolution resolution) => resolution.Outcome == CodeSystemResolution.VersionNotLoaded
        ? $"CodeSystem {TerminologyNames.Canonical(resolution.Url, resolution.Version)} is not loaded (other versions are)"
        : resolution.StubReason is { } stub
            ? $"CodeSystem {TerminologyNames.Canonical(resolution.Url, null)} is not loaded ({stub})"
            : $"CodeSystem {TerminologyNames.Canonical(resolution.Url, resolution.Version)} is not loaded";

    private static IssueItem Problem(ExpansionContext context, string code, string text, string severity = "warning") =>
        new(severity, code, $"{char.ToUpperInvariant(text[0])}{text[1..]}.", context.ValueSet.Origin.ToEvidence().File);

    private static string Name(ValueSetEntry valueSet) => TerminologyNames.Canonical(valueSet.Url, valueSet.Version);

    private static string Name(CodeSystemEntry codeSystem) => TerminologyNames.Canonical(codeSystem.Url, codeSystem.Version);
}
