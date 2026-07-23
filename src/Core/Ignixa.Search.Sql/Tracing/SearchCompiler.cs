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
    public static Task<SearchTrace> CompileAsync(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISearchOptionsBuilder optionsBuilder,
        ISymbolResolver resolver,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
        CancellationToken cancellationToken = default)
        => CompileWithTimeProviderAsync(resourceType, parameters, optionsBuilder, resolver, compartmentDefinitionManager, searchParameterDefinitionManager, null, cancellationToken);

    /// <summary>
    /// Overload that accepts an explicit <see cref="TimeProvider"/> for deterministic approximation-time
    /// capture. <see cref="TimeProvider.GetUtcNow"/> is called exactly once per compile; when
    /// <paramref name="timeProvider"/> is null, <see cref="TimeProvider.System"/> is used.
    /// </summary>
    public static async Task<SearchTrace> CompileWithTimeProviderAsync(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISearchOptionsBuilder optionsBuilder,
        ISymbolResolver resolver,
        ICompartmentDefinitionManager? compartmentDefinitionManager,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(resolver);

        var approximationReferenceTime = (timeProvider ?? TimeProvider.System).GetUtcNow();

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
            // Tracks whether Lower itself completed. Keying the reported stage off planTrace instead would
            // blame the lowerer for anything BuildPlanTrace throws -- and BuildPlanTrace runs the explainer,
            // whose NotSupportedException means a CteDefinition case is missing from PlanExplainer, not
            // that lowering went wrong. Filing that under Lower sends the next reader to the wrong file.
            LoweredPlan? lowered = null;

            try
            {
                lowered = Lower.Run(
                    options.Expression,
                    resolved.Symbols,
                    resourceType,
                    options.Include,
                    options.RevInclude,
                    includeLimit: 0,
                    options.Sort,
                    SortPhase.Valued,
                    page: null,
                    approximationReferenceTime: approximationReferenceTime);

                planTrace = BuildPlanTrace(lowered, outcomes);
                MarkKnownMisses(outcomes, lowered);

                var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
                sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? []);
            }
            // Deliberately does NOT catch ArgumentException. The trace records' construction guards
            // (SqlTextRange, CteProvenance, PlanExplainRow) throw it, but none can trip on a well-formed
            // plan -- they detect programmer error, so swallowing them into a TraceFailure would file a
            // bug in this compiler as though it were a property of the user's query.
            catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
            {
                failure = RecordFailure(outcomes, lowered is null ? TraceStage.Lower : TraceStage.Emit, ex);
            }
        }

        return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace)
        {
            Failure = failure,
            Implicit = DetectImplicit(parameters, options),
        };
    }

    /// <summary>
    /// Compiles an already-built <see cref="SearchOptions"/> — skipping the Build stage entirely, since the
    /// caller (a production ISearchService implementation receiving a pre-built SearchOptions, not raw query
    /// parameters) has already built it upstream. Runs Resolve, Lower, and Emit only, tracing every stage the
    /// same way <see cref="CompileWithTimeProviderAsync"/> does. Failures are recorded as data on
    /// <see cref="SearchTrace.Failure"/>, never thrown, matching CompileAsync's own contract.
    /// </summary>
    public static async Task<SearchTrace> CompileFromOptionsAsync(
        SearchOptions options,
        string? resourceType,
        ISymbolResolver resolver,
        ICompartmentDefinitionManager? compartmentDefinitionManager,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken = default)
    {
        // resourceType is deliberately NOT null-checked here -- null/empty means a multi-type/system-level
        // search, a real supported case (see this task's Interfaces note), not a caller error.
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);

        var approximationReferenceTime = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var outcomes = new List<ParameterTrace>();

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
        var failure = ResolveFailure(resolved.Unresolved);

        // Declared here, not inside the `if` block below, even though it's only ever assigned inside it --
        // the final `return`'s `CompiledPlan = lowered?.Plan` needs it in scope whether or not that block
        // ran at all (an unresolved parameter skips the block entirely and this stays null, which is
        // correct: no Lower call means no plan to report).
        LoweredPlan? lowered = null;

        if (resolved.Unresolved.Count == 0)
        {
            try
            {
                lowered = Lower.Run(
                    options.Expression,
                    resolved.Symbols,
                    resourceType,
                    options.Include,
                    options.RevInclude,
                    includeLimit: 0,
                    options.Sort,
                    SortPhase.Valued,
                    page: null,
                    systemLevelSearch: string.IsNullOrEmpty(resourceType),
                    approximationReferenceTime: approximationReferenceTime);

                planTrace = BuildPlanTrace(lowered, outcomes);
                MarkKnownMisses(outcomes, lowered);

                var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
                sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? []);
            }
            catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
            {
                failure = RecordFailure(outcomes, lowered is null ? TraceStage.Lower : TraceStage.Emit, ex);
            }
        }

        return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace)
        {
            Failure = failure,
            // A pre-built SearchOptions carries no notion of "was this explicitly supplied" -- the only
            // input DetectImplicit needs that this entry point genuinely lacks. Leaving Implicit at its []
            // default (see SearchTrace) rather than guessing which control values the caller supplied.
            CompiledPlan = lowered?.Plan,
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

    /// <summary>
    /// Marks a parameter <see cref="ParameterOutcome.KnownMiss"/> when its CTE lowered to an unsatisfiable
    /// predicate, so "this query cannot return a row, and here is the value that made it so" is data on the
    /// trace rather than a <c>1 = 0</c> a reader has to spot in the emitted SQL.
    /// </summary>
    /// <remarks>
    /// Only overwrites <see cref="ParameterOutcome.Compiled"/>. A parameter already Failed or Ignored has a
    /// stronger story to tell, and restamping it would replace a cause with a consequence.
    /// </remarks>
    private static void MarkKnownMisses(IList<ParameterTrace> outcomes, LoweredPlan lowered)
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
                i, directOrdinals[i], spans[i], ContributingOrdinals(i, lowered.Plan, directOrdinals));
        }

        // Print off the rows already computed rather than calling Explain(), which would run Describe a
        // second time -- same output, twice the work, and two chances to disagree.
        return new QueryPlanTrace(PlanExplainer.Print(rows), ctes, rows);
    }

    /// <summary>
    /// Every parameter ordinal the CTE at <paramref name="index"/> draws from, closed over the CTEs it
    /// composes. A structural CTE has no ordinal of its own, so without this a consumer wanting "which
    /// parameters does this join belong to" has to walk the plan itself.
    /// </summary>
    /// <remarks>
    /// Reads the child references off <paramref name="plan"/> rather than off the explainer's rows: the
    /// rows are a display projection, and provenance should not depend on how something renders. Plan CTE
    /// references only ever point at lower indices — every structural factory appends itself after its
    /// children — so the walk terminates. The visited set exists for a diamond, where two branches share a
    /// child; ordinals land in a set, so revisiting would be harmless but wasteful.
    /// </remarks>
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
