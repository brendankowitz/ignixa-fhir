// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// <c>plan_fhir_search</c> / <c>cli-on-fhir search --check</c>: how a query would run, without running it. The query goes
/// through the same parse and bind path as <see cref="ResourceSearcher"/> (<see cref="ResourceSearcher.Parse"/>: digest ids,
/// the parameter policy, chain diagnosis, the compartment) and is then compiled by the same engine over no resources
/// (<see cref="ResourceSearcher.Compile"/>), so a query the plan accepts is one the search accepts, and every refusal has the
/// search's issue code. Each parameter is reported with the definitions it resolved to along any chain, its modifiers and
/// prefixes, and how the folder's index populates each definition; a definition no resource populates is a
/// <c>parameter-unpopulated</c> warning, since the filter cannot match anything here.
/// </summary>
public static class SearchPlanner
{
    public const string UnpopulatedCode = "parameter-unpopulated";

    public static ToolEnvelope<PlanFacts?> Plan(FolderSession session, SearchRequest request, bool execute = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<PlanFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            // The plan shows the query the search runs: relative dates resolved as the search resolves them, and both queries stated.
            AsOf? callAsOf = AsOfClock.Argument(request.AsOf);
            (SearchRequest resolved, IReadOnlyList<ResolvedParameter> parameters, IssueItem? note) = RelativeDates.Resolve(session.Definitions, request, () => callAsOf ?? session.AsOfClock.Resolve().Value);
            (IReadOnlyList<ResolvedParameter> shown, IReadOnlyList<OmittedItem> unlisted) = note is null ? ([], []) : RelativeDates.ForFacts(request.Query!, parameters);
            ToolEnvelope<PlanFacts?> envelope = Execute(session, resolved, execute, note is null ? null : (QueryEcho.Of(request.Query!), shown, note, unlisted), cancellationToken);
            return callAsOf is null ? envelope : envelope with { AsOf = callAsOf };
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<PlanFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<PlanFacts?> Execute(
        FolderSession session,
        SearchRequest request,
        bool execute,
        (string OriginalQuery, IReadOnlyList<ResolvedParameter> Parameters, IssueItem Note, IReadOnlyList<OmittedItem> Unlisted)? relative,
        CancellationToken cancellationToken)
    {
        // The search's own request checks, in the search's order, so a request the search refuses the plan refuses too.
        ResourceSearcher.ValidatePaging(request);
        (ParsedSearch parsed, List<IssueItem> issues, List<OmittedItem> parseOmitted) = ResourceSearcher.Parse(session, request);
        _ = ResourceSearcher.EffectiveCount(request.Count ?? parsed.RequestedCount, issues);
        ResourceSearcher.Compile(session, parsed);
        issues.AddRange(ReferenceFollowChecks.InapplicableIncludes(parsed.Options));

        var population = new ParameterPopulation(session);
        List<ParameterPlan> parameters = [];
        List<IssueItem> planIssues = [];
        foreach (ParameterTrace trace in parsed.Traces)
        {
            (ParameterPlan plan, bool absentMatches) = Describe(trace, request.ResourceType, population);
            parameters.Add(plan);
            if (!absentMatches && Unpopulated(plan) is { } issue)
            {
                planIssues.Add(issue);
            }
        }

        parameters.AddRange(parsed.Options.Sort.Select(sort => Sort(sort, request.ResourceType, population)));
        parameters.AddRange(parsed.Options.Include.Concat(parsed.Options.RevInclude).Select(include => Include(include, population)));
        if (population.DigestedValues > 0)
        {
            planIssues.Add(new IssueItem("information", "invalid-value", $"{population.DigestedValues} top values are shown as digests ('~' and 16 hex digits): their system is not an http(s) or urn URI, or their code is not shaped as one. A search cannot use a digest.", Location: null));
        }

        var facts = new PlanFacts(request.ResourceType, QueryEcho.Of(request.Query ?? string.Empty), request.Compartment is null ? null : QueryEcho.Of(request.Compartment), population.Indexed(request.ResourceType), parameters, execute)
        {
            OriginalQuery = relative?.OriginalQuery,
            ResolvedParameters = relative?.Parameters,
        };
        if (relative is { } rewrite)
        {
            planIssues.Insert(0, rewrite.Note);
        }

        bool queryCut = QueryEcho.IsCut(request.Query);
        List<OmittedItem> diagnosticsOmitted = [.. parseOmitted, .. relative?.Unlisted ?? []];
        List<IssueItem> diagnostics = [];
        ResourceSearcher.AddDiagnostics(session, population.Source, diagnostics, diagnosticsOmitted);
        if (!execute)
        {
            DateTimeOffset builtAt = population.Source.IndexesRead.Select(index => index.BuiltAt).DefaultIfEmpty(session.Catalog.BuiltAt).Max();
            bool repeatable = SearchNarrowing.Repeatable(request);
            List<NextCall> next = repeatable ? [Search(request)] : [];
            if (queryCut)
            {
                diagnosticsOmitted.Add(QueryEcho.Cut(request.Query!, "facts.query"));
            }

            if (!repeatable)
            {
                diagnosticsOmitted.Add(QueryEcho.NextLeftOut("the search this plan checks"));
            }

            PlanFit fit = PlanFit.Within(session, facts, [.. issues, .. diagnostics], planIssues, diagnosticsOmitted, next, session.MaxResponseBytes);
            return EnvelopeAssembler.Create<PlanFacts?>(session, fit.Facts, builtAt, [], fit.Omitted, [], fit.Issues, cancellationToken) with { Next = next };
        }

        // The search reports its own parse warnings and the diagnostics of the indexes it reads.
        return WithResults(session, request, facts, planIssues, [.. queryCut ? [QueryEcho.Cut(request.Query!, "facts.query")] : Array.Empty<OmittedItem>(), .. relative?.Unlisted ?? []], cancellationToken);
    }

    /// <summary>
    /// The search's own envelope with the plan added to its facts and the plan's own warnings to its issues. The plan is fitted
    /// first, to the whole cap, and the search is told to leave the plan's size unused, so the page fills what the plan leaves
    /// and nothing of the plan is cut while there is room. Only when the two together still exceed the cap is the plan fitted
    /// to what the search's response leaves, and the search asked again.
    /// </summary>
    private static ToolEnvelope<PlanFacts?> WithResults(FolderSession session, SearchRequest request, PlanFacts facts, List<IssueItem> planIssues, List<OmittedItem> planOmitted, CancellationToken cancellationToken)
    {
        const int Slack = 1024;
        int planBudget = session.MaxResponseBytes;
        while (true)
        {
            PlanFit fit = PlanFit.Within(session, facts, [], planIssues, planOmitted, [], planBudget);
            int extra = EnvelopeJson.SizeOf(fit.Facts) + EnvelopeJson.SizeOf(fit.Issues) + EnvelopeJson.SizeOf(fit.Omitted);
            ToolEnvelope<SearchFacts?> search = ResourceSearcher.Search(session, request, extra, cancellationToken);
            if (search.Facts is not { } results)
            {
                return new ToolEnvelope<PlanFacts?>(null, search.Evidence, search.Omitted, search.Absent, search.Issues, search.Freshness) { AsOf = search.AsOf };
            }

            // The search's own items name its facts, which are facts.results here.
            List<OmittedItem> searchOmitted = [.. search.Omitted.Select(item => item.What.StartsWith("facts.", StringComparison.Ordinal) ? item with { What = "facts.results." + item.What["facts.".Length..] } : item)];
            var envelope = new ToolEnvelope<PlanFacts?>(fit.Facts with { Results = results }, search.Evidence, [.. searchOmitted, .. fit.Omitted], search.Absent, [.. search.Issues, .. fit.Issues], search.Freshness) { Next = search.Next, AsOf = search.AsOf };
            int size = EnvelopeJson.SizeOf(envelope);
            if (size <= session.MaxResponseBytes || planBudget <= Slack)
            {
                return envelope;
            }

            // Each round the plan gets what the search's response leaves, and always less than the round before, so this ends.
            planBudget = Math.Max(Slack, Math.Min(planBudget - Slack, session.MaxResponseBytes - EnvelopeJson.SizeOf(search) - Slack));
        }
    }

    /// <returns>The plan, and whether a resource with no value for the parameter can match (<c>:not</c>, <c>:missing=true</c>).</returns>
    private static (ParameterPlan Plan, bool AbsentMatches) Describe(ParameterTrace trace, string resourceType, ParameterPopulation population)
    {
        var collected = new PlanCollector(population);
        if (trace.Ir is { } ir)
        {
            collected.Walk(ir, [resourceType]);
        }

        // Some modifiers (:text) lower to a plain string test that no longer names them, so the key's own modifier is added.
        if (KeyModifier(trace.Key) is { } written)
        {
            collected.Modifiers.Add(written);
        }

        (string outcome, string? reason) = trace.Outcome switch
        {
            ParameterOutcome.Compiled => ("compiled", (string?)null),
            ParameterOutcome.KnownMiss miss => ("known-miss", miss.Reason),
            ParameterOutcome.Ignored ignored => ("ignored", ignored.Reason),
            ParameterOutcome.Failed failed => ("failed", failed.Message),
            _ => throw new InvalidOperationException($"Unknown parameter outcome {trace.Outcome.GetType().Name}."),
        };

        bool comparable = trace.DataType is SearchParamType.Number or SearchParamType.Date or SearchParamType.Quantity;
        var plan = new ParameterPlan(QueryEcho.Of(trace.Key), QueryEcho.Of(trace.Value), outcome, reason, [.. collected.Modifiers.Order(StringComparer.Ordinal)], comparable ? [.. collected.Prefixes.Order(StringComparer.Ordinal)] : [], collected.Bindings);
        return (plan, collected.Modifiers.Contains("not") || collected.MissingMatches);
    }

    private static ParameterPlan Sort(SortExpression sort, string resourceType, ParameterPopulation population) =>
        new("_sort", (sort.SortOrder == Ignixa.Search.Expressions.SortOrder.Descending ? "-" : string.Empty) + sort.Parameter.Code, "compiled", null, [], [], [Binding("sort", resourceType, sort.Parameter, targets: null, population)]);

    private static ParameterPlan Include(IncludeExpression include, ParameterPopulation population)
    {
        string name = (include.Reversed ? "_revinclude" : "_include") + (include.Iterate ? ":iterate" : string.Empty);
        string parameter = include.WildCard ? "*" : include.ReferenceSearchParameter.Code;
        string value = $"{include.SourceResourceType}:{parameter}{(string.IsNullOrEmpty(include.TargetResourceType) ? string.Empty : ":" + include.TargetResourceType)}";
        IReadOnlyList<ParameterBinding> bindings = include.WildCard
            ? []
            : [Binding(include.Reversed ? "revinclude" : "include", include.SourceResourceType, include.ReferenceSearchParameter, [.. include.ReferencedTypes], population)];
        return new ParameterPlan(name, value, "compiled", null, [], [], bindings);
    }

    /// <summary>The modifier written on the key's last link (<c>code:text</c>, <c>subject:Patient.name:exact</c>), not a type; null for none or a <c>_has</c> key.</summary>
    private static string? KeyModifier(string key)
    {
        if (key.StartsWith("_has:", StringComparison.Ordinal))
        {
            return null;
        }

        string last = key[(key.LastIndexOf('.') + 1)..];
        int colon = last.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon < last.Length - 1 && char.IsLower(last[colon + 1]) ? last[(colon + 1)..] : null;
    }

    internal static ParameterBinding Binding(string role, string resourceType, SearchParameterInfo parameter, IReadOnlyList<string>? targets, ParameterPopulation population)
    {
        (int populated, IReadOnlyList<TopValue>? top) = population.Count(resourceType, parameter);
        return new ParameterBinding(
            role,
            resourceType,
            parameter.Code,
            parameter.Url.OriginalString,
            Literal(parameter.Type),
            parameter.Expression,
            targets,
            populated,
            population.Indexed(resourceType),
            role == "filter" ? top : null);
    }

    /// <summary>
    /// The warning for a compiled parameter that cannot match anything in the folder: a link of it (all of its bindings, for a
    /// link that applies to several types) that no resource populates, or a token filter whose values all lack a code (text
    /// only), which a code cannot match; null when it can match.
    /// </summary>
    private static IssueItem? Unpopulated(ParameterPlan plan)
    {
        if (plan.Outcome != "compiled")
        {
            return null;
        }

        string parameter = $"'{plan.Name}={plan.Value}'";
        if (plan.Resolves.GroupBy(binding => (binding.Role, binding.Code)).FirstOrDefault(link => link.All(binding => binding.Populated == 0)) is { } empty)
        {
            ParameterBinding first = empty.First();
            return new IssueItem("warning", UnpopulatedCode, $"{parameter} cannot match anything in this folder: none of its {first.Indexed} {first.ResourceType} resources has a value for {first.Code}.", Location: null);
        }

        if (!plan.Modifiers.Contains("text") && plan.Resolves.FirstOrDefault(binding => binding.TopValues is { Count: 0 } && binding.Populated > 0) is { } textOnly)
        {
            return new IssueItem("warning", UnpopulatedCode, $"{parameter} cannot match anything in this folder: the {textOnly.Populated} {textOnly.ResourceType} resources with a value for {textOnly.Code} have text only, no code (use {textOnly.Code}:text to match the text).", Location: null);
        }

        return null;
    }

    // Built from the request's own arguments only.
    private static NextCall Search(SearchRequest request)
    {
        Dictionary<string, object> args = new() { ["resourceType"] = request.ResourceType };
        AddIf(args, "query", request.Query);
        AddIf(args, "compartment", request.Compartment);
        AddIf(args, "summary", request.Summary);
        AddIf(args, "elements", request.Elements);
        if (request.Count is { } count)
        {
            args["count"] = count;
        }

        if (request.Offset != 0)
        {
            args["offset"] = request.Offset;
        }

        if (request.Lenient)
        {
            args["lenient"] = true;
        }

        return new NextCall("search_fhir_resources", args);
    }

    private static void AddIf(Dictionary<string, object> args, string name, string? value)
    {
        if (value is not null)
        {
            args[name] = value;
        }
    }

    internal static string Literal<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        typeof(TEnum).GetField(value.ToString())?.GetCustomAttribute<EnumLiteralAttribute>()?.Literal
            ?? throw new InvalidOperationException($"{typeof(TEnum).Name}.{value} has no FHIR literal.");
}
