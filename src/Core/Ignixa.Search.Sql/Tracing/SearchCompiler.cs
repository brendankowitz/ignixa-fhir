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
/// assembles a <see cref="SearchTrace"/> spanning all four stages. Production wiring and the tracing test
/// suite both call this — neither re-implements any stage, so the two can never drift.
/// </summary>
public static class SearchCompiler
{
    /// <summary>
    /// Compiles a search end to end and traces every stage. Failures are recorded as data on the
    /// returned trace rather than thrown: an unresolved search parameter is reported at
    /// <see cref="TraceStage.Resolve"/> directly from <see cref="ResolvedSymbols.Unresolved"/>, and a
    /// <see cref="NotSupportedException"/>/<see cref="KeyNotFoundException"/> from Lower or Emit is
    /// caught at this boundary and attributed to the parameter whose predicate span it names.
    /// </summary>
    public static async Task<SearchTrace> CompileAsync(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISearchOptionsBuilder optionsBuilder,
        ISymbolResolver resolver,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
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
                RecordFailure(outcomes, planTrace is null ? TraceStage.Lower : TraceStage.Emit, ex);
            }
        }

        return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace);
    }

    /// <summary>Marks the owning trace Failed(Resolve, ...) for every parameter Resolve could not find, matching by the exact <see cref="SearchParameterInfo"/> instance appearing on a predicate in that trace's IR.</summary>
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
                .OfType<SearchParameterPredicateExpression>()
                .FirstOrDefault(p => unresolved.Any(u => ReferenceEquals(u, p.Parameter)));
            if (match is null)
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
        var ctes = new CteProvenance[lowered.Plan.Ctes.Count];
        for (var i = 0; i < ctes.Length; i++)
        {
            ctes[i] = new CteProvenance(i, null, null);
        }

        foreach (var origin in lowered.Provenance.Origins)
        {
            var owner = outcomes.FirstOrDefault(t => t.Ir is not null && Flatten(t.Ir).Any(n => ReferenceEquals(n, origin.SourceNode)));
            if (owner is not null)
            {
                ctes[origin.CteIndex] = new CteProvenance(origin.CteIndex, owner.Ordinal, ExtractSpan(origin.SourceNode));
            }
        }

        return new QueryPlanTrace(lowered.Plan.Explain(), ctes);
    }

    /// <summary>Attributes a Lower/Emit-stage failure to every parameter whose IR carries the span the failing dispatcher attached to the exception. Leaves outcomes untouched when no span was attached or none matches -- the null Plan/Sql on the returned trace is itself the visible signal in that case.</summary>
    private static void RecordFailure(IList<ParameterTrace> outcomes, TraceStage stage, Exception ex)
    {
        if (ex.Data[LeafLoweringDispatcher.SpanDataKey] is not SourceSpan span)
        {
            return;
        }

        for (var i = 0; i < outcomes.Count; i++)
        {
            var trace = outcomes[i];
            if (trace.Ir is null || !Flatten(trace.Ir).Any(n => ExtractSpan(n) == span))
            {
                continue;
            }

            outcomes[i] = trace with { Outcome = new ParameterOutcome.Failed(stage, ex.Message, span) };
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
