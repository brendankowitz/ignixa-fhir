using System.Globalization;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The attribution and provenance logic behind <see cref="SearchCompilationDiagnostics"/>: which parameter
/// owns which CTE, which parameter a lowering failure belongs to, and which control values took effect
/// without the caller sending them.
/// </summary>
internal static class CompilationDiagnosticsBuilder
{
    /// <summary>Names every parameter Resolve could not find in one top-level failure, or null when it found them all.</summary>
    public static SearchCompilationFailure? ResolveFailure(IReadOnlyList<SearchParameterInfo> unresolved)
    {
        if (unresolved.Count == 0)
        {
            return null;
        }

        var codes = string.Join(", ", unresolved.Select(p => $"'{p.Code}'"));
        return new SearchCompilationFailure(
            CompilationStage.Resolve,
            $"Search parameters could not be resolved: {codes}.",
            unresolved.Count == 1 ? unresolved[0].Code : null,
            Span: null,
            Exception: null);
    }

    /// <summary>
    /// Reports control values that took effect without the caller sending them, read back off the resolved
    /// <see cref="SearchOptions"/> so a changed default shows in the trace. Supplied-ness uses
    /// <see cref="QueryParameter.Category"/>; a <see cref="TotalType.None"/> value is skipped and <c>_summary</c>
    /// is never reported (it is only ever set explicitly).
    /// </summary>
    public static IReadOnlyList<ImplicitParameter> DetectImplicit(IReadOnlyList<QueryParameter> parameters, SearchOptions options)
    {
        const string ServerDefault = "server default";

        var supplied = parameters.Select(p => p.Category).ToHashSet();
        var implicitParameters = new List<ImplicitParameter>();

        if (!supplied.Contains(ParameterCategory.Count))
        {
            implicitParameters.Add(new ImplicitParameter(
                "_count",
                options.MaxItemCount.ToString(CultureInfo.InvariantCulture),
                ServerDefault));
        }

        if (!supplied.Contains(ParameterCategory.Total) && options.Total != TotalType.None)
        {
            // Relies on _summary=count promotion being the only way an unsupplied _total becomes non-None.
            // A future server-set Total default must revisit this.
            implicitParameters.Add(new ImplicitParameter("_total", options.Total.ToString(), "implied by _summary=count"));
        }

        return implicitParameters;
    }

    /// <summary>Marks the owning trace Failed(Resolve, ...) for every unresolved parameter, matching against
    /// every parameter-bearing node in its IR — not leaf predicates alone, since a chain's or :missing's
    /// parameter appears nowhere else.</summary>
    public static void MarkUnresolved(IList<ParameterTrace> outcomes, IReadOnlyList<SearchParameterInfo> unresolved)
    {
        if (unresolved.Count == 0)
        {
            return;
        }

        for (var i = 0; i < outcomes.Count; i++)
        {
            var trace = outcomes[i];
            if (trace.Ir is null)
            {
                continue;
            }

            var match = Flatten(trace.Ir)
                .SelectMany(ParametersOf)
                .FirstOrDefault(p => unresolved.Any(u => u.Equals(p.Parameter)));
            if (match.Parameter is null)
            {
                continue;
            }

            outcomes[i] = trace with
            {
                Outcome = new ParameterOutcome.Failed(
                    TraceStage.Resolve,
                    $"Search parameter '{match.Parameter.Code}' could not be resolved.",
                    match.Span),
            };
        }
    }

    /// <summary>
    /// Marks a parameter <see cref="ParameterOutcome.KnownMiss"/> when its CTE lowered to an unsatisfiable
    /// predicate, so "cannot return a row, and here's why" is trace data rather than a <c>1 = 0</c> in the SQL.
    /// Only overwrites <see cref="ParameterOutcome.Compiled"/> — a Failed/Ignored parameter has a stronger story.
    /// </summary>
    public static void MarkKnownMisses(IList<ParameterTrace> outcomes, LoweredPlan lowered)
    {
        foreach (var origin in lowered.Provenance.Origins)
        {
            if (FindFalse(PredicateOf(lowered.Plan.Ctes[origin.CteIndex])) is not { } miss)
            {
                continue;
            }

            for (var i = 0; i < outcomes.Count; i++)
            {
                var trace = outcomes[i];
                if (trace.Outcome is not ParameterOutcome.Compiled ||
                    trace.Ir is null ||
                    !Flatten(trace.Ir).Any(n => ReferenceEquals(n, origin.SourceNode)))
                {
                    continue;
                }

                outcomes[i] = trace with
                {
                    Outcome = new ParameterOutcome.KnownMiss(
                        miss.Reason ?? "The parameter lowered to a predicate that can never match.",
                        ExtractSpan(origin.SourceNode)),
                };
            }
        }
    }

    /// <summary>The predicate a CTE definition filters on, or null for the definitions that compose other CTEs rather than filter a table.</summary>
    private static Predicate? PredicateOf(CteDefinition definition) => definition switch
    {
        CteDefinition.ParamSource source => source.Predicate,
        CteDefinition.ResourceSource source => source.Predicate,
        CteDefinition.CompartmentSource source => source.Predicate,
        _ => null,
    };

    /// <summary>
    /// The unsatisfiable term that makes a whole predicate tree unsatisfiable, or null when the tree can
    /// still hold. An <c>And</c> falls to either side being false; an <c>Or</c> needs both.
    /// </summary>
    private static Predicate.False? FindFalse(Predicate? predicate) => predicate switch
    {
        Predicate.False unsatisfiable => unsatisfiable,
        Predicate.And and => FindFalse(and.Left) ?? FindFalse(and.Right),
        Predicate.Or or => FindFalse(or.Left) is { } left && FindFalse(or.Right) is not null ? left : null,
        _ => null,
    };

    /// <summary>
    /// Builds the plan trace, mapping each CTE origin to its owning parameter by reference identity against
    /// every trace's IR. Origins with no owner (compartment, structural CTEs) keep a null ordinal.
    /// </summary>
    public static QueryPlanTrace BuildPlanTrace(LoweredPlan lowered, IReadOnlyList<ParameterTrace> outcomes)
    {
        var rows = PlanExplainer.Describe(lowered.Plan);
        var directOrdinals = new int?[lowered.Plan.Ctes.Count];
        var spans = new SourceSpan?[lowered.Plan.Ctes.Count];

        foreach (var origin in lowered.Provenance.Origins)
        {
            var owner = outcomes.FirstOrDefault(t => t.Ir is not null && Flatten(t.Ir).Any(n => ReferenceEquals(n, origin.SourceNode)));
            if (owner is not null)
            {
                directOrdinals[origin.CteIndex] = owner.Ordinal;
                spans[origin.CteIndex] = ExtractSpan(origin.SourceNode);
            }
        }

        var ctes = new CteProvenance[lowered.Plan.Ctes.Count];
        for (var i = 0; i < ctes.Length; i++)
        {
            ctes[i] = new CteProvenance(
                i, directOrdinals[i], spans[i], ContributingOrdinals(i, lowered.Plan, directOrdinals));
        }

        // Print the rows already computed rather than calling Explain(), which would run Describe twice.
        return new QueryPlanTrace(PlanExplainer.Print(rows), ctes, rows);
    }

    /// <summary>
    /// Every parameter ordinal the CTE at <paramref name="index"/> draws from, closed over the CTEs it
    /// composes. Reads child references off <paramref name="plan"/> (not the display rows); the walk
    /// terminates because CTE references only point at lower indices, and the visited set handles diamonds.
    /// </summary>
    private static IReadOnlyList<int> ContributingOrdinals(int index, QueryPlan plan, int?[] directOrdinals)
    {
        var ordinals = new SortedSet<int>();
        var visited = new HashSet<int>();
        Collect(index);
        return [.. ordinals];

        void Collect(int cteIndex)
        {
            if (!visited.Add(cteIndex))
            {
                return;
            }

            if (directOrdinals[cteIndex] is { } ordinal)
            {
                ordinals.Add(ordinal);
            }

            foreach (var child in PlanExplainer.ReferencedCteIndexesOf(plan.Ctes[cteIndex]))
            {
                Collect(child);
            }
        }
    }

    /// <summary>
    /// Records a Lower/Emit-stage failure and attributes it to the owning parameter when the dispatcher named
    /// one. Attribution is by parameter, never span alone — spans repeat, so a span match would mark
    /// neighbours failed. Guards throwing from outside a dispatcher name no parameter but still reach the caller.
    /// </summary>
    public static SearchCompilationFailure RecordFailure(IList<ParameterTrace> outcomes, CompilationStage stage, Exception ex)
    {
        var span = ex.Data[LeafLoweringDispatcher.SpanDataKey] as SourceSpan?;
        var parameter = ex.Data[LeafLoweringDispatcher.ParameterDataKey] as SearchParameterInfo;
        var failure = new SearchCompilationFailure(stage, ex.Message, parameter?.Code, span, ex);

        if (parameter is null)
        {
            return failure;
        }

        for (var i = 0; i < outcomes.Count; i++)
        {
            var trace = outcomes[i];
            if (trace.Ir is null || !Flatten(trace.Ir).SelectMany(ParametersOf).Any(p => p.Parameter.Equals(parameter)))
            {
                continue;
            }

            outcomes[i] = trace with { Outcome = new ParameterOutcome.Failed(ToTraceStage(stage), ex.Message, span) };
        }

        return failure;
    }

    private static TraceStage ToTraceStage(CompilationStage stage) => stage switch
    {
        CompilationStage.Build => TraceStage.Parse,
        CompilationStage.Resolve => TraceStage.Resolve,
        CompilationStage.Lower => TraceStage.Lower,
        CompilationStage.Emit => TraceStage.Emit,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>
    /// Yields every search parameter one IR node names, with its span where it has one. Covers the four node
    /// kinds beyond leaf predicates (chain, composite wrapper, :missing) that Resolve can report unresolved
    /// without any leaf mentioning them.
    /// </summary>
    private static IEnumerable<(SearchParameterInfo Parameter, SourceSpan? Span)> ParametersOf(Expression node)
    {
        switch (node)
        {
            case SearchParameterPredicateExpression p:
                yield return (p.Parameter, p.Span);
                break;
            case CompositeComponentExpression c:
                yield return (c.ComponentSearchParameter, c.Span);
                break;
            case SearchParameterExpression sp:
                yield return (sp.Parameter, null);
                break;
            case ChainedExpression chain:
                yield return (chain.ReferenceSearchParameter, null);
                break;
            case MissingSearchParameterExpression missing:
                yield return (missing.Parameter, null);
                break;
        }
    }

    /// <summary>Returns a leaf/composite node's own span, or null for any other node kind.</summary>
    private static SourceSpan? ExtractSpan(Expression node) => node switch
    {
        SearchParameterPredicateExpression p => p.Span,
        CompositeComponentExpression c => c.Span,
        _ => null,
    };

    /// <summary>
    /// Yields <paramref name="node"/> and every descendant reachable through this parser's container node
    /// kinds, so a caller can search an IR subtree for a node reference without a full visitor.
    /// </summary>
    private static IEnumerable<Expression> Flatten(Expression node)
    {
        yield return node;

        IReadOnlyList<Expression> children = node switch
        {
            MultiaryExpression m => m.Expressions,
            UnionExpression u => u.Expressions,
            NotExpression n => [n.Expression],
            SearchParameterExpression sp => [sp.Expression],
            ChainedExpression c => [c.Expression],
            CompositeComponentExpression cc => [cc.WrappedExpression],
            _ => [],
        };

        foreach (var child in children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
