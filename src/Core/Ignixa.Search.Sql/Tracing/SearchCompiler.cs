using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
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
        Expression? operationExpression = null,
        CancellationToken cancellationToken = default)
        => CompileWithTimeProviderAsync(resourceType, parameters, optionsBuilder, resolver, compartmentDefinitionManager, searchParameterDefinitionManager, null, operationExpression, cancellationToken);

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
        Expression? operationExpression = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(resolver);

        var approximationReferenceTime = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var outcomes = new List<ParameterTrace>();
        var options = optionsBuilder.Build(resourceType, parameters, schemaProvider: null, outcomes);

        // A FHIR operation such as Patient/$everything is not expressible as a query-string search: its
        // root is a PatientEverythingExpression the builder never produces. When the caller supplies that
        // operation expression, it replaces the search expression the builder derived from the query string
        // so Resolve and Lower run against the real operation, not a bare-resource fallback.
        if (operationExpression is not null)
        {
            options.Expression = operationExpression;
        }

        var context = CompilationContext.Create(
            options,
            resourceType,
            new SearchPlanOptions { OperationExpression = operationExpression },
            approximationReferenceTime);

        var resolved = await Resolve.RunAsync(
            context,
            new SymbolResolution(resolver, compartmentDefinitionManager, searchParameterDefinitionManager),
            cancellationToken);

        CompilationDiagnosticsBuilder.MarkUnresolved(outcomes, resolved.Unresolved);

        QueryPlanTrace? planTrace = null;
        EmittedSqlTrace? sqlTrace = null;

        // MarkUnresolved can only attribute a parameter that owns a ParameterTrace, and the builder raises
        // one for ParameterCategory.Search alone -- an unresolved _include, _revinclude, _sort, or synthesized
        // compartment parameter is attributable to nothing. Recording the failure unconditionally guarantees
        // the absent plan below always has a stated cause, whether or not attribution found an owner.
        var resolveFailure = CompilationDiagnosticsBuilder.ResolveFailure(resolved.Unresolved);
        TraceFailure? failure = resolveFailure is null
            ? null
            : new TraceFailure(ToTraceStage(resolveFailure.Stage), resolveFailure.Message, resolveFailure.Span);

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
                lowered = Lower.Run(context, resolved.Symbols);

                planTrace = CompilationDiagnosticsBuilder.BuildPlanTrace(lowered, outcomes);
                CompilationDiagnosticsBuilder.MarkKnownMisses(outcomes, lowered);

                var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
                sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? []);
            }
            // Deliberately does NOT catch ArgumentException. The trace records' construction guards
            // (SqlTextRange, CteProvenance, PlanExplainRow) throw it, but none can trip on a well-formed
            // plan -- they detect programmer error, so swallowing them into a TraceFailure would file a
            // bug in this compiler as though it were a property of the user's query.
            catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
            {
                var compilationFailure = CompilationDiagnosticsBuilder.RecordFailure(outcomes, lowered is null ? CompilationStage.Lower : CompilationStage.Emit, ex);
                failure = new TraceFailure(ToTraceStage(compilationFailure.Stage), compilationFailure.Message, compilationFailure.Span);
            }
        }

        return new SearchTrace(resourceType, outcomes, planTrace, sqlTrace)
        {
            Failure = failure,
            Implicit = CompilationDiagnosticsBuilder.DetectImplicit(parameters, options),
        };
    }

    /// <summary>
    /// Compiles an already-built <see cref="SearchOptions"/> — skipping the Build stage entirely, since the
    /// caller (a production ISearchService implementation receiving a pre-built SearchOptions, not raw query
    /// parameters) has already built it upstream. Runs Resolve, Lower, and Emit only, tracing every stage the
    /// same way <see cref="CompileWithTimeProviderAsync"/> does. Failures are recorded as data on
    /// <see cref="SearchTrace.Failure"/>, never thrown, matching CompileAsync's own contract.
    /// </summary>
    /// <remarks>
    /// This method is the only place a <c>LowerOptions</c> gets built from a caller-supplied
    /// <see cref="SearchOptions"/>. Three properties have, one at a time, been added to
    /// <see cref="SearchOptions"/>, accepted here, and never forwarded — each one a control that looked
    /// live and silently did nothing (<see cref="AccessConstraints"/>, then <see cref="ResourceTypes"/>,
    /// then <see cref="ResourceVersionTypes"/>). The table below is the contract every future
    /// <c>LowerOptions</c> property must be checked against, so the next one is caught here instead
    /// of by a fourth review. One row per <c>LowerOptions</c> property:
    /// <list type="table">
    /// <listheader><term>LowerOptions property</term><description>Source in this method, or why not</description></listheader>
    /// <item><term><c>LowerOptions.CountOnly</c></term><description>The <c>countOnly</c> method parameter, not a <see cref="SearchOptions"/> property. Count-mode is an execution-shape control the caller states directly, the same as <c>includeLimit</c>/<c>sortPhase</c>/<c>countPhaseScoped</c> below.</description></item>
    /// <item><term><c>LowerOptions.Top</c></term><description>Not set (always null on this path). No <see cref="SearchOptions"/> mapping exists, and none should be added naively: <see cref="SearchOptions.MaxItemCount"/> is not a safe 1:1 source because real callers already transform it before a search runs (e.g. <c>SearchResourcesHandler</c> requests <c>MaxItemCount + 1</c> to detect "has more"), so forwarding it here would silently fight that transformation. Row-capping on this path is done via <c>LowerOptions.OffsetPage</c> or keyset paging (the <c>page</c> parameter to <see cref="Lower.Run"/>, always null here today), both mutually exclusive with <c>LowerOptions.Top</c>. Flagged as a real gap in the row-capping story, not fixed here: nothing calls this method wanting <c>Top</c> driven from <see cref="SearchOptions"/> today.</description></item>
    /// <item><term><c>LowerOptions.ApproximationReferenceTime</c></term><description>The <c>timeProvider</c> method parameter. Not a <see cref="SearchOptions"/> concept — an <c>:ap</c> comparator needs a clock, not caller intent.</description></item>
    /// <item><term><c>LowerOptions.Visibility</c></term><description><see cref="SearchOptions.ResourceVersionTypes"/>, mapped through a local <c>ToVisibility</c> helper (<see cref="ResourceVersionTypes.None"/> throws <see cref="NotSupportedException"/>; <see cref="ResourceVersionTypes.Latest"/> alone maps to null, which <see cref="QueryPlan.EffectiveVisibility"/> already treats as <see cref="ResourceVisibility.Current"/>). The third instance of this defect class, fixed alongside this table: was accepted by <see cref="SearchOptions"/>, never reached Lower, so a caller asking for history or soft-deleted rows got silent Latest-only results.</description></item>
    /// <item><term><c>LowerOptions.SurrogateRange</c></term><description>The <c>surrogateIdRange</c> method parameter when the caller supplies one (the explicit path an export partition worker uses today), falling back to <see cref="SearchOptions.StartSurrogateId"/>/<see cref="SearchOptions.EndSurrogateId"/> when it is null — those two properties ARE populated and read elsewhere in this repository (<c>ExportWorkerActivity</c> sets them; <c>FileBasedSearchService</c> and <c>SqlEntityFrameworkSearchService</c> read them), so leaving them unforwarded here was the fourth instance of this defect class. A half-open pair (only one of the two set) throws <see cref="NotSupportedException"/> rather than silently scanning unbounded in one direction. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingOnlySurrogateBounds_...</c>.</description></item>
    /// <item><term><c>LowerOptions.SearchParameterHash</c></term><description>Not set. No corresponding <see cref="SearchOptions"/> property exists. Reindex gating (confirming a resource was indexed against an expected search-parameter hash) is a distinct caller concern, not a search request — there is nothing on <see cref="SearchOptions"/> to forward.</description></item>
    /// <item><term><c>LowerOptions.ResourceTypes</c></term><description><see cref="SearchOptions.ResourceTypes"/>. Forwarded; without it a multi-<c>_type</c> system-level search silently returns every type. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingResourceTypes_...</c>.</description></item>
    /// <item><term><c>LowerOptions.AccessConstraints</c></term><description><see cref="SearchOptions.AccessConstraints"/>. Forwarded; without it an authorization constraint is accepted but never enforced. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingAccessConstraints_...</c>.</description></item>
    /// <item><term><c>LowerOptions.IncludesOnly</c></term><description>Not set. No corresponding <see cref="SearchOptions"/> property exists. The <c>$includes</c> operation does not call this compiler today — <c>IncludesResourceHandler</c> re-executes the full search through a different abstraction and filters Include entries out client-side — so nothing in the Application layer currently expresses an "includes only" intent for this method to carry.</description></item>
    /// <item><term><c>LowerOptions.SystemLevelSearch</c></term><description>Derived as <c>resourceType is null</c> from this method's own <c>resourceType</c> parameter, deliberately not from <see cref="SearchOptions.ResourceType"/> — the parameter exists precisely so Resolve, Lower, this flag, and the returned <see cref="SearchTrace.ResourceType"/> all observe one normalized value (see that parameter's own remarks above).</description></item>
    /// <item><term><c>LowerOptions.OffsetPage</c></term><description>The <c>offsetPage</c> method parameter. Not a <see cref="SearchOptions"/> property: constructing it requires decoding a legacy <see cref="SearchOptions.ContinuationToken"/> and driving a two-phase retry loop, which is adapter logic living in a different layer — a real architectural boundary, not an omission.</description></item>
    /// <item><term><c>LowerOptions.CountPhaseScoped</c></term><description>The <c>countPhaseScoped</c> method parameter, the same class as <c>LowerOptions.OffsetPage</c> immediately above — the compiler-side half of two-phase sort execution, orchestrated by the caller.</description></item>
    /// </list>
    /// </remarks>
    public static async Task<SearchTrace> CompileFromOptionsAsync(
        SearchOptions options,
        string? resourceType,
        ISymbolResolver resolver,
        ICompartmentDefinitionManager? compartmentDefinitionManager,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager,
        TimeProvider? timeProvider,
        OffsetSpec? offsetPage = null,
        (long Start, long End)? surrogateIdRange = null,
        bool countOnly = false,
        int includeLimit = 0,
        bool countPhaseScoped = false,
        SortPhase sortPhase = SortPhase.Valued,
        CancellationToken cancellationToken = default)
    {
        // resourceType is deliberately NOT null-checked here -- null/empty means a multi-type/system-level
        // search, a real supported case (see this task's Interfaces note), not a caller error. Normalized
        // to null ONCE, here, before any downstream use -- Resolve.RunAsync, Lower.Run, systemLevelSearch,
        // and the final SearchTrace.ResourceType must all observe the exact same value, or an empty string
        // would read as system-level to one and as a literal (unmatchable) resource type to the others.
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);

        resourceType = string.IsNullOrEmpty(resourceType) ? null : resourceType;

        var approximationReferenceTime = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var outcomes = new List<ParameterTrace>();

        // Deliberately constructed rather than built by CompilationContext.Create: Create maps
        // ResourceVersionTypes and the surrogate bounds eagerly, and both mappings can throw
        // NotSupportedException. Today those throws happen at the ToVisibility/ToSurrogateRange calls
        // in the try below, whose catch turns them into a recorded failure rather than an escaped
        // exception. Calling Create here would move them outside that try and change what a caller
        // observes.
        // Visibility and SurrogateRange are therefore left null here: Resolve reads neither. The real
        // values are computed inside the try below and layered onto this same context with a `with`
        // expression, so both stages share one context rather than constructing a second one.
        var context = new CompilationContext
        {
            Expression = options.Expression,
            TargetResourceType = string.IsNullOrEmpty(resourceType) ? null : resourceType,
            Includes = options.Include ?? [],
            RevIncludes = options.RevInclude ?? [],
            Sort = options.Sort ?? [],
            AccessConstraints = options.AccessConstraints ?? [],
            ResourceTypes = options.ResourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime,
            Visibility = null,
            SurrogateRange = null,
            Options = new SearchPlanOptions
            {
                CountOnly = countOnly,
                IncludeLimit = includeLimit,
                SortPhase = sortPhase,
                CountPhaseScoped = countPhaseScoped,
                OffsetPage = offsetPage,
                SurrogateRange = surrogateIdRange,
            },
        };

        var resolved = await Resolve.RunAsync(
            context,
            new SymbolResolution(resolver, compartmentDefinitionManager, searchParameterDefinitionManager),
            cancellationToken);

        CompilationDiagnosticsBuilder.MarkUnresolved(outcomes, resolved.Unresolved);

        QueryPlanTrace? planTrace = null;
        EmittedSqlTrace? sqlTrace = null;
        var resolveFailure = CompilationDiagnosticsBuilder.ResolveFailure(resolved.Unresolved);
        TraceFailure? failure = resolveFailure is null
            ? null
            : new TraceFailure(ToTraceStage(resolveFailure.Stage), resolveFailure.Message, resolveFailure.Span);

        // Declared here, not inside the `if` block below, even though it's only ever assigned inside it --
        // the final `return`'s `CompiledPlan = lowered?.Plan` needs it in scope whether or not that block
        // ran at all (an unresolved parameter skips the block entirely and this stays null, which is
        // correct: no Lower call means no plan to report).
        LoweredPlan? lowered = null;

        if (resolved.Unresolved.Count == 0)
        {
            try
            {
                var loweringContext = context with
                {
                    Visibility = ToVisibility(options.ResourceVersionTypes),
                    SurrogateRange = ToSurrogateRange(surrogateIdRange, options),
                };

                lowered = Lower.Run(loweringContext, resolved.Symbols);

                planTrace = CompilationDiagnosticsBuilder.BuildPlanTrace(lowered, outcomes);
                CompilationDiagnosticsBuilder.MarkKnownMisses(outcomes, lowered);

                var emitted = SqlBuilder.Run(lowered.Plan, new EmitOptions(IncludeTextRanges: true));
                sqlTrace = new EmittedSqlTrace(emitted.Sql, emitted.Parameters, emitted.TextRanges ?? []);
            }
            catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
            {
                var compilationFailure = CompilationDiagnosticsBuilder.RecordFailure(outcomes, lowered is null ? CompilationStage.Lower : CompilationStage.Emit, ex);
                failure = new TraceFailure(ToTraceStage(compilationFailure.Stage), compilationFailure.Message, compilationFailure.Span);
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

    /// <summary>
    /// Maps <see cref="SearchOptions.ResourceVersionTypes"/> onto the SQL compiler's own
    /// <see cref="ResourceVisibility"/>, per the mapping <see cref="ResourceVersionTypes"/>'s own remarks
    /// document. <see cref="ResourceVersionTypes.Latest"/> alone returns null rather than an explicit
    /// <see cref="ResourceVisibility.Current"/> -- both leave <see cref="QueryPlan.EffectiveVisibility"/>
    /// (which falls back to <see cref="ResourceVisibility.Current"/> on null) at the same value, so null
    /// is the smaller diff against the plan this compiler emitted before this forwarding existed.
    /// </summary>
    private static ResourceVisibility? ToVisibility(ResourceVersionTypes types) => types switch
    {
        // Not a valid search input by the enum's own doc -- treating it as Latest would silently reproduce
        // the exact fail-open-by-omission shape this method exists to close, only one layer further in.
        ResourceVersionTypes.None => throw new NotSupportedException(
            "SearchOptions.ResourceVersionTypes.None is not a valid search input; a search must select at least Latest."),
        ResourceVersionTypes.Latest => null,
        _ => new ResourceVisibility(
            IncludeHistory: types.HasFlag(ResourceVersionTypes.History),
            IncludeDeleted: types.HasFlag(ResourceVersionTypes.SoftDeleted)),
    };

    private static TraceStage ToTraceStage(CompilationStage stage) => stage switch
    {
        CompilationStage.Build => TraceStage.Parse,
        CompilationStage.Resolve => TraceStage.Resolve,
        CompilationStage.Lower => TraceStage.Lower,
        CompilationStage.Emit => TraceStage.Emit,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    /// <summary>
    /// Resolves the surrogate-id bound this compile should apply: the explicit <paramref name="explicitRange"/>
    /// method parameter when the caller supplied one, otherwise a fallback onto
    /// <see cref="SearchOptions.StartSurrogateId"/>/<see cref="SearchOptions.EndSurrogateId"/> -- the fourth
    /// instance of the same defect class as <see cref="AccessConstraints"/>, <see cref="ResourceTypes"/>, and
    /// <see cref="ResourceVersionTypes"/>: a <see cref="SearchOptions"/> property that looked live and reached
    /// nothing. The explicit parameter wins when both are supplied, matching the adapter-parameter boundary
    /// <c>LowerOptions.OffsetPage</c> uses.
    /// </summary>
    private static SurrogateIdRange? ToSurrogateRange((long Start, long End)? explicitRange, SearchOptions options)
    {
        if (explicitRange is { } range)
        {
            return new SurrogateIdRange(new SqlParameterRef(range.Start), new SqlParameterRef(range.End));
        }

        return (options.StartSurrogateId, options.EndSurrogateId) switch
        {
            (null, null) => null,
            ({ } start, { } end) => new SurrogateIdRange(new SqlParameterRef(start), new SqlParameterRef(end)),
            // A half-open range is a caller error, not a partial intent to honour -- silently treating one
            // bound as unset would scan an unbounded direction, the same fail-open shape this method exists
            // to close. Matches ToVisibility's NotSupportedException for ResourceVersionTypes.None below.
            _ => throw new NotSupportedException(
                "SearchOptions.StartSurrogateId and EndSurrogateId must both be set or both be null."),
        };
    }
}
