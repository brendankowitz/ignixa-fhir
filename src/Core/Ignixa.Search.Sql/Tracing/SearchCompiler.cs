using System.Globalization;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// The compiler's single orchestration entry point: runs Build, Resolve, Lower, and Emit in sequence and
/// assembles a <see cref="SearchTrace"/> spanning all four stages. Today the tracing test suite is its only
/// caller; it exists so that future production wiring runs the same sequence rather than re-implementing the
/// stage order alongside it.
/// </summary>
public static class SearchCompiler
{
    /// <summary>
    /// Compiles a search end to end and traces every stage. Failures are recorded as data on the
    /// returned trace rather than thrown: an unresolved search parameter always reaches
    /// <see cref="SearchTrace.Failure"/> at <see cref="TraceStage.Resolve"/> directly from
    /// <see cref="ResolvedSymbols.Unresolved"/> — and additionally marks its own parameter outcome when one
    /// of them owns a <see cref="ParameterTrace"/> to mark — and a
    /// <see cref="NotSupportedException"/>/<see cref="KeyNotFoundException"/> from Lower or Emit is
    /// caught at this boundary, recorded on <see cref="SearchTrace.Failure"/>, and attributed to the
    /// parameter the failing dispatcher named.
    /// </summary>
    public static async Task<SearchTrace> CompileAsync(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISearchOptionsBuilder optionsBuilder,
        ISymbolResolver resolver,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(resolver);

        var outcomes = new List<ParameterTrace>();
        var options = optionsBuilder.Build(resourceType, parameters, schemaProvider: null, outcomes);

        var resolved = await Resolve.RunAsync(
            options.Expression,
            options.Include,
            options.RevInclude,
            options.Sort,
            resolver,
            resourceType,
            cancellationToken,
            compartmentDefinitionManager,
            searchParameterDefinitionManager);

        MarkUnresolved(outcomes, resolved.Unresolved);

        QueryPlanTrace? planTrace = null;
        EmittedSqlTrace? sqlTrace = null;

        // MarkUnresolved can only attribute a parameter that owns a ParameterTrace, and the builder raises
        // one for ParameterCategory.Search alone -- an unresolved _include, _revinclude, _sort, or synthesized
        // compartment parameter is attributable to nothing. Recording the failure unconditionally guarantees
        // the absent plan below always has a stated cause, whether or not attribution found an owner.
        var failure = ResolveFailure(resolved.Unresolved);

        // An unresolved parameter guarantees Lower will throw KeyNotFoundException the moment it looks
        // up that parameter's SearchParamId -- skip the call rather than let a second, less specific
        // failure land on top of the Resolve-stage one already recorded above.
        if (resolved.Unresolved.Count == 0)
        {
            try
            {
                var lowered = Lower.Run(
                    options.Expression,
                    resolved.Symbols,
                    resourceType,
                    options.Include,
                    options.RevInclude,
                    includeLimit: 0,
                    options.Sort,
                    SortPhase.Valued,
                    page: null);

                planTrace = BuildPlanTrace(lowered, outcomes);

                var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
                sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.TextRanges ?? []);
            }
            catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
            {
                failure = RecordFailure(outcomes, planTrace is null ? TraceStage.Lower : TraceStage.Emit, ex);
            }
        }

        return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace)
        {
            Failure = failure,
            Implicit = DetectImplicit(parameters, options),
        };
    }

    /// <summary>Names every parameter Resolve could not find in one top-level failure, or null when it found them all.</summary>
    private static TraceFailure? ResolveFailure(IReadOnlyList<SearchParameterInfo> unresolved)
    {
        if (unresolved.Count == 0)
        {
            return null;
        }

        var codes = string.Join(", ", unresolved.Select(p => $"'{p.Code}'"));
        return new TraceFailure(TraceStage.Resolve, $"Search parameters could not be resolved: {codes}.", null);
    }

    /// <summary>
    /// Reports the control values that took effect without the caller sending them, reading each one back
    /// off the resolved <see cref="SearchOptions"/> rather than restating a default — a changed default
    /// then shows up in the trace instead of drifting away from it.
    /// </summary>
    /// <remarks>
    /// Supplied-ness is decided by <see cref="QueryParameter.Category"/>, the same classification the
    /// builder switches on, so a name form the builder would not treat as <c>_count</c> is not treated as
    /// one here either. A resolved value carrying no decision (<see cref="TotalType.None"/>) is skipped: a
    /// chip reading "nothing happened" is noise. <c>_summary</c> is never reported, because the builder
    /// only ever sets it from an explicit <c>_summary</c>, which by definition is not implicit.
    /// </remarks>
    private static IReadOnlyList<ImplicitParameter> DetectImplicit(IReadOnlyList<QueryParameter> parameters, SearchOptions options)
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
            // Relies on the _summary=count promotion being the only way an unsupplied _total becomes
            // non-None. A future server-set Total default must revisit this single reason.
            implicitParameters.Add(new ImplicitParameter("_total", options.Total.ToString(), "implied by _summary=count"));
        }

        return implicitParameters;
    }

    /// <summary>Marks the owning trace Failed(Resolve, ...) for every parameter Resolve could not find, matching against every parameter-bearing node in that trace's IR -- not leaf predicates alone, since a chain's or a :missing's parameter appears nowhere else.</summary>
    private static void MarkUnresolved(IList<ParameterTrace> outcomes, IReadOnlyList<SearchParameterInfo> unresolved)
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

    /// <summary>Builds the plan trace, mapping each CTE origin to its owning parameter by reference identity against every trace's IR subtree. Origins with no owner (:missing, compartment, structural CTEs) keep a null ordinal.</summary>
    private static QueryPlanTrace BuildPlanTrace(LoweredPlan lowered, IReadOnlyList<ParameterTrace> outcomes)
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
                i, directOrdinals[i], spans[i], ContributingOrdinals(i, rows, directOrdinals));
        }

        return new QueryPlanTrace(lowered.Plan.Explain(), ctes, rows);
    }

    /// <summary>
    /// Every parameter ordinal the CTE at <paramref name="index"/> draws from, closed over the CTEs it
    /// composes. A structural CTE has no ordinal of its own, so without this a consumer wanting "which
    /// parameters does this join belong to" has to walk the plan itself.
    /// </summary>
    /// <remarks>
    /// Depth-first over <see cref="PlanExplainRow.ReferencedCteIndexes"/>, which the explainer reads
    /// straight off the plan node. Plan CTE references form a DAG (a CTE may only reference lower indices),
    /// so the walk terminates; <paramref name="visited"/> keeps a diamond from being counted twice.
    /// </remarks>
    private static IReadOnlyList<int> ContributingOrdinals(
        int index,
        IReadOnlyList<PlanExplainRow> rows,
        int?[] directOrdinals)
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

            var row = rows.FirstOrDefault(r => r.CanonicalLabel == SqlLabels.CteLabel(cteIndex));
            foreach (var child in row?.ReferencedCteIndexes ?? [])
            {
                Collect(child);
            }
        }
    }

    /// <summary>
    /// Records a Lower/Emit-stage failure, and attributes it to the owning parameter when the failing
    /// dispatcher named one. Attribution is by parameter, never by span alone: spans repeat across
    /// parameters, so a span-only match would mark same-length neighbours failed too. Guards that throw
    /// from outside the dispatchers name no parameter; their message still reaches the caller through the
    /// returned <see cref="TraceFailure"/>.
    /// </summary>
    private static TraceFailure RecordFailure(IList<ParameterTrace> outcomes, TraceStage stage, Exception ex)
    {
        var span = ex.Data[LeafLoweringDispatcher.SpanDataKey] as SourceSpan?;
        var failure = new TraceFailure(stage, ex.Message, span);

        if (ex.Data[LeafLoweringDispatcher.ParameterDataKey] is not SearchParameterInfo parameter)
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

            outcomes[i] = trace with { Outcome = new ParameterOutcome.Failed(stage, ex.Message, span) };
        }

        return failure;
    }

    /// <summary>
    /// Yields every search parameter one IR node names, with the node's own span where it has one. Covers
    /// the four node kinds that carry a parameter, not just leaf predicates: a chain names its reference
    /// parameter, a wrapper names a composite's own identity, and :missing names its subject -- each of
    /// which Resolve can report unresolved without any leaf predicate mentioning it.
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

    /// <summary>Yields <paramref name="node"/> and every descendant reachable through this parser's container node kinds, so a caller can search a whole IR subtree for a specific node reference without a full visitor.</summary>
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
