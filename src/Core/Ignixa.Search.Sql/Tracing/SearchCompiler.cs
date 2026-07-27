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

        var resolved = await Resolve.RunAsync(
            options.Expression,
            options.Include,
            options.RevInclude,
            options.Sort,
            resolver,
            resourceType,
            cancellationToken,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            // Kept in step with the AccessConstraints forwarded to LowerOptions below: this entry point
            // lowers the same constraint predicates, so it needs their symbols resolved too.
            accessConstraints: options.AccessConstraints,
            // Likewise kept in step with LowerOptions.AllowedResourceTypes below: Lower's allow-list
            // enforcement needs each permitted type's id, so its names must resolve here (an unknown one
            // keeps the unmatchable sentinel rather than being dropped).
            allowedResourceTypes: options.AllowedResourceTypes);

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
                    new LowerOptions
                    {
                        ApproximationReferenceTime = approximationReferenceTime,
                        AccessConstraints = options.AccessConstraints,
                        AllowedResourceTypes = options.AllowedResourceTypes,
                    });

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
    /// <remarks>
    /// This method is the only place a <see cref="LowerOptions"/> gets built from a caller-supplied
    /// <see cref="SearchOptions"/>. Three properties have, one at a time, been added to
    /// <see cref="SearchOptions"/>, accepted here, and never forwarded — each one a control that looked
    /// live and silently did nothing (<see cref="AccessConstraints"/>, then <see cref="ResourceTypes"/>,
    /// then <see cref="ResourceVersionTypes"/>). The table below is the contract every future
    /// <see cref="LowerOptions"/> property must be checked against, so the next one is caught here instead
    /// of by a fourth review. One row per <see cref="LowerOptions"/> property:
    /// <list type="table">
    /// <listheader><term>LowerOptions property</term><description>Source in this method, or why not</description></listheader>
    /// <item><term><see cref="LowerOptions.CountOnly"/></term><description>The <c>countOnly</c> method parameter, not a <see cref="SearchOptions"/> property. Count-mode is an execution-shape control the caller states directly, the same as <c>includeLimit</c>/<c>sortPhase</c>/<c>countPhaseScoped</c> below.</description></item>
    /// <item><term><see cref="LowerOptions.Top"/></term><description>Not set (always null on this path). No <see cref="SearchOptions"/> mapping exists, and none should be added naively: <see cref="SearchOptions.MaxItemCount"/> is not a safe 1:1 source because real callers already transform it before a search runs (e.g. <c>SearchResourcesHandler</c> requests <c>MaxItemCount + 1</c> to detect "has more"), so forwarding it here would silently fight that transformation. Row-capping on this path is done via <see cref="LowerOptions.OffsetPage"/> or keyset paging (the <c>page</c> parameter to <see cref="Lower.Run"/>, always null here today), both mutually exclusive with <see cref="LowerOptions.Top"/>. Flagged as a real gap in the row-capping story, not fixed here: nothing calls this method wanting <c>Top</c> driven from <see cref="SearchOptions"/> today.</description></item>
    /// <item><term><see cref="LowerOptions.ApproximationReferenceTime"/></term><description>The <c>timeProvider</c> method parameter. Not a <see cref="SearchOptions"/> concept — an <c>:ap</c> comparator needs a clock, not caller intent.</description></item>
    /// <item><term><see cref="LowerOptions.Visibility"/></term><description><see cref="SearchOptions.ResourceVersionTypes"/>, mapped through a local <c>ToVisibility</c> helper (<see cref="ResourceVersionTypes.None"/> throws <see cref="NotSupportedException"/>; <see cref="ResourceVersionTypes.Latest"/> alone maps to null, which <see cref="QueryPlan.EffectiveVisibility"/> already treats as <see cref="ResourceVisibility.Current"/>). The third instance of this defect class, fixed alongside this table: was accepted by <see cref="SearchOptions"/>, never reached Lower, so a caller asking for history or soft-deleted rows got silent Latest-only results.</description></item>
    /// <item><term><see cref="LowerOptions.SurrogateRange"/></term><description>The <c>surrogateIdRange</c> method parameter when the caller supplies one (the explicit path an export partition worker uses today), falling back to <see cref="SearchOptions.StartSurrogateId"/>/<see cref="SearchOptions.EndSurrogateId"/> when it is null — those two properties ARE populated and read elsewhere in this repository (<c>ExportWorkerActivity</c> sets them; <c>FileBasedSearchService</c> and <c>SqlEntityFrameworkSearchService</c> read them), so leaving them unforwarded here was the fourth instance of this defect class. A half-open pair (only one of the two set) throws <see cref="NotSupportedException"/> rather than silently scanning unbounded in one direction. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingOnlySurrogateBounds_...</c>.</description></item>
    /// <item><term><see cref="LowerOptions.SearchParameterHash"/></term><description>Not set. No corresponding <see cref="SearchOptions"/> property exists. Reindex gating (confirming a resource was indexed against an expected search-parameter hash) is a distinct caller concern, not a search request — there is nothing on <see cref="SearchOptions"/> to forward.</description></item>
    /// <item><term><see cref="LowerOptions.ResourceTypes"/></term><description><see cref="SearchOptions.ResourceTypes"/>. Forwarded; without it a multi-<c>_type</c> system-level search silently returns every type. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingResourceTypes_...</c>.</description></item>
    /// <item><term><see cref="LowerOptions.AccessConstraints"/></term><description><see cref="SearchOptions.AccessConstraints"/>. Forwarded; without it an authorization constraint is accepted but never enforced. See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingAccessConstraints_...</c>.</description></item>
    /// <item><term><see cref="LowerOptions.AllowedResourceTypes"/></term><description><see cref="SearchOptions.AllowedResourceTypes"/>. Forwarded; without it the SMART clinical-scope allow-list is accepted but never enforced, so the match set is ungated and an <c>_include</c> could return a resource type the scope never granted -- a fail-open authorization bypass. Distinct from <see cref="LowerOptions.ResourceTypes"/> (caller intent) and enforced structurally alongside it (their intersection is the effective base set). See <c>CompileFromOptionsTests.GivenSearchOptionsCarryingAllowedResourceTypes_...</c>.</description></item>
    /// <item><term><see cref="LowerOptions.IncludesOnly"/></term><description>Not set. No corresponding <see cref="SearchOptions"/> property exists. The <c>$includes</c> operation does not call this compiler today — <c>IncludesResourceHandler</c> re-executes the full search through a different abstraction and filters Include entries out client-side — so nothing in the Application layer currently expresses an "includes only" intent for this method to carry.</description></item>
    /// <item><term><see cref="LowerOptions.SystemLevelSearch"/></term><description>Derived as <c>resourceType is null</c> from this method's own <c>resourceType</c> parameter, deliberately not from <see cref="SearchOptions.ResourceType"/> — the parameter exists precisely so Resolve, Lower, this flag, and the returned <see cref="SearchTrace.ResourceType"/> all observe one normalized value (see that parameter's own remarks above).</description></item>
    /// <item><term><see cref="LowerOptions.OffsetPage"/></term><description>The <c>offsetPage</c> method parameter. Not a <see cref="SearchOptions"/> property: constructing it requires decoding a legacy <see cref="SearchOptions.ContinuationToken"/> and driving a two-phase retry loop, which is adapter logic living in a different layer — a real architectural boundary, not an omission.</description></item>
    /// <item><term><see cref="LowerOptions.CountPhaseScoped"/></term><description>The <c>countPhaseScoped</c> method parameter, the same class as <see cref="LowerOptions.OffsetPage"/> immediately above — the compiler-side half of two-phase sort execution, orchestrated by the caller.</description></item>
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

        var resolved = await Resolve.RunAsync(
            options.Expression,
            options.Include,
            options.RevInclude,
            options.Sort,
            resolver,
            resourceType,
            cancellationToken,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            // A system-level caller that resolved _type before compiling passes those names here rather
            // than in the expression tree, so nothing collects them and they would resolve to the
            // unmatchable sentinel -- a base set of IN (-1, -1) that emits cleanly and matches nothing.
            // The same list is forwarded to LowerOptions.ResourceTypes below; both halves are required.
            additionalResourceTypes: options.ResourceTypes,
            // Likewise both halves: the constraints are forwarded to LowerOptions.AccessConstraints below
            // so they are enforced, and here so the symbols their predicates reference actually resolve.
            accessConstraints: options.AccessConstraints,
            // Both halves again: the allow-list is forwarded to LowerOptions.AllowedResourceTypes below so it
            // is enforced, and here so each permitted type's name resolves to the id Lower needs (an unknown
            // one keeps the unmatchable sentinel rather than being silently dropped).
            allowedResourceTypes: options.AllowedResourceTypes);

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
                    includeLimit,
                    options.Sort,
                    sortPhase,
                    page: null,
                    new LowerOptions
                    {
                        CountOnly = countOnly,
                        ApproximationReferenceTime = approximationReferenceTime,
                        SystemLevelSearch = resourceType is null,

                        // Without this forwarding a multi-_type search silently returns EVERY resource type
                        // rather than the requested subset: the cross-type leaves carry no ResourceTypeId of
                        // their own, so nothing else narrows them. Same class of defect as the AccessConstraints
                        // omission above -- accepted by the API, never reaching Lower, invisible to a green build.
                        // See CompileFromOptionsTests.
                        ResourceTypes = options.ResourceTypes,
                        OffsetPage = offsetPage,
                        CountPhaseScoped = countPhaseScoped,
                        SurrogateRange = ToSurrogateRange(surrogateIdRange, options),
                        // The forwarding this task exists for: without it, a caller setting AccessConstraints
                        // on options gets silent non-enforcement -- the constraint is accepted by the API but
                        // never reaches Lower, so nothing narrows the match set or guards an include/chain
                        // target. See CompileFromOptionsTests.
                        AccessConstraints = options.AccessConstraints,

                        // The security control this task exists for. Same class of defect as
                        // AccessConstraints above if omitted: a caller setting AllowedResourceTypes (the
                        // resource types a SMART clinical scope grants) would have it accepted by the API but
                        // never reach Lower, so nothing would gate the match set or the include stages and an
                        // _include could return a type the scope never permitted -- a fail-open authorization
                        // bypass. See CompileFromOptionsTests.
                        AllowedResourceTypes = options.AllowedResourceTypes,

                        // Same class of defect as ResourceTypes/AccessConstraints above: without this, a
                        // caller setting ResourceVersionTypes = History (or SoftDeleted) gets silent
                        // Latest-only results -- the control is accepted by the API but never reaches Lower.
                        // See CompileFromOptionsTests.
                        Visibility = ToVisibility(options.ResourceVersionTypes),
                    });

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

    /// <summary>
    /// Resolves the surrogate-id bound this compile should apply: the explicit <paramref name="explicitRange"/>
    /// method parameter when the caller supplied one, otherwise a fallback onto
    /// <see cref="SearchOptions.StartSurrogateId"/>/<see cref="SearchOptions.EndSurrogateId"/> -- the fourth
    /// instance of the same defect class as <see cref="AccessConstraints"/>, <see cref="ResourceTypes"/>, and
    /// <see cref="ResourceVersionTypes"/>: a <see cref="SearchOptions"/> property that looked live and reached
    /// nothing. The explicit parameter wins when both are supplied, matching the adapter-parameter boundary
    /// <see cref="LowerOptions.OffsetPage"/> uses.
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
